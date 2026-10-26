using CtrlCpp.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace CtrlCpp.ViewModels;

public sealed class HistoryViewModel : INotifyPropertyChanged
{
    public ObservableCollection<ClipboardItemViewModel> Items { get; } = new();

    private ListCollectionView? _view;
    private string _search = "";

    public int TotalCount => Items.Count;

    private int _viewCount;
    public int ViewCount
    {
        get => _viewCount;
        private set { _viewCount = value; Raise(nameof(ViewCount)); }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; Raise(nameof(StatusText)); }
    }

    private string _emptyText = "暂无剪贴板记录\n复制的内容会出现在这里";
    public string EmptyText
    {
        get => _emptyText;
        private set { _emptyText = value; Raise(nameof(EmptyText)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Reload()
    {
        var rows = AppServices.Store.ListRecent(500);
        Items.Clear();
        foreach (var r in rows) Items.Add(new ClipboardItemViewModel(r));
        _view = CollectionViewSource.GetDefaultView(Items) as ListCollectionView;
        if (_view != null) _view.Filter = FilterItem;
        RaiseCounts();
    }

    private bool FilterItem(object o)
    {
        if (_search.Length == 0) return true;
        var vm = (ClipboardItemViewModel)o;
        var t = vm.Item.FullText;
        if (string.IsNullOrEmpty(t)) return false;
        return t.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public void ApplyFilter(string query)
    {
        _search = query.Trim();
        if (_view == null)
        {
            _view = CollectionViewSource.GetDefaultView(Items) as ListCollectionView;
            if (_view != null) _view.Filter = FilterItem;
        }
        _view?.Refresh();
        RaiseCounts();
    }

    public void Remove(ClipboardItemViewModel vm)
    {
        Items.Remove(vm);
        RaiseCounts();
    }

    private void RaiseCounts()
    {
        ViewCount = _view == null ? Items.Count : _view.Count;
        EmptyText = TotalCount == 0 ? "暂无剪贴板记录\n复制的内容会出现在这里" : "没有匹配的结果";
        StatusText = $"共 {ViewCount} 条 · Enter 粘贴 · Del 删除 · Esc 关闭";
    }

    public void RefreshTimes()
    {
        foreach (var vm in Items) vm.RefreshTime();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
