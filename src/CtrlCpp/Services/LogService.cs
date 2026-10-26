using System.IO;
namespace CtrlCpp.Services;

public static class LogService
{
    private static readonly object _lock = new();
    private static string _dir = "";

    public static void Init(string dir)
    {
        _dir = dir;
        try { Directory.CreateDirectory(dir); } catch { /* 忽略 */ }
        TrimOld();
        Log("---- 启动 ----");
    }

    public static void Log(string msg)
    {
        if (_dir.Length == 0) return;
        try
        {
            lock (_lock)
            {
                File.AppendAllText(
                    Path.Combine(_dir, $"{DateTime.Now:yyyy-MM-dd}.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n");
            }
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    public static void LogEx(string msg, Exception? ex)
    {
        if (ex == null) { Log(msg); return; }
        Log($"{msg} :: {ex.GetType().Name}: {ex.Message}\r\n{ex.StackTrace}");
    }

    private static void TrimOld()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_dir, "*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7))
                    File.Delete(f);
        }
        catch { /* 忽略 */ }
    }
}
