using System.IO;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CtrlCpp.Models;

namespace CtrlCpp.Services;

/// <summary>
/// 专用 STA 工作线程上读取剪贴板（隔离 Excel 等延迟渲染可能造成的长时间阻塞）。
/// </summary>
public sealed class ClipboardReader : IDisposable
{
    private const long MaxImageBytes = 64L * 1024 * 1024;
    private const int MaxTextChars = 1_000_000;
    private const int MaxPreviewChars = 256;

    private readonly ConcurrentQueue<DateTime> _queue = new();
    private readonly AutoResetEvent _pulse = new(false);
    private Thread? _thread;
    private volatile bool _stop;

    /// <summary>在工作线程上触发；订阅方需自行保证线程安全。</summary>
    public event Action<ClipboardPayload?>? ItemRead;

    public void Start()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "CtrlCpp-ClipReader",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Enqueue()
    {
        _queue.Enqueue(DateTime.UtcNow);
        _ = _pulse.Set();
    }

    private void Loop()
    {
        while (!_stop)
        {
            try { _ = _pulse.WaitOne(2000); }
            catch { /* 忽略 */ }
            if (_stop) break;
            while (_queue.TryDequeue(out _))
            {
                ClipboardPayload? payload = null;
                try { payload = ReadOnce(); }
                catch (Exception ex) { LogService.LogEx("读取剪贴板失败", ex); }
                try { ItemRead?.Invoke(payload); }
                catch (Exception ex) { LogService.LogEx("处理剪贴板失败", ex); }
            }
        }
    }

