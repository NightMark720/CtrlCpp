using CtrlCpp.Models;

namespace CtrlCpp.ViewModels;

/// <summary>--dump 调试输出的精简行。</summary>
public sealed record ItemDump(
    long Id,
    int Kind,
    string Preview,
    int FullTextLength,
    string? MediaPath,
    string? FileListJson,
    int Width,
    int Height,
    long ByteSize,
    string? SourceApp,
    string FirstSeenAt,
    string LastSeenAt)
{
    public static ItemDump From(ClipboardItem i) => new(
        i.Id,
        (int)i.Kind,
        i.Preview.Length > 80 ? i.Preview[..80] : i.Preview,
        i.FullText?.Length ?? 0,
        i.MediaPath,
        i.FileListJson,
        i.Width,
        i.Height,
        i.ByteSize,
        i.SourceApp,
        new DateTime(i.FirstSeenAt, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
        new DateTime(i.LastSeenAt, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
}
