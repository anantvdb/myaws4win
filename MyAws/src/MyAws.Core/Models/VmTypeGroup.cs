namespace MyAws.Core.Models;

public sealed class VmTypeOption
{
    public string Suffix { get; set; } = "";
    public string Description { get; set; } = "";

    public string FullName(string groupPrefix) => groupPrefix + Suffix;
}

public sealed class VmTypeGroup
{
    public string Prefix { get; set; } = "";
    public List<VmTypeOption> Types { get; set; } = [];
}