    private ClipboardPayload? ReadOnce()
    {
        var sourceApp = SourceAppDetector.GetOwnerExeName();

        IDataObject? dao = null;
        for (var i = 0; i < 5 && dao == null; i++)
        {
            try { dao = Clipboard.GetDataObject(); }
            catch { Thread.Sleep(40); }
        }
        if (dao == null) return null;

        string[] formats;
        try { formats = dao.GetFormats(); }
        catch (Exception ex) { LogService.LogEx("获取剪贴板格式失败", ex); return null; }

        // 1. 文件列表（资源管理器复制文件时同时带文字路径，优先按文件处理）
        if (Array.Exists(formats, f => f == DataFormats.FileDrop))
        {
            try
            {
                if (dao.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                    return BuildFiles(files, sourceApp);
            }
            catch (Exception ex) { LogService.LogEx("读取文件列表失败", ex); }
        }

        // 2. 图片
        var hasPng = Array.Exists(formats, f => f == "PNG");
        var hasBmp = Array.Exists(formats, f => f == DataFormats.Bitmap)
                     || Array.Exists(formats, f => f == DataFormats.Dib);
        if (hasPng || hasBmp)
        {
            var png = TryGetImagePng(dao, hasPng);
            if (png != null) return BuildImage(png, sourceApp);
        }

        // 3. 文本 / 链接
        if (Array.Exists(formats, f => f == DataFormats.UnicodeText)
            || Array.Exists(formats, f => f == DataFormats.Text))
        {
            try
            {
                if (dao.GetData(DataFormats.UnicodeText) is string s && s.Length > 0)
                    return BuildText(s, sourceApp);
            }
            catch (Exception ex) { LogService.LogEx("读取文本失败", ex); }
        }

        return null;
    }

    private static byte[]? TryGetImagePng(IDataObject dao, bool preferPng)
    {
        if (preferPng)
        {
            try
            {
                if (dao.GetData("PNG") is MemoryStream ms)
                {
                    var bytes = ms.ToArray();
                    if (bytes.Length <= MaxImageBytes && LooksLikePng(bytes))
                    {
                        // 某些截图工具的 PNG 透明度全为 0，但 RGB 数据有效
                        using var decodeMs = new MemoryStream(bytes, false);
                        var frame = BitmapFrame.Create(decodeMs,
                            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        var opaque = ForceOpaqueWhenAllAlphaZero(frame);
                        return opaque == null ? bytes : EncodePng(opaque);
                    }
                }
            }
            catch { /* 回退到 DIB */ }
        }
        try
        {
            // 基于已取的快照解码，避免回退路径重读活动剪贴板造成内容与来源错配
            if (dao.GetData(DataFormats.Bitmap) is not System.Windows.Media.Imaging.BitmapSource src || src == null) return null;
            if (src.PixelWidth > 8192 || src.PixelHeight > 8192) return null;
            var normalized = ForceOpaqueWhenAllAlphaZero(src) ?? src;
            return EncodePng(normalized);
        }
        catch (Exception ex)
        {
            LogService.LogEx("读取剪贴板图片失败", ex);
            return null;
        }
    }

    private static byte[]? EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var outMs = new MemoryStream();
        encoder.Save(outMs);
        var png = outMs.ToArray();
        return png.Length <= MaxImageBytes ? png : null;
    }

    /// <summary>仅在整图 alpha 都为 0 时改为不透明，保留 RGB；正常透明图片不变。</summary>
    private static BitmapSource? ForceOpaqueWhenAllAlphaZero(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var rawSize = (long)width * height * 4;
        if (rawSize <= 0 || rawSize > 128 * 1024 * 1024) return null;

        var stride = width * 4;
        var pixels = new byte[(int)rawSize];
        converted.CopyPixels(pixels, stride, 0);
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) return null;

        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var opaque = BitmapSource.Create(width, height, converted.DpiX, converted.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        opaque.Freeze();
        return opaque;
    }

    private static bool LooksLikePng(byte[] bytes) =>
        bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    private static ClipboardPayload BuildText(string s, string? sourceApp)
    {
        var normalized = s.Replace("\r\n", "\n");
        var trimmed = normalized.Trim();
        var isUrl = trimmed.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^https?://\S+$");
        var truncated = s.Length > MaxTextChars;
        var full = truncated ? s[..MaxTextChars] : s;
        var preview = normalized.Replace("\n", " ").Trim();
        if (preview.Length > MaxPreviewChars) preview = preview[..MaxPreviewChars];
        // 哈希与存储内容保持一致（截断后），预览明示截断，避免回粘出"静默缺尾"的内容
        if (truncated) preview = preview.Length > MaxPreviewChars - 10
            ? preview[..(MaxPreviewChars - 10)] + "…[已截断]"
            : preview + "…[已截断]";
        var hashInput = (isUrl ? "U:" : "T:") + full.Replace("\r\n", "\n");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
        return new ClipboardPayload
        {
            Kind = isUrl ? ItemKind.Url : ItemKind.Text,
            ContentHash = hash,
            Preview = preview,
            FullText = full,
            ByteSize = Encoding.UTF8.GetByteCount(full) + 128,
            SourceApp = sourceApp,
        };
    }

    private static ClipboardPayload BuildImage(byte[] png, string? sourceApp)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("I:" + Convert.ToBase64String(SHA256.HashData(png)))));
        // PNG IHDR：宽高分别位于第 16-20、20-24 字节（大端）
        int width = 0, height = 0;
        if (png.Length >= 24)
        {
            width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        }
        return new ClipboardPayload
        {
            Kind = ItemKind.Image,
            ContentHash = hash,
            Preview = "图片",
            PngBytes = png,
            Width = width,
            Height = height,
            ByteSize = png.Length + 128,
            SourceApp = sourceApp,
        };
    }

    private static ClipboardPayload BuildFiles(string[] files, string? sourceApp)
    {
        var json = JsonSerializer.Serialize(files);
        var hashInput = "F:" + string.Join("\n", files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
        var names = files.Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n)).ToArray();
        var head = string.Join(", ", names.Take(3));
        var preview = names.Length <= 3
            ? $"{names.Length} 个文件: {head}"
            : $"{names.Length} 个文件: {head} 等";
        if (preview.Length > MaxPreviewChars) preview = preview[..MaxPreviewChars];
        return new ClipboardPayload
        {
            Kind = ItemKind.Files,
            ContentHash = hash,
            Preview = preview,
            FileListJson = json,
            ByteSize = Encoding.UTF8.GetByteCount(json) + 128,
            SourceApp = sourceApp,
        };
    }

    public void Dispose()
    {
        _stop = true;
        try { _ = _pulse.Set(); } catch { /* 忽略 */ }
    }
}
