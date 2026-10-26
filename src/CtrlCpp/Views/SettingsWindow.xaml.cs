using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CtrlCpp.Models;
using CtrlCpp.Services;
using CtrlCpp.ViewModels;

namespace CtrlCpp.Views;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    public sealed record ExcludedRow(string ExeName, string Display);

    private readonly ObservableCollection<ExcludedRow> _excluded = new();
    private bool _loaded;
    private bool _hotkeyRecording;

    public SettingsWindow()
    {
        InitializeComponent();
        ExcludedList.ItemsSource = _excluded;
        Loaded += OnLoaded;
        AppServices.HistoryChanged += OnHistoryChanged;
        Closed += (_, _) =>
        {
            AppServices.HistoryChanged -= OnHistoryChanged;
            KeyboardHookService.Suspended = false;
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        var s = AppServices.Settings.Settings;
        AutoStartToggle.IsChecked = s.StartWithWindows;
        RetentionBox.Text = s.RetentionDays.ToString();
        CacheLimitBox.Text = s.CacheLimitMB.ToString();
        ShowHotkey();
        foreach (var exe in s.ExcludedApps) _excluded.Add(new ExcludedRow(exe, exe));
        VersionRun.Text = $"CtrlC++ v{typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";
        RefreshUsage();
    }

    private void ShowHotkey()
    {
        var s = AppServices.Settings.Settings;
        HotkeyBox.Text = HotkeyFormat.ToString((HotkeyMods)s.HotkeyMods, s.HotkeyVk);
    }

    private void OnHistoryChanged() =>
        Dispatcher.BeginInvoke(RefreshUsage);

    private void RefreshUsage()
    {
        try
        {
            var sum = AppServices.Store.SumSize();
            var limitBytes = (long)AppServices.Settings.Settings.CacheLimitMB * 1024 * 1024;
            UsageText.Text = $"当前缓存：{FormatUtil.FormatSize(sum)} / {AppServices.Settings.Settings.CacheLimitMB} MB";
            UsageBar.Value = limitBytes > 0 ? Math.Min(1.0, (double)sum / limitBytes) : 0;
        }
        catch (Exception ex) { LogService.LogEx("刷新缓存用量失败", ex); }
    }

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var enabled = AutoStartToggle.IsChecked == true;
        AppServices.Settings.Settings.StartWithWindows = enabled;
        try { AutostartService.SetEnabled(enabled); }
        catch (Exception ex) { LogService.LogEx("设置开机自启失败", ex); }
        AppServices.Settings.Save();
    }

    private void OnNumberCommitted(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var s = AppServices.Settings.Settings;
        if (sender == RetentionBox && int.TryParse(RetentionBox.Text, out var days))
        {
            days = Math.Clamp(days, 1, 30);
            if (days != s.RetentionDays)
            {
                s.RetentionDays = days;
                RetentionBox.Text = days.ToString();
                AppServices.Settings.Save();
                AppServices.Cleanup.RunAll();
            }
        }
        else if (sender == CacheLimitBox && int.TryParse(CacheLimitBox.Text, out var mb))
        {
            mb = Math.Clamp(mb, 256, 20480);
            if (mb != s.CacheLimitMB)
            {
                s.CacheLimitMB = mb;
                CacheLimitBox.Text = mb.ToString();
                AppServices.Settings.Save();
                AppServices.Cleanup.RunAll();
                RefreshUsage();
            }
        }
    }

    private async void OnAddExcluded(object sender, RoutedEventArgs e)
    {
        var dlg = new ExcludedAppPickerDialog { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedExe))
        {
            var exe = dlg.SelectedExe.ToLowerInvariant();
            var s = AppServices.Settings.Settings;
            if (s.ExcludedApps.Contains(exe)) return;
            // 整表替换（读取线程会并发遍历列表，不能原地 Add）
            s.ExcludedApps = s.ExcludedApps.Append(exe).ToList();
            _excluded.Add(new ExcludedRow(exe, exe));
            AppServices.Settings.Save();
            LogService.Log($"已排除应用：{exe}");
        }
        await Task.CompletedTask;
    }

    private void OnRemoveExcluded(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ExcludedRow row) return;
        var s = AppServices.Settings.Settings;
        if (!s.ExcludedApps.Contains(row.ExeName)) return;
        // 整表替换（读取线程会并发遍历列表，不能原地 Remove）
        s.ExcludedApps = s.ExcludedApps.Where(x => x != row.ExeName).ToList();
        var listRow = _excluded.FirstOrDefault(r => r.ExeName == row.ExeName);
        if (listRow != null) _excluded.Remove(listRow);
        AppServices.Settings.Save();
        LogService.Log($"已取消排除：{row.ExeName}");
    }

    private void OnClearAll(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this,
            "确定要清空全部历史记录吗？此操作不可恢复。",
            "确认清空", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            var media = AppServices.Store.ClearAll();
            foreach (var m in media) AppServices.Media.Delete(m);
            AppServices.NotifyHistoryChanged();
            RefreshUsage();
            LogService.Log("已手动清空全部历史");
        }
        catch (Exception ex) { LogService.LogEx("清空历史失败", ex); }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ---------- 快捷键录制 ----------

    private void OnHotkeyGotFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _hotkeyRecording = true;
        KeyboardHookService.Suspended = true;
        HotkeyHint.Visibility = Visibility.Collapsed;
        HotkeyBox.Text = "请按下新的组合键…";
    }

    private void OnHotkeyLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_hotkeyRecording) return;
        EndRecording();
    }

    private void EndRecording()
    {
        _hotkeyRecording = false;
        KeyboardHookService.Suspended = false;
        HotkeyHint.Visibility = Visibility.Collapsed;
        ShowHotkey();
    }

    private static HotkeyMods ToMods(ModifierKeys m)
    {
        var r = HotkeyMods.None;
        if (m.HasFlag(ModifierKeys.Control)) r |= HotkeyMods.Ctrl;
        if (m.HasFlag(ModifierKeys.Shift)) r |= HotkeyMods.Shift;
        if (m.HasFlag(ModifierKeys.Alt)) r |= HotkeyMods.Alt;
        if (m.HasFlag(ModifierKeys.Windows)) r |= HotkeyMods.Win;
        return r;
    }

    private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_hotkeyRecording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            HotkeyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }

        // 只按了修饰键：仅更新显示，等待主键
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            HotkeyBox.Text = HotkeyFormat.ToString(ToMods(Keyboard.Modifiers), 0);
            HotkeyHint.Visibility = Visibility.Collapsed;
            return;
        }

        var mods = ToMods(Keyboard.Modifiers);
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (!HotkeyFormat.IsValid(mods, vk))
        {
            HotkeyHint.Text = key is >= Key.F1 and <= Key.F24
                ? "该键不可用，请换一个"
                : "普通按键需要搭配 Ctrl 或 Alt 使用（例如 Ctrl+B）";
            HotkeyHint.Visibility = Visibility.Visible;
            return;
        }

        var s = AppServices.Settings.Settings;
        s.HotkeyMods = (int)mods;
        s.HotkeyVk = vk;
        AppServices.Settings.Save();
        LogService.Log($"用户设置快捷键：{HotkeyFormat.ToString(mods, vk)}");
        HotkeyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private void OnHotkeyReset(object sender, RoutedEventArgs e)
    {
        var s = AppServices.Settings.Settings;
        s.HotkeyMods = HotkeyFormat.DefaultMods;
        s.HotkeyVk = HotkeyFormat.DefaultVk;
        AppServices.Settings.Save();
        LogService.Log("快捷键已恢复默认 Ctrl + 空格");
        EndRecording();
    }
}
