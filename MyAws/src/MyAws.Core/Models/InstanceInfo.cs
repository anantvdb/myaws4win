namespace MyAws.Core.Models;

public sealed class InstanceInfo
{
    public string InstanceId { get; set; } = "";
    public string ImageId { get; set; } = "";
    public string State { get; set; } = "";
    public string InstanceType { get; set; } = "";
    public string PublicDns { get; set; } = "";
    public string PublicIp { get; set; } = "";
    public DateTime? LaunchTime { get; set; }
}
