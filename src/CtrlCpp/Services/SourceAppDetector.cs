using System.IO;
using System.Text;
using static CtrlCpp.Interop.NativeConstants;
using static CtrlCpp.Interop.NativeMethods;

namespace CtrlCpp.Services;

/// <summary>识别剪贴板来源应用（GetClipboardOwner → 进程 exe 名）。</summary>
public static class SourceAppDetector
{
    public static string? GetOwnerExeName()
    {
        try
        {
            var owner = GetClipboardOwner();
            if (owner == IntPtr.Zero) return null;
            _ = GetWindowThreadProcessId(owner, out var pid);
            if (pid == 0) return null;
            if (pid == (uint)Environment.ProcessId) return null;

            var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref size) && size > 0)
                    return Path.GetFileName(sb.ToString(0, (int)size)).ToLowerInvariant();
                return null;
            }
            finally { CloseHandle(h); }
        }
        catch (Exception ex)
        {
            LogService.LogEx("识别剪贴板来源失败", ex);
            return null;
        }
    }
}
