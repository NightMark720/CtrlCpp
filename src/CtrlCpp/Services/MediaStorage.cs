using System.IO;
namespace CtrlCpp.Services;

/// <summary>图片媒体文件存储（内容寻址，原子写入）。</summary>
public sealed class MediaStorage
{
    public string MediaDir { get; private set; } = "";

    public void Init(string mediaDir)
    {
        MediaDir = mediaDir;
        Directory.CreateDirectory(mediaDir);
    }

    public string ResolveFullPath(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        return Path.Combine(MediaDir, name);
    }

    public string SavePng(byte[] png, string hash16)
    {
        var fileName = $"img_{hash16}.png";
        var full = Path.Combine(MediaDir, fileName);
        if (File.Exists(full)) return $"media\\{fileName}";
        var tmp = full + ".tmp";
        File.WriteAllBytes(tmp, png);
        try { File.Move(tmp, full); }
        catch (IOException)
        {
            // 极端情况：同名文件刚被其他线程写入
            try { File.Delete(tmp); } catch { /* 忽略 */ }
        }
        return $"media\\{fileName}";
    }

    public byte[] ReadBytes(string relativePath)
    {
        return File.ReadAllBytes(ResolveFullPath(relativePath));
    }

    public void Delete(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;
        var full = ResolveFullPath(relativePath);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (File.Exists(full)) File.Delete(full);
                return;
            }
            catch (IOException) when (attempt < 2) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) when (attempt < 2) { Thread.Sleep(200); }
            catch (Exception ex) { LogService.LogEx($"删除媒体文件失败 {relativePath}", ex); return; }
        }
    }

    public void ClearAll()
    {
        try
        {
            foreach (var f in Directory.GetFiles(MediaDir)) File.Delete(f);
        }
        catch (Exception ex) { LogService.LogEx("清空媒体目录失败", ex); }
    }

    /// <summary>删除未被数据库引用的媒体文件（仅在数据库成功打开后调用）。</summary>
    public void SweepOrphans(IEnumerable<string> referencedRelativePaths)
    {
        var referenced = new HashSet<string>(referencedRelativePaths.Select(pp => Path.GetFileName(pp)!), StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in Directory.GetFiles(MediaDir, "img_*.png"))
                if (!referenced.Contains(Path.GetFileName(f)))
                    File.Delete(f);
            // 崩溃/强杀遗留的半截临时文件（引用恒为空，直接清理）
            foreach (var f in Directory.GetFiles(MediaDir, "*.png.tmp"))
                File.Delete(f);
        }
        catch (Exception ex) { LogService.LogEx("孤儿媒体清扫失败", ex); }
    }
}
