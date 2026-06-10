using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace NexStrap.Services;

internal delegate void RobloxProgressReporter(
    string message,
    double percent,
    bool indeterminate = false,
    string? detail = null);

public sealed class RobloxPackageInstallerService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static readonly string RobloxDownloadsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "Downloads");

    private const int  MaxDownloadRetries = 5;
    private const int  BufferSize         = 65536;
    private const int  MaxSegments        = 4;
    private const long MinSegmentBytes    = 2 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> PackageDirs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Libraries.zip"]                     = "",
            ["RobloxApp.zip"]                     = "",
            ["redist.zip"]                        = "",
            ["shaders.zip"]                       = "shaders/",
            ["ssl.zip"]                           = "ssl/",
            ["WebView2.zip"]                      = "",
            ["WebView2RuntimeInstaller.zip"]      = "WebView2/",
            ["content-avatar.zip"]                = "content/avatar/",
            ["content-configs.zip"]               = "content/configs/",
            ["content-fonts.zip"]                 = "content/fonts/",
            ["content-sky.zip"]                   = "content/sky/",
            ["content-sounds.zip"]                = "content/sounds/",
            ["content-textures.zip"]              = "content/textures/",
            ["content-textures2.zip"]             = "content/textures/",
            ["content-textures3.zip"]             = "PlatformContent/pc/textures/",
            ["content-models.zip"]                = "content/models/",
            ["content-terrain.zip"]               = "PlatformContent/pc/terrain/",
            ["content-platform-fonts.zip"]        = "PlatformContent/pc/fonts/",
            ["content-platform-dictionaries.zip"] = "PlatformContent/pc/shared_compression_dictionaries/",
            ["extracontent-luapackages.zip"]      = "ExtraContent/LuaPackages/",
            ["extracontent-models.zip"]           = "ExtraContent/models/",
            ["extracontent-places.zip"]           = "ExtraContent/places/",
            ["extracontent-textures.zip"]         = "ExtraContent/textures/",
            ["extracontent-translations.zip"]     = "ExtraContent/translations/",
            ["NPRobloxProxy.zip"]                 = "",
        };

    private long   _totalDownloadedBytes;
    private long   _totalPackedBytes;
    private string _currentPackageName = string.Empty;
    private long   _totalExtractFiles;
    private long   _completedExtractFiles;

    static RobloxPackageInstallerService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
    }

    public long TotalDownloadedBytes => Interlocked.Read(ref _totalDownloadedBytes);

    public long TotalPackedBytes => Interlocked.Read(ref _totalPackedBytes);

    public string CurrentPackageName => _currentPackageName;

    internal void ResetDownloadProgress(long totalPackedBytes)
    {
        _totalDownloadedBytes = 0;
        _totalPackedBytes     = totalPackedBytes;
        _currentPackageName   = string.Empty;
    }

    internal void SetCurrentPackageName(string packageName)
    {
        _currentPackageName = packageName;
    }

    internal async Task DownloadPackageAsync(
        RobloxPackage package,
        string localPath,
        string cdnBaseUrl,
        string versionGuid,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        if (File.Exists(localPath) && ComputeMd5(localPath) == package.Signature)
        {
            Interlocked.Add(ref _totalDownloadedBytes, package.CompressedSize);
            return;
        }
        try { if (File.Exists(localPath)) File.Delete(localPath); } catch { }

        var robloxCached = Path.Combine(RobloxDownloadsDir, package.Signature);
        if (File.Exists(robloxCached))
        {
            try
            {
                File.Copy(robloxCached, localPath);
                Interlocked.Add(ref _totalDownloadedBytes, package.CompressedSize);
                return;
            }
            catch { }
        }

        var url = $"{cdnBaseUrl}/version-{versionGuid}-{package.Name}";

        for (int attempt = 1; attempt <= MaxDownloadRetries; attempt++)
        {
            if (ct.IsCancellationRequested) return;
            long bytesThisAttempt = 0;

            try
            {
                if (package.CompressedSize >= MinSegmentBytes)
                {
                    var downloaded = await TryDownloadMultipartAsync(url, localPath, package, ct);
                    if (downloaded) return;
                }

                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(localPath, FileMode.Create,
                    FileAccess.ReadWrite, FileShare.Delete);

                var buffer = new byte[BufferSize];
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    bytesThisAttempt += n;
                    Interlocked.Add(ref _totalDownloadedBytes, n);
                }

                dst.Seek(0, SeekOrigin.Begin);
                var hash = ComputeMd5(dst);
                if (hash != package.Signature)
                    throw new InvalidDataException(
                        $"MD5 mismatch for {package.Name}: expected {package.Signature}, got {hash}");

                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Interlocked.Add(ref _totalDownloadedBytes, -bytesThisAttempt);
                try { File.Delete(localPath); } catch { }

                RobloxService.Log($"Download attempt {attempt}/{MaxDownloadRetries} failed for {package.Name}: {ex.Message}");
                if (attempt >= MaxDownloadRetries) break;

                if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    url = "http://" + url[8..];

                await Task.Delay(500 * attempt, ct);
            }
        }
    }

    internal async Task CountExtractFilesAsync(
        IEnumerable<(string Path, string Name)> downloadedPaths,
        CancellationToken ct)
    {
        _totalExtractFiles     = 0;
        _completedExtractFiles = 0;

        await Task.Run(() =>
        {
            foreach (var (path, _) in downloadedPaths)
            {
                if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    continue;
                try
                {
                    using var z = ZipFile.OpenRead(path);
                    _totalExtractFiles += z.Entries.Count(e => !string.IsNullOrEmpty(e.Name));
                }
                catch { }
            }
        }, ct);
    }

    internal void ExtractPackageWithProgress(
        string archivePath,
        string packageName,
        string versionDir,
        double extStart,
        double extEnd,
        RobloxProgressReporter reportProgress)
    {
        try
        {
            if (!packageName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                reportProgress($"Installing {packageName}", extStart);
                File.Copy(archivePath, Path.Combine(versionDir, packageName), overwrite: true);
                return;
            }

            var sub  = PackageDirs.TryGetValue(packageName, out var d) ? d : "";
            var dest = string.IsNullOrEmpty(sub)
                ? versionDir
                : Path.Combine(versionDir, sub.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dest);

            var debugLog = string.Equals(packageName, "RobloxApp.zip", StringComparison.OrdinalIgnoreCase);
            if (debugLog)
                RobloxService.Log($"[DEBUG] RobloxApp.zip: archivePath={archivePath}, dest={dest}");

            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();

            if (debugLog)
            {
                RobloxService.Log($"[DEBUG] RobloxApp.zip entry count: {entries.Count}");
                foreach (var e in entries)
                    RobloxService.Log($"[DEBUG] RobloxApp.zip entry.FullName={e.FullName}");
            }

            foreach (var entry in entries)
            {
                var done = Interlocked.Increment(ref _completedExtractFiles);
                var pct  = _totalExtractFiles > 0
                    ? extStart + done / (double)_totalExtractFiles * (extEnd - extStart)
                    : extStart;
                reportProgress($"Extracting {entry.Name}", pct);

                var destPath = Path.Combine(dest, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                if (debugLog)
                    RobloxService.Log($"[DEBUG] RobloxApp.zip destPath={destPath}");
                var dir = Path.GetDirectoryName(destPath);
                if (dir != null) Directory.CreateDirectory(dir);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            RobloxService.Log($"Extraction failed for {packageName}: {ex.Message}");
        }
    }

    private async Task<bool> TryDownloadMultipartAsync(
        string url,
        string localPath,
        RobloxPackage package,
        CancellationToken ct)
    {
        var bytesAtStart = Interlocked.Read(ref _totalDownloadedBytes);
        try
        {
            long contentLength = package.CompressedSize;
            int  segs          = (int)Math.Min(MaxSegments, Math.Max(1, contentLength / MinSegmentBytes));
            if (segs <= 1) return false;

            await using (var alloc = new FileStream(localPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                alloc.SetLength(contentLength);
            }

            long segSize = contentLength / segs;

            await Task.WhenAll(Enumerable.Range(0, segs).Select(async i =>
            {
                long start = i * segSize;
                long end   = i == segs - 1 ? contentLength - 1 : start + segSize - 1;

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(start, end);

                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (resp.StatusCode != System.Net.HttpStatusCode.PartialContent)
                    throw new NotSupportedException($"Server returned {resp.StatusCode}, not 206");

                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(localPath, FileMode.Open, FileAccess.Write,
                    FileShare.Write, BufferSize, FileOptions.Asynchronous);
                dst.Position = start;

                var buf = new byte[BufferSize];
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    Interlocked.Add(ref _totalDownloadedBytes, n);
                }
            }));

            if (ComputeMd5(localPath) != package.Signature)
            {
                File.Delete(localPath);
                RobloxService.Log($"Multipart MD5 mismatch for {package.Name}, falling back to single download");
                Interlocked.Add(ref _totalDownloadedBytes, -contentLength);
                return false;
            }

            RobloxService.Log($"Multipart download: {package.Name} ({segs} segments)");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var bytesAdded = Interlocked.Read(ref _totalDownloadedBytes) - bytesAtStart;
            if (bytesAdded > 0) Interlocked.Add(ref _totalDownloadedBytes, -bytesAdded);
            RobloxService.Log($"Multipart download failed for {package.Name}: {ex.Message}, falling back");
            try { File.Delete(localPath); } catch { }
            return false;
        }
    }

    private static string ComputeMd5(string filePath)
    {
        using var md5    = MD5.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string ComputeMd5(Stream stream)
    {
        using var md5 = MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }
}
