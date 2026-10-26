namespace CtrlCpp.Models;

/// <summary>解析后的剪贴板内容（在专用 STA 工作线程上产生）。</summary>
public sealed class ClipboardPayload
{
    public ItemKind Kind { get; init; }
    public string ContentHash { get; init; } = "";
    public string Preview { get; init; } = "";
    public string? FullText { get; init; }
    public byte[]? PngBytes { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? FileListJson { get; init; }
    public long ByteSize { get; init; }
    public string? SourceApp { get; init; }

    /// <summary>仅用于 --add-dummy 测试数据注入。</summary>
    public long LastSeenAtOverride { get; init; }
}
