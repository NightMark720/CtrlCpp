using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using CtrlCpp.Interop;
using CtrlCpp.Models;
using CtrlCpp.ViewModels;
using CtrlCpp.Services;
using CtrlCpp.Views;
using H.NotifyIcon;

namespace CtrlCpp;

public partial class App : Application
{
    private SingleInstanceService? _single;
    private KeyboardHookService? _hook;
    private ClipboardMonitorService? _monitor;
    private TaskbarIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private bool _exiting;

    private static readonly JsonSerializerOptions DumpJsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args ?? Array.Empty<string>();

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CtrlCpp");
        Directory.CreateDirectory(dataDir);
        LogService.Init(Path.Combine(dataDir, "logs"));

        // 无界面调试命令（自动化测试钩子）
        if (args.Contains("--dump")) { RunDump(args, dataDir); Shutdown(); return; }
        if (args.Contains("--add-dummy")) { RunAddDummy(args, dataDir); Shutdown(); return; }
        if (args.Contains("--clear")) { RunClear(dataDir); Shutdown(); return; }

        if (args.Contains("--exit"))
        {
            if (!SingleInstanceService.TrySendCommand("exit"))
                LogService.Log("--exit：未发现运行中的实例");
            Shutdown();
            return;
        }

        _single = new SingleInstanceService();
        if (!_single.TryAcquirePrimary())
        {
            LogService.Log("已有实例在运行，转发命令后退出");
            SingleInstanceService.TrySendCommand(args.Contains("--settings") ? "settings" : "show");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            LogService.LogEx("UI 线程未处理异常", ex.Exception);
            try
            {
                MessageBox.Show("CtrlC++ 遇到错误：" + ex.Exception.Message, "CtrlC++",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* 忽略 */ }
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            LogService.LogEx("未处理异常", ex.ExceptionObject as Exception ?? new Exception("未知异常"));

        // 初始化：任何一步失败都必须退出进程，否则会成为既无窗口也无法 --exit 的僵尸实例
        try
        {
        // 设置
        var settingsSvc = new SettingsService(Path.Combine(dataDir, "settings.json"));
        settingsSvc.Load();
        AppServices.Settings = settingsSvc;

        ThemeService.ApplyDark();

        // 存储
        var store = new HistoryStore();
        store.Open(Path.Combine(dataDir, "history.db"));
        AppServices.Store = store;
        var media = new MediaStorage();
        media.Init(Path.Combine(dataDir, "media"));
        AppServices.Media = media;
        try { media.SweepOrphans(store.GetAllMediaPaths()); }
        catch (Exception ex) { LogService.LogEx("孤儿媒体清扫失败", ex); }

        // 清理服务
        var cleanup = new CleanupService(Dispatcher, store, media, settingsSvc);
        AppServices.Cleanup = cleanup;

        // 历史窗口控制器
        var popup = new PopupController();
        AppServices.Popup = popup;

        // 剪贴板监听
        var reader = new ClipboardReader();
        AppServices.Reader = reader;
        var monitor = new ClipboardMonitorService();
        _monitor = monitor;
        AppServices.Monitor = monitor;
        reader.ItemRead += payload => OnClipboardRead(payload, cleanup);
        reader.Start();
        monitor.Start(reader);

        // 键盘钩子
        _hook = new KeyboardHookService(Dispatcher);
        AppServices.Hook = _hook;
        _hook.SetHotkey((HotkeyMods)settingsSvc.Settings.HotkeyMods, settingsSvc.Settings.HotkeyVk);
        _hook.HotkeyPressed += () => popup.Toggle();
        _hook.Install();

        // 托盘
        CreateTrayIcon(settingsSvc.IsFirstRun);

        // JumpList
        CreateJumpList();

        // 单实例 IPC
        _single.CommandReceived += cmd => Dispatcher.BeginInvoke(() => HandleIpcCommand(cmd));

        settingsSvc.Changed += () =>
        {
            _hook.SetHotkey((HotkeyMods)settingsSvc.Settings.HotkeyMods, settingsSvc.Settings.HotkeyVk);
            cleanup.RunAll();
        };

        cleanup.Start();

        // 开机自启路径自愈
        try
        {
            if (settingsSvc.Settings.StartWithWindows) AutostartService.Refresh();
        }
        catch (Exception ex) { LogService.LogEx("自启刷新失败", ex); }

        if (args.Contains("--settings")) ShowSettings();
        else if (args.Contains("--history"))
            Dispatcher.BeginInvoke(() => popup.Show(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        LogService.Log("启动完成");
        }
        catch (Exception ex)
        {
            LogService.LogEx("初始化失败，程序退出", ex);
            try { _hook?.Dispose(); _monitor?.Dispose(); _tray?.Dispose(); AppServices.Store?.Close(); } catch { /* 忽略 */ }
            try
            {
                MessageBox.Show("CtrlC++ 启动失败：" + ex.Message, "CtrlC++",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* 忽略 */ }
            Environment.Exit(1);
        }
    }

    private void OnClipboardRead(ClipboardPayload? payload, CleanupService cleanup)
    {
        try
        {
            if (payload == null) return;
            if (AppServices.Settings.IsExcluded(payload.SourceApp))
            {
                LogService.Log($"来源 {payload.SourceApp} 在排除列表，跳过");
                return;
            }
            string? mediaRel = null;
            if (payload.Kind == ItemKind.Image && payload.PngBytes is { Length: > 0 })
                mediaRel = AppServices.Media.SavePng(payload.PngBytes, payload.ContentHash[..16].ToLowerInvariant());
            var id = AppServices.Store.Upsert(payload, mediaRel);
            LogService.Log($"已记录 #{id} kind={payload.Kind} 来源={payload.SourceApp ?? "?"} 大小={payload.ByteSize}");
            cleanup.RunSizeLimit();
            AppServices.NotifyHistoryChanged();
        }
        catch (Exception ex) { LogService.LogEx("记录剪贴板失败", ex); }
    }

    private void CreateTrayIcon(bool firstRun)
    {
        _tray = new TaskbarIcon { ToolTipText = "CtrlC++ — 剪贴板历史" };
        try
        {
            var sri = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            _tray.Icon = new System.Drawing.Icon(sri.Stream);
        }
        catch (Exception ex)
        {
            LogService.LogEx("加载托盘图标失败，回退到 exe 图标", ex);
            try
            {
                if (Environment.ProcessPath is { } p)
                    _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(p);
            }
            catch { /* 忽略 */ }
        }

        var menu = new ContextMenu();
        var miSettings = new MenuItem { Header = "设置" };
        miSettings.Click += (_, _) => ShowSettings();
        var miHistory = new MenuItem { Header = "历史记录" };
        miHistory.Click += (_, _) => AppServices.Popup.Show();
        var miExit = new MenuItem { Header = "退出" };
        miExit.Click += (_, _) => ExitApp();
        _ = menu.Items.Add(miSettings);
        _ = menu.Items.Add(miHistory);
        _ = menu.Items.Add(new Separator());
        _ = menu.Items.Add(miExit);
        _tray.ContextMenu = menu;
        _tray.TrayLeftMouseUp += (_, _) => AppServices.Popup.Show();

        // 纯代码创建的 TaskbarIcon 不在可视化树中，必须强制创建托盘图标
        _tray.ForceCreate(false);
        LogService.Log("托盘图标已创建");

        if (firstRun)
            ShowFirstRunBalloon();
    }

    /// <summary>首启气泡（H.NotifyIcon 2.x 无系统气泡 API，用自定义气球弹窗）。</summary>
    private void ShowFirstRunBalloon()
    {
        try
        {
            var stack = new StackPanel();
            _ = stack.Children.Add(new TextBlock
            {
                Text = "CtrlC++ 已在后台运行",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF2, 0xF2, 0xF2)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
            });
            _ = stack.Children.Add(new TextBlock
            {
                Text = $"按 {HotkeyFormat.ToString((HotkeyMods)AppServices.Settings.Settings.HotkeyMods, AppServices.Settings.Settings.HotkeyVk)} 打开剪贴板历史",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x9B, 0x9B, 0x9B)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1C)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 12),
                Width = 330,
                Child = stack,
            };
            _tray!.ShowCustomBalloon(border, System.Windows.Controls.Primitives.PopupAnimation.Fade, 6000);
        }
        catch (Exception ex) { LogService.LogEx("气泡提示失败", ex); }
    }

    private void CreateJumpList()
    {
        try
        {
            var exe = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exe)) return;
            var jl = new JumpList();
            jl.JumpItems.Add(new JumpTask
            {
                Title = "设置",
                Description = "打开 CtrlC++ 设置",
                ApplicationPath = exe,
                Arguments = "--settings",
            });
            jl.JumpItems.Add(new JumpTask
            {
                Title = "历史记录",
                Description = "打开剪贴板历史",
                ApplicationPath = exe,
                Arguments = "--history",
            });
            JumpList.SetJumpList(this, jl);
            jl.Apply();
        }
        catch (Exception ex) { LogService.LogEx("JumpList 创建失败", ex); }
    }

    private void HandleIpcCommand(string cmd)
    {
        switch (cmd)
        {
            case "show":
            case "history":
                AppServices.Popup.Show();
                break;
            case "settings":
                ShowSettings();
                break;
            case "exit":
                ExitApp();
                break;
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    internal void ExitForUpdate() => ExitApp();

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        LogService.Log("退出中…");
        try { _hook?.Dispose(); } catch { /* 忽略 */ }
        try { _monitor?.Dispose(); } catch { /* 忽略 */ }
        try { _tray?.Dispose(); } catch { /* 忽略 */ }
        try { AppServices.Store?.Close(); } catch { /* 忽略 */ }
        // 设置是写穿保存的，退出时不再 Save（避免触发 Changed → 清理服务访问已关闭的 DB）
        LogService.Log("已退出");
        Environment.Exit(0);
    }

    // ---------- 无界面调试命令 ----------

    private void RunDump(string[] args, string dataDir)
    {
        string? outPath = null;
        var n = 5;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--dump" && i + 1 < args.Length && int.TryParse(args[i + 1], out var nn)
                && !args[i + 1].StartsWith("--"))
                n = nn;
            if (args[i] == "--out" && i + 1 < args.Length) outPath = args[i + 1];
        }
        try
        {
            var store = new HistoryStore();
            store.Open(Path.Combine(dataDir, "history.db"));
            var items = store.ListRecent(n);
            var obj = new Dictionary<string, object?>
            {
                ["totalSize"] = store.SumSize(),
                ["count"] = items.Count,
                ["items"] = items.Select(ItemDump.From).ToList(),
            };
            var json = JsonSerializer.Serialize(obj, DumpJsonOpts);
            store.Close();
            if (outPath != null) File.WriteAllText(outPath, json);
            else TryConsoleOut(json);
        }
        catch (Exception ex)
        {
            LogService.LogEx("--dump 失败", ex);
            var msg = "ERROR " + ex.Message;
            if (outPath != null) File.WriteAllText(outPath, msg);
            else TryConsoleOut(msg);
        }
    }

    private static void TryConsoleOut(string text)
    {
        try
        {
            NativeMethods.AttachConsole(0xFFFFFFFF);
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine(text);
            NativeMethods.FreeConsole();
        }
        catch { /* 无控制台时忽略 */ }
    }

    private void RunAddDummy(string[] args, string dataDir)
    {
        int count = 0, maxKb = 0, ageHours = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != "--add-dummy") continue;
            if (i + 1 < args.Length) _ = int.TryParse(args[i + 1], out count);
            if (i + 2 < args.Length) _ = int.TryParse(args[i + 2], out maxKb);
            for (var j = i + 3; j < args.Length - 1; j++)
                if (args[j] == "--ageHours" && int.TryParse(args[j + 1], out var h))
                    ageHours = h;
            break;
        }
        if (count <= 0 || maxKb <= 0)
        {
            LogService.Log("--add-dummy 参数无效");
            return;
        }
        try
        {
            var store = new HistoryStore();
            store.Open(Path.Combine(dataDir, "history.db"));
            var media = new MediaStorage();
            media.Init(Path.Combine(dataDir, "media"));
            var rnd = new Random(12345);
            var nowTicks = DateTime.UtcNow.Ticks;
            for (var i = 0; i < count; i++)
            {
                var kb = Math.Max(1, rnd.Next(1, Math.Max(2, maxKb)));
                var bytes = new byte[kb * 1024];
                rnd.NextBytes(bytes);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                var rel = media.SavePng(bytes, hash[..16].ToLowerInvariant());
                var payload = new ClipboardPayload
                {
                    Kind = ItemKind.Image,
                    ContentHash = hash,
                    Preview = "dummy",
                    ByteSize = bytes.Length + 128,
                    SourceApp = "dummy.exe",
                    LastSeenAtOverride = nowTicks - (long)ageHours * TimeSpan.TicksPerHour - i,
                };
                _ = store.Upsert(payload, rel);
            }
            store.Close();
            TryConsoleOut($"OK added {count}");
        }
        catch (Exception ex)
        {
            LogService.LogEx("--add-dummy 失败", ex);
            TryConsoleOut("ERROR " + ex.Message);
        }
    }

    private void RunClear(string dataDir)
    {
        try
        {
            var store = new HistoryStore();
            store.Open(Path.Combine(dataDir, "history.db"));
            var media = new MediaStorage();
            media.Init(Path.Combine(dataDir, "media"));
            var mediaPaths = store.ClearAll();
            foreach (var m in mediaPaths) media.Delete(m);
            media.ClearAll();
            store.Close();
            TryConsoleOut("OK cleared");
        }
        catch (Exception ex)
        {
            LogService.LogEx("--clear 失败", ex);
            TryConsoleOut("ERROR " + ex.Message);
        }
    }
}
