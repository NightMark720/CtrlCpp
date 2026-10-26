using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CtrlCpp.Services;

namespace CtrlCpp.Views;

/// <summary>从正在运行的程序中选择要排除的应用。</summary>
public partial class ExcludedAppPickerDialog : Window
{
    private readonly ObservableCollection<Row> _rows = new();
    private readonly List<Row> _all = new();

    public string? SelectedExe { get; private set; }

    public ExcludedAppPickerDialog()
    {
        InitializeComponent();
        ProcList.ItemsSource = _rows;
        SourceInitialized += (_, _) => ThemeService.ApplyDarkTitleBar(
            new System.Windows.Interop.WindowInteropHelper(this).Handle, true);
        foreach (var p in ProcessPickerService.GetVisibleWindowProcesses())
            _all.Add(new Row(p.ExeName, p.Display));
        Refill("");
    }

    private void Refill(string filter)
    {
        _rows.Clear();
        foreach (var r in _all)
            if (filter.Length == 0 ||
                r.Exe.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                r.Display.Contains(filter, StringComparison.OrdinalIgnoreCase))
                _rows.Add(r);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility =
            string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        Refill(SearchBox.Text.Trim());
    }

    private void OnProcDoubleClick(object sender, MouseButtonEventArgs e) => ConfirmSelected();

    private void OnAddSelected(object sender, RoutedEventArgs e) => ConfirmSelected();

    private void ConfirmSelected()
    {
        if (ProcList.SelectedItem is Row row)
        {
            SelectedExe = row.Exe;
            DialogResult = true;
        }
    }

    private void OnAddManual(object sender, RoutedEventArgs e)
    {
        var name = ManualBox.Text.Trim().ToLowerInvariant();
        if (name.Length == 0) return;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        SelectedExe = name;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    public sealed class Row
    {
        public Row(string exe, string display) { Exe = exe; Display = display; }
        public string Exe { get; }
        public string Display { get; }
    }
}
