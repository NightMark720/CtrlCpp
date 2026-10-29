using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace CtrlCpp.Services;

public sealed record UpdateRelease(
    string Tag,
    Version Version,
    string DownloadUrl,
    string ExpectedSha256,
    long Size);

public static class UpdateService
{
    private const string LatestReleaseUrl =
        "https://github.com/NightMark720/CtrlCpp/releases/latest";
    private const string ReleaseDownloadBase =
        "https://github.com/NightMark720/CtrlCpp/releases/download";
    private const long MaxDownloadBytes = 512L * 1024 * 1024;

    private static readonly HttpClient Client = CreateClient();
    private static readonly HttpClient LatestClient = CreateLatestClient();

    private static HttpClient CreateLatestClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CtrlCpp-UpdateChecker/1.0");
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CtrlCpp-UpdateChecker/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    public static Version CurrentVersion =>
        NormalizeVersion(typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0));

    public static async Task<UpdateRelease?> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        // 网络偶发失败时重试一次，避免用户看到"检查失败"
        for (var attempt = 0; ; attempt++)
        {
            try { return await CheckLatestOnceAsync(cancellationToken); }
            catch (HttpRequestException) when (attempt < 1)
            {
                LogService.LogEx("检查更新请求失败，重试一次", new Exception());
                await Task.Delay(1500, cancellationToken);
            }
        }
    }

    private static async Task<UpdateRelease?> CheckLatestOnceAsync(CancellationToken cancellationToken)
    {
        using var response = await LatestClient.GetAsync(LatestReleaseUrl,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var location = response.Headers.Location?.ToString()
                       ?? throw new InvalidDataException("无法获取最新版本链接");
        const string marker = "/releases/tag/";
        var markerIndex = location.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) throw new InvalidDataException("无法识别最新版本");
        var tag = location[(markerIndex + marker.Length)..].Trim('/');
        if (tag.Length == 0 || tag.Contains('/') || tag.Contains('?'))
            throw new InvalidDataException("最新版本号无效");

        var latestVersion = ParseVersion(tag);
        if (latestVersion <= CurrentVersion) return null;

        var hashUrl = $"{ReleaseDownloadBase}/{tag}/CtrlCpp.sha256";
        string expectedHash;
        using (var hashResponse = await Client.GetAsync(hashUrl, cancellationToken))
        {
            _ = hashResponse.EnsureSuccessStatusCode();
            var text = await hashResponse.Content.ReadAsStringAsync(cancellationToken);
            expectedHash = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
        }
        if (expectedHash.Length != 64 || expectedHash.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("最新版本缺少有效的文件校验信息");

        return new UpdateRelease(tag, latestVersion,
            $"{ReleaseDownloadBase}/{tag}/CtrlCpp.exe",
            expectedHash.ToLowerInvariant(), 0);
    }

    public static async Task<string> DownloadAsync(
        UpdateRelease release,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var updateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CtrlCpp", "update");
        Directory.CreateDirectory(updateDir);
        var newFile = Path.Combine(updateDir, "CtrlCpp.new.exe");

        try
        {
            using var response = await Client.GetAsync(release.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            _ = response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("下载地址不是安全的 HTTPS 连接");

            var expectedSize = response.Content.Headers.ContentLength ?? release.Size;
            if (expectedSize > MaxDownloadBytes || release.Size > MaxDownloadBytes)
                throw new InvalidDataException("更新文件过大");

            long total = 0;
            var lastProgress = -1;
            using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(newFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    total += read;
                    if (total > MaxDownloadBytes) throw new InvalidDataException("更新文件过大");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    if (expectedSize > 0 && progress != null)
                    {
                        var percent = (int)Math.Min(99, total * 100 / expectedSize);
                        if (percent != lastProgress)
                        {
                            lastProgress = percent;
                            progress.Report(percent);
                        }
                    }
                }

                var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actualHash, release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("文件校验失败，已取消更新");
            }

            if (release.Size > 0 && total != release.Size)
                throw new InvalidDataException("下载文件大小不一致");
            ValidateDownloadedFile(newFile, release.ExpectedSha256, release.Version);

            progress?.Report(100);
            return newFile;
        }
        catch
        {
            try { if (File.Exists(newFile)) File.Delete(newFile); } catch { /* 忽略 */ }
            throw;
        }
    }

    /// <summary>校验下载文件：SHA-256、可执行文件头、文件版本号。</summary>
    public static void ValidateDownloadedFile(string path, string expectedSha256, Version expectedVersion)
    {
        using var check = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (check.Length < 1024 * 1024 || check.ReadByte() != 'M' || check.ReadByte() != 'Z')
            throw new InvalidDataException("下载内容不是有效的 Windows 程序");

        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("文件校验失败，已取消更新");

        var fileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
        if (fileVersion == null || ParseVersion(fileVersion) != NormalizeVersion(expectedVersion))
            throw new InvalidDataException("下载文件版本不一致");
    }

    public static void StartApplyUpdate(string newFile, bool restart = true)
    {
        var targetFile = Environment.ProcessPath
                         ?? throw new InvalidOperationException("无法确定程序路径");
        var updateDir = Path.GetDirectoryName(newFile)!;
        var scriptPath = Path.Combine(updateDir, "apply-update.ps1");
        // 必须带 UTF-8 BOM：PowerShell 5.1 会把无 BOM 的 UTF-8 按 GBK 解析，脚本里的中文会乱码并导致语法错误
        File.WriteAllText(scriptPath, ApplyScript, new System.Text.UTF8Encoding(true));

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = updateDir,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-TargetPath");
        startInfo.ArgumentList.Add(targetFile);
        startInfo.ArgumentList.Add("-NewPath");
        startInfo.ArgumentList.Add(newFile);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        if (!restart) startInfo.ArgumentList.Add("-NoRestart");
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动更新程序");
    }

    /// <summary>解析版本号并统一为 4 段（1.4 与 1.4.0.0 视为相同，避免比较失败）。</summary>
    private static Version ParseVersion(string text)
    {
        var value = text.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(value, out var version))
            throw new InvalidDataException($"无法识别版本号：{text}");
        return NormalizeVersion(version);
    }

    private static Version NormalizeVersion(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    private const string ApplyScript = """
param(
    [Parameter(Mandatory=$true)][string]$TargetPath,
    [Parameter(Mandatory=$true)][string]$NewPath,
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [switch]$NoRestart
)
$ErrorActionPreference = 'Stop'
$backup = "$TargetPath.previous.exe"
$logFile = Join-Path $PSScriptRoot 'update.log'
function Write-UpdateLog([string]$message) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message" | Out-File $logFile -Append -Encoding utf8
}
try {
    Write-UpdateLog "开始更新 $TargetPath"
    $deadline = (Get-Date).AddSeconds(60)
    # ProcessId 无效（0 或负数）时直接跳过等待，避免 Get-Process 命中 Idle 进程
    if ($ProcessId -gt 0) {
        while ((Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 250
        }
        if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) { throw '原程序未能及时退出' }
    }

    Copy-Item $TargetPath $backup -Force
    $copied = $false
    for ($i = 0; $i -lt 20 -and -not $copied; $i++) {
        try { Copy-Item $NewPath $TargetPath -Force; $copied = $true }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $copied) { throw '替换程序文件失败' }

    if (-not $NoRestart) { Start-Process $TargetPath }
    Remove-Item $NewPath,$backup -Force -ErrorAction SilentlyContinue
    Write-UpdateLog '更新完成，已重新启动'
}
catch {
    Write-UpdateLog "更新失败：$($_.Exception.Message)"
    if (Test-Path $backup) {
        Copy-Item $backup $TargetPath -Force -ErrorAction SilentlyContinue
        Remove-Item $backup -Force -ErrorAction SilentlyContinue
    }
    if (-not $NoRestart -and (Test-Path $TargetPath)) { Start-Process $TargetPath }
    exit 1
}
""";
}
