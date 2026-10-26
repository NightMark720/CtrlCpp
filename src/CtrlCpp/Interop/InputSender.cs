using System.Runtime.InteropServices;
using CtrlCpp.Interop;

namespace CtrlCpp.Services;

/// <summary>用 SendInput 以扫描码方式模拟 Ctrl+V 粘贴。</summary>
internal static class InputSender
{
    public static bool SendCtrlV()
    {
        var down = new INPUT[2];
        down[0].type = NativeConstants.INPUT_KEYBOARD;
        down[0].ki.wScan = NativeConstants.SCAN_LCTRL;
        down[0].ki.dwFlags = NativeConstants.KEYEVENTF_SCANCODE;
        down[1].type = NativeConstants.INPUT_KEYBOARD;
        down[1].ki.wScan = NativeConstants.SCAN_V;
        down[1].ki.dwFlags = NativeConstants.KEYEVENTF_SCANCODE;

        var up = new INPUT[2];
        up[0].type = NativeConstants.INPUT_KEYBOARD;
        up[0].ki.wScan = NativeConstants.SCAN_V;
        up[0].ki.dwFlags = NativeConstants.KEYEVENTF_SCANCODE | NativeConstants.KEYEVENTF_KEYUP;
        up[1].type = NativeConstants.INPUT_KEYBOARD;
        up[1].ki.wScan = NativeConstants.SCAN_LCTRL;
        up[1].ki.dwFlags = NativeConstants.KEYEVENTF_SCANCODE | NativeConstants.KEYEVENTF_KEYUP;

        var r1 = NativeMethods.SendInput(2, down, Marshal.SizeOf<INPUT>());
        Thread.Sleep(20);
        var r2 = NativeMethods.SendInput(2, up, Marshal.SizeOf<INPUT>());
        return r1 == 2 && r2 == 2;
    }
}
