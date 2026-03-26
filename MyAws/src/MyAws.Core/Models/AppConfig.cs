namespace MyAws.Core.Models;

public sealed class AppConfig
{
    public string AwsOwnerId { get; set; } = "";
    public string AwsKeyName { get; set; } = "";
    public string AwsSecurityGroupId { get; set; } = "";
    public string AwsRegion { get; set; } = "eu-central-1";
    public string AwsLocationName { get; set; } = "EU (Frankfurt)";
    public string AwsOperatingSystem { get; set; } = "Linux";
    public string AwsProfile { get; set; } = "";
    public string SshUser { get; set; } = "root";
    public string SshKnownHostsFile { get; set; } = "";
    public string PreferredCurrency { get; set; } = "EUR";
    public string DefaultVmTypeUpdate { get; set; } = "m7i.12xlarge";
    public string DefaultVmTypeRebuild { get; set; } = "m7i.48xlarge";
    public string UpdateCommand { get; set; } = "update";
    public string RebuildCommand { get; set; } = "fullupdate";
    public int RefreshIntervalSeconds { get; set; } = 900;
    public List<VmTypeGroup> VmTypes { get; set; } = ConfigDefaults.DefaultVmTypes();
}

public static class ConfigDefaults
{
    public static List<VmTypeGroup> DefaultVmTypes() =>
    [
        new()
        {
            Prefix = "m5",
            Types =
            [
                new() { Suffix = ".4xlarge", Description = "16 vcpu, 64 GB RAM" },
                new() { Suffix = ".12xlarge", Description = "48 vcpu, 192 GB RAM" },
                new() { Suffix = ".24xlarge", Description = "96 vcpu, 384 GB RAM" },
            ]
        },
        new()
        {
            Prefix = "m6i",
            Types =
            [
                new() { Suffix = ".4xlarge", Description = "16 vcpu, 64 GB RAM" },
                new() { Suffix = ".12xlarge", Description = "48 vcpu, 192 GB RAM" },
                new() { Suffix = ".24xlarge", Description = "96 vcpu, 384 GB RAM" },
                new() { Suffix = ".32xlarge", Description = "128 vcpu, 512 GB RAM" },
            ]
        },
        new()
        {
            Prefix = "m7i",
            Types =
            [
                new() { Suffix = ".4xlarge", Description = "16 vcpu, 64 GB RAM" },
                new() { Suffix = ".12xlarge", Description = "48 vcpu, 192 GB RAM" },
                new() { Suffix = ".24xlarge", Description = "96 vcpu, 384 GB RAM" },
                new() { Suffix = ".48xlarge", Description = "192 vcpu, 768 GB RAM" },
            ]
        },
        new()
        {
            Prefix = "c5",
            Types =
            [
                new() { Suffix = ".4xlarge", Description = "16 vcpu, 32 GB RAM" },
                new() { Suffix = ".9xlarge", Description = "36 vcpu, 72 GB RAM" },
                new() { Suffix = ".18xlarge", Description = "72 vcpu, 144 GB RAM" },
                new() { Suffix = ".24xlarge", Description = "96 vcpu, 192 GB RAM" },
            ]
        },
        new()
        {
            Prefix = "c6i",
            Types =
            [
                new() { Suffix = ".4xlarge", Description = "16 vcpu, 32 GB RAM" },
                new() { Suffix = ".12xlarge", Description = "48 vcpu, 96 GB RAM" },
                new() { Suffix = ".24xlarge", Description = "96 vcpu, 192 GB RAM" },
                new() { Suffix = ".32xlarge", Description = "128 vcpu, 256 GB RAM" },
            ]
        },
        new()
        {
            Prefix = "c7i",
            Types =
            [
                new() { Suffix = ".4xlarge", Description = "16 vcpu, 32 GB RAM" },
                new() { Suffix = ".12xlarge", Description = "48 vcpu, 96 GB RAM" },
                new() { Suffix = ".24xlarge", Description = "96 vcpu, 192 GB RAM" },
                new() { Suffix = ".48xlarge", Description = "192 vcpu, 384 GB RAM" },
            ]
        },
        new()
        {
            Prefix = "u-6tb1",
            Types =
            [
                new() { Suffix = ".112xlarge", Description = "448 vcpu, 6 TB RAM" },
            ]
        },
    ];
}
