using MyAws.Core.Models;

namespace MyAws.Core.Services;

public interface IEc2Service
{
    Task<List<ImageInfo>> GetOwnedImagesAsync(string ownerId, CancellationToken ct = default);
    Task<List<InstanceInfo>> GetAllInstancesAsync(CancellationToken ct = default);
    Task<string> RunInstanceAsync(string imageId, string instanceType, string keyName, string securityGroupId, CancellationToken ct = default);
    Task StartInstanceAsync(string instanceId, CancellationToken ct = default);
    Task StopInstanceAsync(string instanceId, CancellationToken ct = default);
    Task TerminateInstanceAsync(string instanceId, CancellationToken ct = default);
    Task TerminateInstancesAsync(IEnumerable<string> instanceIds, CancellationToken ct = default);
    Task<string> CreateImageAsync(string instanceId, string name, CancellationToken ct = default);
    Task DeregisterImageAsync(string imageId, CancellationToken ct = default);
    Task DeleteSnapshotAsync(string snapshotId, CancellationToken ct = default);
    Task WaitForInstanceRunningAsync(string instanceId, CancellationToken ct = default);
    Task WaitForInstanceStoppedAsync(string instanceId, CancellationToken ct = default);
    Task WaitForImageAvailableAsync(string imageId, CancellationToken ct = default);
    Task<string> GetConsoleOutputAsync(string instanceId, CancellationToken ct = default);
    Task<byte[]> GetConsoleScreenshotAsync(string instanceId, CancellationToken ct = default);
    Task<(int VolumesCount, long VolumesGb)> GetVolumesSummaryAsync(CancellationToken ct = default);
    Task<(int SnapshotsCount, long SnapshotsGb)> GetSnapshotsSummaryAsync(string ownerId, CancellationToken ct = default);
    Task<InstanceInfo> DescribeInstanceAsync(string instanceId, CancellationToken ct = default);
}
