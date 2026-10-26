using System.Windows.Interop;
using static CtrlCpp.Interop.NativeConstants;
using static CtrlCpp.Interop.NativeMethods;

namespace CtrlCpp.Services;

/// <summary>消息窗口接收 WM_CLIPBOARDUPDATE，将读取任务转交 STA 工作线程。</summary>
public sealed class ClipboardMonitorService : IDisposable
{
    private HwndSource? _source;
    private ClipboardReader? _reader;

    /// <summary>PasteService 写回剪贴板前设置，抑制对自己的写入做记录。</summary>
    public DateTime SuppressUntilUtc { get; set; } = DateTime.MinValue;

    public void Start(ClipboardReader reader)
    {
        _reader = reader;
        var prm = new HwndSourceParameters("CtrlCppClipWnd", 0, 0)
        {
            ParentWindow = new IntPtr(HWND_MESSAGE),
        };
        _source = new HwndSource(prm);
        _source.AddHook(WndProc);
        _ = AddClipboardFormatListener(_source.Handle);
        LogService.Log("剪贴板监听已启动");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_CLIPBOARDUPDATE) return IntPtr.Zero;
        handled = true;
        if (DateTime.UtcNow < SuppressUntilUtc) return IntPtr.Zero;
        var owner = GetClipboardOwner();
        if (owner != IntPtr.Zero)
        {
            _ = GetWindowThreadProcessId(owner, out var pid);
            if (pid == (uint)Environment.ProcessId) return IntPtr.Zero;
        }
        _reader?.Enqueue();
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        try
        {
            if (_source != null) _ = RemoveClipboardFormatListener(_source.Handle);
            _source?.Dispose();
        }
        catch { /* 忽略 */ }
    }
}
