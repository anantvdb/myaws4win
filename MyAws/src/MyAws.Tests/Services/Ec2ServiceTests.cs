using Amazon.EC2;
using Amazon.EC2.Model;
using FluentAssertions;
using NSubstitute;
using MyAws.Core.Services;

namespace MyAws.Tests.Services;

public class Ec2ServiceTests
{
    private readonly IAmazonEC2 _mockEc2 = Substitute.For<IAmazonEC2>();
    private readonly Ec2Service _sut;

    public Ec2ServiceTests()
    {
        _sut = new Ec2Service(_mockEc2);
    }

    [Fact]
    public async Task GetOwnedImages_MapsResponseToImageInfo()
    {
        _mockEc2.DescribeImagesAsync(Arg.Any<DescribeImagesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DescribeImagesResponse
            {
                Images =
                [
                    new Image
                    {
                        ImageId = "ami-123",
                        Name = "TestImage",
                        BlockDeviceMappings =
                        [
                            new BlockDeviceMapping
                            {
                                Ebs = new EbsBlockDevice { SnapshotId = "snap-abc" }
                            }
                        ]
                    }
                ]
            });

        var result = await _sut.GetOwnedImagesAsync("owner-123");

        result.Should().HaveCount(1);
        result[0].ImageId.Should().Be("ami-123");
        result[0].Name.Should().Be("TestImage");
        result[0].SnapshotId.Should().Be("snap-abc");
    }

    [Fact]
    public async Task GetAllInstances_MapsResponseToInstanceInfo()
    {
        _mockEc2.DescribeInstancesAsync(Arg.Any<DescribeInstancesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DescribeInstancesResponse
            {
                Reservations =
                [
                    new Reservation
                    {
                        Instances =
                        [
                            new Instance
                            {
                                InstanceId = "i-001",
                                ImageId = "ami-123",
                                State = new InstanceState { Name = InstanceStateName.Running },
                                InstanceType = InstanceType.M5Xlarge,
                                PublicDnsName = "ec2-1-2-3-4.compute.amazonaws.com",
                                PublicIpAddress = "1.2.3.4",
                                LaunchTime = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                            }
                        ]
                    }
                ]
            });

        var result = await _sut.GetAllInstancesAsync();

        result.Should().HaveCount(1);
        result[0].InstanceId.Should().Be("i-001");
        result[0].State.Should().Be("running");
        result[0].PublicIp.Should().Be("1.2.3.4");
    }

    [Fact]
    public async Task RunInstance_ReturnsInstanceId()
    {
        _mockEc2.RunInstancesAsync(Arg.Any<RunInstancesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RunInstancesResponse
            {
                Reservation = new Reservation
                {
                    Instances = [new Instance { InstanceId = "i-new" }]
                }
            });

        var id = await _sut.RunInstanceAsync("ami-123", "m5.xlarge", "mykey", "sg-123");
        id.Should().Be("i-new");
    }

    [Fact]
    public async Task GetVolumesSummary_AggregatesCorrectly()
    {
        _mockEc2.DescribeVolumesAsync(Arg.Any<DescribeVolumesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DescribeVolumesResponse
            {
                Volumes =
                [
                    new Volume { Size = 100 },
                    new Volume { Size = 200 },
                ]
            });

        var (count, gb) = await _sut.GetVolumesSummaryAsync();
        count.Should().Be(2);
        gb.Should().Be(300);
    }

    [Fact]
    public async Task TerminateInstances_DoesNothing_WhenEmpty()
    {
        await _sut.TerminateInstancesAsync([]);

        await _mockEc2.DidNotReceive()
            .TerminateInstancesAsync(Arg.Any<TerminateInstancesRequest>(), Arg.Any<CancellationToken>());
    }
}
