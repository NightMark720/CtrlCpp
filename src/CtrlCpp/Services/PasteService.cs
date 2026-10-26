using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using CtrlCpp.Interop;
using CtrlCpp.Models;
using static CtrlCpp.Interop.NativeConstants;
using static CtrlCpp.Interop.NativeMethods;

namespace CtrlCpp.Services;

/// <summary>把选中记录写回剪贴板 + 恢复焦点 + 模拟 Ctrl+V。</summary>
public static class PasteService
{
    public static void WriteToClipboard(ClipboardItem item)
    {
        switch (item.Kind)
        {
            case ItemKind.Text:
            case ItemKind.Url:
                WithRetry(() => Clipboard.SetText(item.FullText ?? item.Preview));
                break;

            case ItemKind.Image:
            {
                if (item.MediaPath == null) throw new InvalidOperationException("图片数据缺失");
                var bytes = AppServices.Media.ReadBytes(item.MediaPath);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                WithRetry(() => Clipboard.SetImage(bmp));
                break;
            }

            case ItemKind.Files:
            {
                var files = ParseFiles(item.FileListJson);
                var sc = new StringCollection();
                foreach (var f in files) sc.Add(f);
                if (sc.Count == 0) throw new InvalidOperationException("文件列表为空");
                WithRetry(() => Clipboard.SetFileDropList(sc));
                break;
            }
        }
    }

    /// <summary>剪贴板被其他程序短暂占用（CLIPBRD_E_CANT_OPEN）时短重试。</summary>
    private static void WithRetry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (COMException) when (attempt < 4) { Thread.Sleep(100); }
        }
    }

    public static List<string> ParseFiles(string? json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json ?? "[]") ?? new List<string>(); }
        catch { return new List<string>(); }
    }

    /// <summary>
    /// 切回 prevHwnd 并模拟 Ctrl+V。
    /// 返回 false 表示焦点恢复失败（剪贴板已更新，但未自动粘贴）。
    /// </summary>
    public static async Task<bool> RestoreFocusAndPasteAsync(IntPtr prevHwnd)
    {
        if (prevHwnd == IntPtr.Zero || !IsWindow(prevHwnd)) return false;

        _ = SetForegroundWindow(prevHwnd);
        await Task.Delay(100);

        if (GetForegroundWindow() != prevHwnd)
        {
            // AttachThreadInput 技巧重试一次：必须附加到"当前前台窗口"的线程才能继承前台权限
            var fg = GetForegroundWindow();
            var fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0;
            var ourThread = GetCurrentThreadId();
            if (fgThread != 0 && fgThread != ourThread)
            {
                _ = AttachThreadInput(ourThread, fgThread, true);
                _ = SetForegroundWindow(prevHwnd);
                _ = AttachThreadInput(ourThread, fgThread, false);
            }
            else
            {
                _ = SetForegroundWindow(prevHwnd);
            }
            await Task.Delay(50);
            if (GetForegroundWindow() != prevHwnd) return false;
        }

        await Task.Delay(40);
        return InputSender.SendCtrlV();
    }
}
