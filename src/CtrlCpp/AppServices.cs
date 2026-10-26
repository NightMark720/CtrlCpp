namespace CtrlCpp.Services;

/// <summary>简单的服务定位器（单实例小应用，不引入 DI 框架）。</summary>
public static class AppServices
{
    public static SettingsService Settings { get; set; } = null!;
    public static HistoryStore Store { get; set; } = null!;
    public static MediaStorage Media { get; set; } = null!;
    public static ClipboardMonitorService Monitor { get; set; } = null!;
    public static ClipboardReader Reader { get; set; } = null!;
    public static PopupController Popup { get; set; } = null!;
    public static CleanupService Cleanup { get; set; } = null!;
    public static KeyboardHookService Hook { get; set; } = null!;
    public static FileIconCache FileIcons { get; } = new();

    /// <summary>程序正在退出（TaskbarLauncherWindow 用来允许真正关闭）。</summary>
    public static bool IsExiting { get; set; }

    /// <summary>历史数据变化（新记录/删除/清理）。订阅方需自行调度到 UI 线程。</summary>
    public static event Action? HistoryChanged;

    public static void NotifyHistoryChanged() => HistoryChanged?.Invoke();
}
