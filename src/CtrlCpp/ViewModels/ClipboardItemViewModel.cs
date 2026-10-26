using System.ComponentModel;
using System.Windows;
using System.Text.Json;
using System.Windows.Media;
using System.IO;
using CtrlCpp.Models;
using CtrlCpp.Services;

namespace CtrlCpp.ViewModels;

/// <summary>历史列表中的一行。缩略图/文件图标按需惰性解码。</summary>
public sealed class ClipboardItemViewModel : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private bool _thumbnailLoaded;
    private ImageSource? _fileIcon;
    private bool _fileIconLoaded;

    public ClipboardItem Item { get; }

    public ClipboardItemViewModel(ClipboardItem item) => Item = item;

    public long Id => Item.Id;
    public ItemKind Kind => Item.Kind;
    public string FullText => Item.FullText ?? "";

    public string KindGlyph => Kind switch
    {
        ItemKind.Url => "",   // Link
        ItemKind.Image => "", // Photo
        ItemKind.Files => "", // Folder
        _ => "",              // Document
    };

    /// <summary>文字/链接显示设计稿的矢量文档图标，图片/文件暂用系统图标。</summary>
    public Visibility DocIconVisibility =>
        Kind is ItemKind.Text or ItemKind.Url ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GlyphVisibility =>
        Kind is ItemKind.Image or ItemKind.Files ? Visibility.Visible : Visibility.Collapsed;
    public string PreviewText => Kind switch
    {
        ItemKind.Text or ItemKind.Url => Item.FullText ?? Item.Preview,
        ItemKind.Image => "图片",
        ItemKind.Files => DescribeFiles(),
        _ => Item.Preview,
    };

    public string Caption
    {
        get
        {
            var time = FormatRelative(Item.LastSeenAt);
            return Kind == ItemKind.Image
                ? $"{Item.Width}×{Item.Height} · {FormatUtil.FormatSize(Item.ByteSize)} · {time}"
                : $"来源 {PrettyApp(Item.SourceApp)} · {time}";
        }
    }

    /// <summary>图片缩略图（解码失败返回 null，模板里显示类型图标）。</summary>
    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded) return _thumbnail;
            _thumbnailLoaded = true;
            if (Kind == ItemKind.Image && Item.MediaPath != null)
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 112;
                    bmp.UriSource = new Uri(AppServices.Media.ResolveFullPath(Item.MediaPath));
                    bmp.EndInit();
                    bmp.Freeze();
                    _thumbnail = bmp;
                }
                catch { /* 媒体可能已被清理 */ }
            }
            return _thumbnail;
        }
    }

    /// <summary>文件类型图标（仅 Files 行）。</summary>
    public ImageSource? FileIcon
    {
        get
        {
            if (_fileIconLoaded) return _fileIcon;
            _fileIconLoaded = true;
            if (Kind == ItemKind.Files)
            {
                try
                {
                    var files = CtrlCpp.Services.PasteService.ParseFiles(Item.FileListJson);
                    if (files.Count > 0)
                        _fileIcon = AppServices.FileIcons.Get(Path.GetExtension(files[0]));
                }
                catch { /* 忽略 */ }
            }
            return _fileIcon;
        }
    }

    private string DescribeFiles()
    {
        try
        {
            var names = JsonSerializer.Deserialize<string[]>(Item.FileListJson ?? "[]") ?? Array.Empty<string>();
            var head = string.Join(", ", names.Take(3).Select(Path.GetFileName));
            return names.Length <= 3
                ? $"{names.Length} 个文件: {head}"
                : $"{names.Length} 个文件: {head} 等";
        }
        catch { return "文件"; }
    }

    public void RefreshTime() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Caption)));

    public event PropertyChangedEventHandler? PropertyChanged;

    internal static string PrettyApp(string? exe)
    {
        if (string.IsNullOrEmpty(exe)) return "未知";
        return exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe;
    }

    internal static string FormatRelative(long lastSeenTicks)
    {
        var t = DateTime.UtcNow - new DateTime(lastSeenTicks, DateTimeKind.Utc);
        if (t.TotalSeconds < 60) return "刚刚";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} 分钟前";
        if (t.TotalHours < 24) return $"{(int)t.TotalHours} 小时前";
        if (t.TotalDays < 7) return $"{(int)t.TotalDays} 天前";
        return new DateTime(lastSeenTicks, DateTimeKind.Utc).ToLocalTime().ToString("M月d日");
    }
}

public static class FormatUtil
{
    public static string FormatSize(long b)
    {
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:0.#} KB";
        if (b < 1024L * 1024 * 1024) return $"{b / 1048576.0:0.#} MB";
        return $"{b / 1073741824.0:0.##} GB";
    }
}
