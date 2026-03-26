using Amazon.EC2;
using Amazon.EC2.Model;
using MyAws.Core.Models;

namespace MyAws.Core.Services;

public sealed class Ec2Service : IEc2Service
{
    private readonly IAmazonEC2 _ec2;

    public Ec2Service(IAmazonEC2 ec2)
    {
        _ec2 = ec2;
    }

    public async Task<List<ImageInfo>> GetOwnedImagesAsync(string ownerId, CancellationToken ct = default)
    {
        var response = await _ec2.DescribeImagesAsync(new DescribeImagesRequest
        {
            Owners = [ownerId],
        }, ct);

        return response.Images.Select(img => new ImageInfo
        {
            ImageId = img.ImageId,
            Name = img.Name ?? "unnamed",
            SnapshotId = img.BlockDeviceMappings.FirstOrDefault()?.Ebs?.SnapshotId,
        }).ToList();
    }

    public async Task<List<InstanceInfo>> GetAllInstancesAsync(CancellationToken ct = default)
    {
        var result = new List<InstanceInfo>();
        string? nextToken = null;

        do
        {
            var response = await _ec2.DescribeInstancesAsync(new DescribeInstancesRequest
            {
                NextToken = nextToken,
            }, ct);

            foreach (var reservation in response.Reservations)
            foreach (var instance in reservation.Instances)
            {
                result.Add(new InstanceInfo
                {
                    InstanceId = instance.InstanceId,
                    ImageId = instance.ImageId,
                    State = instance.State?.Name?.Value ?? "unknown",
                    InstanceType = instance.InstanceType?.Value ?? "",
                    PublicDns = instance.PublicDnsName ?? "",
                    PublicIp = instance.PublicIpAddress ?? "",
                    LaunchTime = instance.LaunchTime,
                });
            }

            nextToken = response.NextToken;
        } while (!string.IsNullOrEmpty(nextToken));

        return result;
    }

    public async Task<string> RunInstanceAsync(string imageId, string instanceType, string keyName, string securityGroupId, CancellationToken ct = default)
    {
        var response = await _ec2.RunInstancesAsync(new RunInstancesRequest
        {
            ImageId = imageId,
            InstanceType = instanceType,
            MinCount = 1,
            MaxCount = 1,
            KeyName = keyName,
            SecurityGroupIds = [securityGroupId],
            EbsOptimized = true,
        }, ct);

        return response.Reservation.Instances[0].InstanceId;
    }

    public async Task StartInstanceAsync(string instanceId, CancellationToken ct = default)
    {
        await _ec2.StartInstancesAsync(new StartInstancesRequest
        {
            InstanceIds = [instanceId],
        }, ct);
    }

    public async Task StopInstanceAsync(string instanceId, CancellationToken ct = default)
    {
        await _ec2.StopInstancesAsync(new StopInstancesRequest
        {
            InstanceIds = [instanceId],
            Force = true,
        }, ct);
    }

    public async Task TerminateInstanceAsync(string instanceId, CancellationToken ct = default)
    {
        await _ec2.TerminateInstancesAsync(new TerminateInstancesRequest
        {
            InstanceIds = [instanceId],
        }, ct);
    }

    public async Task TerminateInstancesAsync(IEnumerable<string> instanceIds, CancellationToken ct = default)
    {
        var ids = instanceIds.ToList();
        if (ids.Count == 0) return;

        await _ec2.TerminateInstancesAsync(new TerminateInstancesRequest
        {
            InstanceIds = ids,
        }, ct);
    }

    public async Task<string> CreateImageAsync(string instanceId, string name, CancellationToken ct = default)
    {
        var response = await _ec2.CreateImageAsync(new CreateImageRequest
        {
            InstanceId = instanceId,
            Name = name,
        }, ct);

        return response.ImageId;
    }

    public async Task DeregisterImageAsync(string imageId, CancellationToken ct = default)
    {
        await _ec2.DeregisterImageAsync(new DeregisterImageRequest
        {
            ImageId = imageId,
        }, ct);
    }

