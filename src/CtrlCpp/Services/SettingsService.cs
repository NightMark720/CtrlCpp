using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using CtrlCpp.Models;

namespace CtrlCpp.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string FilePath { get; }
    public AppSettings Settings { get; private set; } = new();
    public bool IsFirstRun { get; private set; }

    public event Action? Changed;

    public SettingsService(string filePath) => FilePath = filePath;

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings();
            else
                IsFirstRun = true;
        }
        catch (Exception ex)
        {
            LogService.LogEx("读取设置失败，使用默认设置", ex);
            Settings = new AppSettings();
        }
        Sanitize();
    }

    private void Sanitize()
    {
        Settings.RetentionDays = Math.Clamp(Settings.RetentionDays, 1, 30);
        Settings.CacheLimitMB = Math.Clamp(Settings.CacheLimitMB, 256, 20480);
        if (!HotkeyFormat.IsValid((HotkeyMods)Settings.HotkeyMods, Settings.HotkeyVk))
        {
            Settings.HotkeyMods = HotkeyFormat.DefaultMods;
            Settings.HotkeyVk = HotkeyFormat.DefaultVk;
        }
        // 统一存小写完整 exe 名；用户输入不带 .exe 时自动补全
        Settings.ExcludedApps = Settings.ExcludedApps
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a =>
            {
                var n = a.Trim().ToLowerInvariant();
                return n.EndsWith(".exe", StringComparison.Ordinal) ? n : n + ".exe";
            })
            .Distinct()
            .ToList();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, JsonOpts));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
            Changed?.Invoke();
        }
        catch (Exception ex) { LogService.LogEx("保存设置失败", ex); }
    }

    public bool IsExcluded(string? exeName)
    {
        if (exeName == null) return false;
        // 快照引用读取（列表由 UI 线程整表替换，避免与读取线程的并发修改竞态）
        var snapshot = Settings.ExcludedApps;
        return snapshot.Contains(exeName.ToLowerInvariant());
    }
}
