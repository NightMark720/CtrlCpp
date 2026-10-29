using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CtrlCpp.Interop;
using CtrlCpp.Services;
using CtrlCpp.ViewModels;
using static CtrlCpp.Interop.NativeConstants;
using static CtrlCpp.Interop.NativeMethods;

namespace CtrlCpp.Views;

public partial class HistoryPopupWindow : Window
{
    public PopupController Controller { get; }
    private readonly HistoryViewModel _vm = new();
    private readonly DispatcherTimer _timeTimer;
    private bool _wheelAnimating;
    private ScrollViewer? _listScrollViewer;
    private double _wheelFrom;
    private double _wheelTarget;
    private DateTime _wheelStartedAt;
    private bool _needPosition;

    public HistoryPopupWindow(PopupController controller)
    {
        Controller = controller;
        InitializeComponent();
        List.ItemsSource = _vm.Items;
        DataContext = _vm;
        SourceInitialized += (_, _) =>
            ThemeService.ApplyRoundedCorners(new WindowInteropHelper(this).Handle);
        // SizeToContent 的窗口在内容测量后才得到真实高度，因此挂 SizeChanged 定位
        SizeChanged += (_, _) =>
        {
            if (!_needPosition) return;
            _needPosition = false;
            PositionNearCursor();
        };
        Deactivated += (_, _) =>
        {
            if (!Controller.SuppressClose) Hide();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        _timeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timeTimer.Tick += (_, _) => _vm.RefreshTimes();
        List.PreviewMouseWheel += OnListPreviewMouseWheel;
        List.PreviewMouseLeftButtonDown += (_, _) => StopWheelAnimation();
        AppServices.HistoryChanged += OnHistoryChanged;
        Closed += (_, _) =>
        {
            AppServices.HistoryChanged -= OnHistoryChanged;
            _timeTimer.Stop();
            StopWheelAnimation();
        };
    }

    /// <summary>窗口显示并完成尺寸测量后定位；拖动后高度不变再次打开也能正确定位。</summary>
    public void PositionAfterShow()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_needPosition || !IsVisible) return;
            _needPosition = false;
            PositionNearCursor();
        }, DispatcherPriority.Render);
    }

    private void OnHistoryChanged() =>
        Dispatcher.BeginInvoke(() =>
        {
            StopWheelAnimation();
            _vm.Reload();
            UpdateEmptyState();
        });

    /// <summary>每次显示前重置状态（PopupController.Show 调用）。</summary>
    public void PrepareShow()
    {
        _needPosition = true;
        StopWheelAnimation();
        SearchBox.Text = "";
        SearchPlaceholder.Visibility = Visibility.Visible;
        ClearBtn.Visibility = Visibility.Collapsed;
        _vm.ApplyFilter("");
        _vm.Reload();
        UpdateEmptyState();
        if (_vm.Items.Count > 0)
        {
            List.SelectedIndex = 0;
            List.ScrollIntoView(List.SelectedItem);
        }
        else List.SelectedIndex = -1;
        Dispatcher.BeginInvoke(() =>
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }, DispatcherPriority.Input);
        _timeTimer.Start();
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = _vm.ViewCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _vm.EmptyText;
        StatusText.Text = _vm.StatusText;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        StopWheelAnimation();
        var empty = string.IsNullOrEmpty(SearchBox.Text);
        SearchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearBtn.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _vm.ApplyFilter(SearchBox.Text);
        UpdateEmptyState();
        if (List.SelectedIndex < 0 && _vm.Items.Count > 0) List.SelectedIndex = 0;
    }

    private void OnClearButtonMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnClearSearch(object sender, MouseButtonEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            StopWheelAnimation();
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Controller.Hide();
                break;
            case Key.Delete:
                if (!(Keyboard.FocusedElement is TextBox))
                {
                    e.Handled = true;
                    DeleteSelected();
                }
                break;
            case Key.Enter:
                e.Handled = true;
                PasteSelected();
                break;
            case Key.Down:
                e.Handled = true;
                MoveSelection(1);
                break;
            case Key.Up:
                e.Handled = true;
                MoveSelection(-1);
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        var idx = List.SelectedIndex + delta;
        if (idx < 0 || idx >= _vm.Items.Count) return;
        List.SelectedIndex = idx;
        List.ScrollIntoView(List.SelectedItem);
    }

    private void OnListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scroller = GetListScrollViewer();
        if (scroller == null || scroller.ScrollableHeight <= 0) return;
        e.Handled = true;

        if (!_wheelAnimating) _wheelTarget = scroller.VerticalOffset;
        // 与 WPF 原生 ScrollViewer 保持一致：e.Delta 为负时内容向下滚（偏移增加）。
        // Windows 已在驱动层按系统"反转滚动方向"设置翻转滚轮消息，这里直接沿用即可，
        // 额外再翻转会造成双重反转。
        _wheelTarget = Math.Clamp(_wheelTarget - e.Delta * 0.4, 0, scroller.ScrollableHeight);
        _wheelFrom = scroller.VerticalOffset;
        _wheelStartedAt = DateTime.UtcNow;

        if (Math.Abs(_wheelTarget - _wheelFrom) < 0.5)
        {
            StopWheelAnimation();
            scroller.ScrollToVerticalOffset(_wheelTarget);
            return;
        }
        StartWheelAnimation();
    }

    private void StartWheelAnimation()
    {
        if (_wheelAnimating) return;
        _wheelAnimating = true;
        System.Windows.Media.CompositionTarget.Rendering += OnWheelRendering;
    }

    private void StopWheelAnimation()
    {
        if (!_wheelAnimating) return;
        _wheelAnimating = false;
        System.Windows.Media.CompositionTarget.Rendering -= OnWheelRendering;
    }

    private void OnWheelRendering(object? sender, EventArgs e)
    {
        var scroller = GetListScrollViewer();
        if (scroller == null)
        {
            StopWheelAnimation();
            return;
        }
        // 跟随渲染帧更新并缩短缓动，避免定时器与屏幕刷新错位产生拖影
        var progress = Math.Min(1, (DateTime.UtcNow - _wheelStartedAt).TotalMilliseconds / 130);
        var eased = 1 - Math.Pow(1 - progress, 2);
        scroller.ScrollToVerticalOffset(_wheelFrom + (_wheelTarget - _wheelFrom) * eased);
        if (progress >= 1) StopWheelAnimation();
    }

    private ScrollViewer? GetListScrollViewer()
    {
        if (_listScrollViewer != null) return _listScrollViewer;
        _listScrollViewer = FindDescendant<ScrollViewer>(List);
        return _listScrollViewer;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindDescendant<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private void OnListClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d &&
            ItemsControl.ContainerFromElement(List, d) is ListBoxItem { Content: ClipboardItemViewModel vm })
            PasteSelected(vm);
    }

    private void PasteSelected()
    {
        if (List.SelectedItem is ClipboardItemViewModel vm) PasteSelected(vm);
    }

    private void PasteSelected(ClipboardItemViewModel vm) => Controller.RequestPaste(vm);

    private void DeleteSelected()
    {
        if (List.SelectedItem is ClipboardItemViewModel vm) DeleteItem(vm);
    }

    private void DeleteItem(ClipboardItemViewModel vm)
    {
        try
        {
            var media = AppServices.Store.DeleteIds(new[] { vm.Id });
            foreach (var m in media) AppServices.Media.Delete(m);
            _vm.Remove(vm);
            UpdateEmptyState();
            LogService.Log($"已删除记录 #{vm.Id}");
        }
        catch (Exception ex) { LogService.LogEx("删除记录失败", ex); }
    }

    private void OnMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d &&
            ItemsControl.ContainerFromElement(List, d) is ListBoxItem { Content: ClipboardItemViewModel vm })
        {
            List.SelectedItem = vm;
            Controller.SuppressClose = true;
            var menu = new ContextMenu();
            var miPaste = new MenuItem { Header = "粘贴" };
            miPaste.Click += (_, _) => PasteSelected(vm);
            var miCopy = new MenuItem { Header = "复制内容" };
            miCopy.Click += (_, _) => Controller.CopyWithoutClosing(vm);
            var miDel = new MenuItem { Header = "删除" };
            miDel.Click += (_, _) => DeleteItem(vm);
            _ = menu.Items.Add(miPaste);
            _ = menu.Items.Add(miCopy);
            _ = menu.Items.Add(new Separator());
            _ = menu.Items.Add(miDel);
            menu.Closed += OnMenuClosed;
            List.ContextMenu = menu;
        }
        else e.Handled = true;
    }

    private void OnMenuClosed(object sender, RoutedEventArgs e) => Controller.SuppressClose = false;

    private void OnDragAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || e.OriginalSource is not DependencyObject source) return;
        // 保留列表点击、文本编辑和滚动条的原生行为；其余空白区域均可拖动窗口
        if (source == ClearBtn ||
            ItemsControl.ContainerFromElement(List, source) != null ||
            FindAncestor<TextBoxBase>(source) != null ||
            FindAncestor<ScrollBar>(source) != null) return;

        _needPosition = false;
        try { DragMove(); }
        catch (InvalidOperationException) { /* 鼠标已松开时无需拖动 */ }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T match) return match;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    /// <summary>定位到鼠标附近（物理像素，限制在显示器工作区内）。</summary>
    private void PositionNearCursor()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            _ = GetCursorPos(out var pt);
            var mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return;
            if (!GetWindowRect(hwnd, out var wr)) return;
            var w = wr.Right - wr.Left;
            var h = wr.Bottom - wr.Top;
            var x = Math.Min(Math.Max(mi.rcWork.Left, pt.X + 12), mi.rcWork.Right - w);
            var y = Math.Min(Math.Max(mi.rcWork.Top, pt.Y + 12), mi.rcWork.Bottom - h);
            _ = SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch (Exception ex) { LogService.LogEx("定位历史窗口失败", ex); }
    }
}