    public async Task DeleteSnapshotAsync(string snapshotId, CancellationToken ct = default)
    {
        await _ec2.DeleteSnapshotAsync(new DeleteSnapshotRequest
        {
            SnapshotId = snapshotId,
        }, ct);
    }

    public async Task WaitForInstanceRunningAsync(string instanceId, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var info = await DescribeInstanceAsync(instanceId, ct);
            if (info.State == "running") return;
            await Task.Delay(5000, ct);
        }
    }

    public async Task WaitForInstanceStoppedAsync(string instanceId, CancellationToken ct = default)
    {
        for (int i = 0; i < 60; i++)
        {
            var info = await DescribeInstanceAsync(instanceId, ct);
            if (info.State.Equals("stopped", StringComparison.OrdinalIgnoreCase)) return;
            await Task.Delay(5000, ct);
        }
        throw new TimeoutException($"Instance {instanceId} did not stop within 5 minutes.");
    }

    public async Task WaitForImageAvailableAsync(string imageId, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var response = await _ec2.DescribeImagesAsync(new DescribeImagesRequest
            {
                ImageIds = [imageId],
            }, ct);

            var image = response.Images.FirstOrDefault();
            if (image?.State?.Value == "available") return;
            await Task.Delay(10000, ct);
        }
    }

    public async Task<string> GetConsoleOutputAsync(string instanceId, CancellationToken ct = default)
    {
        var response = await _ec2.GetConsoleOutputAsync(new GetConsoleOutputRequest
        {
            InstanceId = instanceId,
        }, ct);

        return response.Output ?? "";
    }

    public async Task<byte[]> GetConsoleScreenshotAsync(string instanceId, CancellationToken ct = default)
    {
        var response = await _ec2.GetConsoleScreenshotAsync(new GetConsoleScreenshotRequest
        {
            InstanceId = instanceId,
        }, ct);

        if (string.IsNullOrEmpty(response.ImageData))
            return [];

        return Convert.FromBase64String(response.ImageData);
    }

    public async Task<(int VolumesCount, long VolumesGb)> GetVolumesSummaryAsync(CancellationToken ct = default)
    {
        var count = 0;
        var gb = 0L;
        string? nextToken = null;

        do
        {
            var response = await _ec2.DescribeVolumesAsync(new DescribeVolumesRequest
            {
                NextToken = nextToken,
            }, ct);
            count += response.Volumes.Count;
            gb += response.Volumes.Sum(v => (long)(v.Size ?? 0));
            nextToken = response.NextToken;
        } while (!string.IsNullOrEmpty(nextToken));

        return (count, gb);
    }

    public async Task<(int SnapshotsCount, long SnapshotsGb)> GetSnapshotsSummaryAsync(string ownerId, CancellationToken ct = default)
    {
        var count = 0;
        var gb = 0L;
        string? nextToken = null;

        do
        {
            var response = await _ec2.DescribeSnapshotsAsync(new DescribeSnapshotsRequest
            {
                OwnerIds = [ownerId],
                NextToken = nextToken,
            }, ct);
            count += response.Snapshots.Count;
            gb += response.Snapshots.Sum(s => (long)(s.VolumeSize ?? 0));
            nextToken = response.NextToken;
        } while (!string.IsNullOrEmpty(nextToken));

        return (count, gb);
    }

    public async Task<InstanceInfo> DescribeInstanceAsync(string instanceId, CancellationToken ct = default)
    {
        var response = await _ec2.DescribeInstancesAsync(new DescribeInstancesRequest
        {
            InstanceIds = [instanceId],
        }, ct);

        var instance = response.Reservations[0].Instances[0];
        return new InstanceInfo
        {
            InstanceId = instance.InstanceId,
            ImageId = instance.ImageId,
            State = instance.State?.Name?.Value ?? "unknown",
            InstanceType = instance.InstanceType?.Value ?? "",
            PublicDns = instance.PublicDnsName ?? "",
            PublicIp = instance.PublicIpAddress ?? "",
            LaunchTime = instance.LaunchTime,
        };
    }
}
