namespace MyAws.Core.Models;

public sealed class MonthlyCostItem
{
    public string ServiceName { get; set; } = "";
    public decimal Amount { get; set; }
    public string Unit { get; set; } = "USD";
}

public sealed class DailyCostItem
{
    public string Date { get; set; } = "";
    public decimal Amount { get; set; }
}

public sealed class AppSnapshot
{
    public DateTime Timestamp { get; set; }
    public List<ImageInfo> Images { get; set; } = [];
    public Dictionary<string, List<InstanceInfo>> InstancesByImage { get; set; } = [];
    public int VolumesCount { get; set; }
    public long VolumesGb { get; set; }
    public int SnapshotsCount { get; set; }
    public long SnapshotsGb { get; set; }
    public decimal MonthlyTotal { get; set; }
    public List<MonthlyCostItem> MonthlyCostItems { get; set; } = [];
    public List<DailyCostItem> DailyCostItems { get; set; } = [];
}
