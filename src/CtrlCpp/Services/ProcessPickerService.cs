using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static CtrlCpp.Interop.NativeConstants;
using static CtrlCpp.Interop.NativeMethods;

namespace CtrlCpp.Services;

public sealed record ProcessInfoRow(string ExeName, string Description, string Display);

/// <summary>枚举有可见窗口的进程，供“排除应用”选择器使用。</summary>
public static class ProcessPickerService
{
    public static List<ProcessInfoRow> GetVisibleWindowProcesses()
    {
        var list = new List<ProcessInfoRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _ = EnumWindows((h, _) =>
        {
            try
            {
                if (!IsWindowVisible(h)) return true;
                if (GetWindowTextLength(h) == 0) return true;
                var exStyle = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;
                GetWindowThreadProcessId(h, out var pid);
                if (pid == 0 || pid == (uint)Environment.ProcessId) return true;

                var path = GetProcessImagePath(pid);
                if (path == null) return true;
                var exe = Path.GetFileName(path).ToLowerInvariant();
                if (exe.Length == 0 || !seen.Add(exe)) return true;

                string desc = "";
                try { desc = FileVersionInfo.GetVersionInfo(path).FileDescription ?? ""; }
                catch { /* 忽略 */ }

                var display = desc.Length > 0 ? $"{exe} — {desc}" : exe;
                list.Add(new ProcessInfoRow(exe, desc, display));
            }
            catch { /* 单个窗口失败不影响枚举 */ }
            return true;
        }, IntPtr.Zero);

        return list.OrderBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? GetProcessImagePath(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) && size > 0
                ? sb.ToString(0, (int)size)
                : null;
        }
        finally { CloseHandle(h); }
    }
}
