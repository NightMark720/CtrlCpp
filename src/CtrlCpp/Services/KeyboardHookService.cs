using CtrlCpp.Interop;
using CtrlCpp.Models;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using static CtrlCpp.Interop.NativeConstants;
using static CtrlCpp.Interop.NativeMethods;

namespace CtrlCpp.Services;

/// <summary>
/// 低级键盘钩子识别可配置的全局快捷键（默认 Ctrl+空格）。回调保持 O(1)（无 I/O），
/// 触发通过 Dispatcher.BeginInvoke 转回 UI 线程。构成快捷键的按键被吞掉（不输入、
/// 不触发输入法切换），其余按键一律放行。
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private IntPtr _hook = IntPtr.Zero;
    private readonly LowLevelHookProc _proc;
    private readonly Dispatcher _dispatcher;

    private int _mods = HotkeyFormat.DefaultMods;
    private int _vk = HotkeyFormat.DefaultVk;

    // 修饰键物理状态（按下为 true）
    private bool _ctrlDown;
    private bool _shiftDown;
    private bool _altDown;
    private bool _winDown;

    private bool _keyPhysicallyDown;
    private bool _swallow; // 已吞掉按下，配对的抬起也要吞

    private static volatile bool _suspended;

    /// <summary>录制新快捷键期间挂起触发（按键照常透传给录制框，不吞不打扰）。</summary>
    public static bool Suspended { get => _suspended; set => _suspended = value; }

    /// <summary>已在 UI 线程上触发（BeginInvoke 转发）。</summary>
    public event Action? HotkeyPressed;

    public KeyboardHookService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _proc = HookProc;
    }

    /// <summary>运行中更新快捷键（设置保存后立即生效）。</summary>
    public void SetHotkey(HotkeyMods mods, int vk)
    {
        Volatile.Write(ref _mods, (int)mods);
        Volatile.Write(ref _vk, vk);
        LogService.Log($"快捷键已设置为 {HotkeyFormat.ToString(mods, vk)}");
    }

    public void Install()
    {
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        LogService.Log(_hook != IntPtr.Zero
            ? "键盘钩子安装成功"
            : $"键盘钩子安装失败 GetLastError={Marshal.GetLastWin32Error()}");
    }

    /// <summary>修饰键需精确匹配：组合里的必须按下，组合外的必须抬起。</summary>
    private bool ModifierMatches(HotkeyMods mods) =>
        ((mods & HotkeyMods.Ctrl) != 0) == _ctrlDown &&
        ((mods & HotkeyMods.Shift) != 0) == _shiftDown &&
        ((mods & HotkeyMods.Alt) != 0) == _altDown &&
        ((mods & HotkeyMods.Win) != 0) == _winDown;

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var s = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var msg = wParam.ToInt32();
                var down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                var up = msg is WM_KEYUP or WM_SYSKEYUP;

                // 跟踪修饰键状态（这些键永远放行）
                if (s.vkCode == VK_CONTROL || s.vkCode == VK_LCONTROL || s.vkCode == VK_RCONTROL)
                    _ctrlDown = down;
                else if (s.vkCode == VK_SHIFT || s.vkCode == VK_LSHIFT || s.vkCode == VK_RSHIFT)
                    _shiftDown = down;
                else if (s.vkCode == VK_MENU || s.vkCode == VK_LMENU || s.vkCode == VK_RMENU)
                    _altDown = down;
                else if (s.vkCode == VK_LWIN || s.vkCode == VK_RWIN)
                    _winDown = down;

                var vk = Volatile.Read(ref _vk);
                if (vk != 0 && s.vkCode == (uint)vk)
                {
                    if (down)
                    {
                        if (!_keyPhysicallyDown)
                        {
                            _keyPhysicallyDown = true;
                            if (!_suspended && ModifierMatches((HotkeyMods)Volatile.Read(ref _mods)))
                            {
                                // 快捷键命中：吞掉并触发弹窗开关
                                _swallow = true;
                                _dispatcher.BeginInvoke(() => HotkeyPressed?.Invoke());
                                return new IntPtr(1);
                            }
                            // 未命中（录制中/修饰键不符）：放行，并清除陈旧的吞键标记
                            _swallow = false;
                        }
                        else if (_swallow)
                        {
                            // 按住不放产生的自动重复：一并吞掉，不重复触发
                            return new IntPtr(1);
                        }
                    }
                    else if (up)
                    {
                        _keyPhysicallyDown = false;
                        if (_swallow)
                        {
                            _swallow = false;
                            return new IntPtr(1); // 吞掉配对的抬起
                        }
                    }
                }
            }
            catch { /* 钩子回调内绝不让异常逃逸 */ }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            try { _ = UnhookWindowsHookEx(_hook); } catch { /* 忽略 */ }
            _hook = IntPtr.Zero;
        }
    }
}
