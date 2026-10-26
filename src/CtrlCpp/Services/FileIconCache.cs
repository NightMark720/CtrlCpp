using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CtrlCpp.Interop;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using static CtrlCpp.Interop.NativeConstants;
using static CtrlCpp.Interop.NativeMethods;

namespace CtrlCpp.Services;

/// <summary>按扩展名缓存文件类型小图标（SHGetFileInfo，不要求文件真实存在）。</summary>
public sealed class FileIconCache
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ImageSource? Get(string extension)
    {
        var ext = (extension ?? "").TrimStart('.').ToLowerInvariant();
        lock (_lock)
        {
            if (_cache.TryGetValue(ext, out var cached)) return cached;
        }

        ImageSource? result = null;
        try
        {
            var shfi = new SHFILEINFO();
            var probe = ext.Length > 0 ? "x." + ext : "x";
            var res = SHGetFileInfo(probe, FILE_ATTRIBUTE_NORMAL, ref shfi,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
            if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
            {
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(
                        shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    result = src;
                }
                finally { _ = DestroyIcon(shfi.hIcon); }
            }
        }
        catch { /* 拿不到图标就显示默认 glyph */ }

        lock (_lock) { _cache[ext] = result; }
        return result;
    }
}
