namespace CtrlCpp.Models;

public sealed class ClipboardItem
{
    public long Id { get; set; }
    public ItemKind Kind { get; set; }
    public string ContentHash { get; set; } = "";
    public string Preview { get; set; } = "";
    public string? FullText { get; set; }
    public string? MediaPath { get; set; }
    public string? FileListJson { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long ByteSize { get; set; }
    public string? SourceApp { get; set; }
    public long FirstSeenAt { get; set; }
    public long LastSeenAt { get; set; }
}
