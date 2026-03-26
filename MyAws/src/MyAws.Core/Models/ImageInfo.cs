namespace MyAws.Core.Models;

public sealed class ImageInfo
{
    public string ImageId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? SnapshotId { get; set; }
}
