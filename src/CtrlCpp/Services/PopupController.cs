using System.Runtime.InteropServices;
using CtrlCpp.Interop;
using CtrlCpp.ViewModels;
using CtrlCpp.Views;
using static CtrlCpp.Interop.NativeConstants;
using static CtrlCpp.Interop.NativeMethods;

namespace CtrlCpp.Services;

/// <summary>历史弹出窗口的显示/隐藏/切换与回粘流程控制。</summary>
public sealed class PopupController
{
    private HistoryPopupWindow? _window;

    /// <summary>弹窗打开前的前台窗口；回粘时切回这里。</summary>
    public IntPtr PrevHwnd { get; private set; }

    /// <summary>粘贴流程/右键菜单期间抑制“失焦自动关闭”。</summary>
    public bool SuppressClose { get; set; }

    public bool IsOpen => _window?.IsVisible ?? false;

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    public void Show()
    {
        if (_window == null) _window = new HistoryPopupWindow(this);
        if (_window.IsVisible)
        {
            _window.Activate();
            return;
        }
        PrevHwnd = GetForegroundWindow();
        LogService.Log($"打开历史窗口，前一窗口 0x{PrevHwnd.ToInt64():X}");
        // 打开时暂时抑制失焦关闭，等弹窗稳定后再允许
        SuppressClose = true;
        _window.PrepareShow();
        _window.Show();
        _window.Activate();
        // 延迟解除抑制，避免任务栏按钮等抢占焦点导致立即关闭
        Task.Delay(300).ContinueWith(_ => _window.Dispatcher.BeginInvoke(() =>
        {
            if (IsOpen) SuppressClose = false;
        }));
    }

    public void Hide()
    {
        SuppressClose = false;
        var prev = PrevHwnd;
        PrevHwnd = IntPtr.Zero;
        if (_window != null && _window.IsVisible) _window.Hide();
        if (prev != IntPtr.Zero && IsWindow(prev)) _ = SetForegroundWindow(prev);
    }

    public void RequestPaste(ClipboardItemViewModel vm)
    {
        if (_window == null) return;
        var prev = PrevHwnd;
        SuppressClose = true;
        var written = false;
        try
        {
            AppServices.Monitor.SuppressUntilUtc = DateTime.UtcNow.AddMilliseconds(800);
            PasteService.WriteToClipboard(vm.Item);
            written = true;
            LogService.Log($"写回剪贴板 #{vm.Id} kind={vm.Kind}");
        }
        catch (Exception ex) { LogService.LogEx("写回剪贴板失败，已中止自动粘贴", ex); }
        _window.Hide();
        if (!written)
        {
            // 写回失败时绝不发送 Ctrl+V，避免把旧剪贴板内容当成所选记录粘出去
            SuppressClose = false;
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var ok = await PasteService.RestoreFocusAndPasteAsync(prev);
                LogService.Log(ok ? "已自动粘贴" : "焦点恢复失败，已更新剪贴板但未自动粘贴");
            }
            catch (Exception ex) { LogService.LogEx("自动粘贴失败", ex); }
            finally { SuppressClose = false; }
        });
    }

    public void CopyWithoutClosing(ClipboardItemViewModel vm)
    {
        try
        {
            AppServices.Monitor.SuppressUntilUtc = DateTime.UtcNow.AddMilliseconds(800);
            PasteService.WriteToClipboard(vm.Item);
            LogService.Log($"仅复制 #{vm.Id} 到剪贴板");
        }
        catch (Exception ex) { LogService.LogEx("复制内容失败", ex); }
    }
}
