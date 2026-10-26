using System.Windows.Threading;

namespace CtrlCpp.Services;

/// <summary>保留期清扫 + 缓存上限清理。</summary>
public sealed class CleanupService
{
    private readonly HistoryStore _store;
    private readonly MediaStorage _media;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer;

    public CleanupService(Dispatcher dispatcher, HistoryStore store, MediaStorage media, SettingsService settings)
    {
        _store = store;
        _media = media;
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _timer.Tick += (_, _) => RunAll();
    }

    public void Start()
    {
        _timer.Start();
        RunAll();
    }

    public void RunAll()
    {
        _ = Task.Run(() =>
        {
            try { RunRetention(); }
            catch (Exception ex) { LogService.LogEx("保留期清扫失败", ex); }
            try { RunSizeLimit(); }
            catch (Exception ex) { LogService.LogEx("缓存上限清理失败", ex); }
            AppServices.NotifyHistoryChanged();
        });
    }

    public void RunRetention()
    {
        var cutoff = DateTime.UtcNow.AddDays(-_settings.Settings.RetentionDays).Ticks;
        var media = _store.DeleteOlderThan(cutoff);
        foreach (var m in media) _media.Delete(m);
        if (media.Count > 0) LogService.Log($"保留期清扫：删除 {media.Count} 个媒体文件");
    }

    public void RunSizeLimit()
    {
        var max = (long)_settings.Settings.CacheLimitMB * 1024 * 1024;
        while (true)
        {
            var media = _store.EvictOldest(max);
            if (media == null) break;
            foreach (var m in media) _media.Delete(m);
            if (media.Count == 0) break;
            LogService.Log($"缓存超限：已清理 {media.Count} 条最旧记录");
        }
    }
}
