using System.IO.Compression;

namespace NexStrap.Services;

public sealed class StudioPackageInstallerService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private const int  MaxDownloadRetries = 5;
    private const int  BufferSize         = 65536;
    private const int  MaxSegments        = 4;
    private const long MinSegmentBytes    = 2 * 1024 * 1024;

    private long   _totalDownloadedBytes;
    private long   _totalPackedBytes;
    private string _currentPackageName    = string.Empty;
    private long   _totalExtractFiles;
    private long   _completedExtractFiles;

    private static readonly IReadOnlyDictionary<string, string> StudioPackageDirs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Libraries.zip"]                     = "",
            ["redist.zip"]                        = "",
            ["RobloxStudio.zip"]                  = "",
            ["WebView2.zip"]                      = "",
            ["WebView2RuntimeInstaller.zip"]      = "WebView2/",
            ["shaders.zip"]                       = "shaders/",
            ["ssl.zip"]                           = "ssl/",
            ["content-avatar.zip"]                = "content/avatar/",
            ["content-configs.zip"]               = "content/configs/",
            ["content-fonts.zip"]                 = "content/fonts/",
            ["content-sky.zip"]                   = "content/sky/",
            ["content-sounds.zip"]                = "content/sounds/",
            ["content-textures.zip"]              = "content/textures/",
            ["content-textures2.zip"]             = "content/textures/",
            ["content-textures3.zip"]             = "content/textures/",
            ["content-models.zip"]                = "content/models/",
            ["content-terrain.zip"]               = "content/terrain/",
            ["content-platform-fonts.zip"]        = "PlatformContent/pc/fonts/",
            ["content-platform-dictionaries.zip"] = "PlatformContent/pc/",
            ["extracontent-luapackages.zip"]      = "ExtraContent/LuaPackages/",
            ["extracontent-models.zip"]           = "ExtraContent/models/",
            ["extracontent-places.zip"]           = "ExtraContent/places/",
            ["extracontent-textures.zip"]         = "ExtraContent/textures/",
            ["extracontent-translations.zip"]     = "ExtraContent/translations/",
            ["BuiltInPlugins.zip"]                = "BuiltInPlugins/",
            ["BuiltInStandalonePlugins.zip"]      = "BuiltInStandalonePlugins/",
            ["ApplicationConfig.zip"]             = "ApplicationConfig/",
            ["NPRobloxProxy.zip"]                 = "",
            ["Plugins.zip"]                       = "Plugins/",
            ["StudioFonts.zip"]                   = "StudioFonts/",
            ["LibrariesQt5.zip"]                  = "",
            ["content-qt_translations.zip"]       = "content/qt_translations/",
            ["content-studio_svg_textures.zip"]   = "content/studio_svg_textures/",
            ["content-api-docs.zip"]              = "content/api_docs/",
            ["extracontent-scripts.zip"]          = "ExtraContent/scripts/",
            ["studiocontent-models.zip"]          = "StudioContent/models/",
            ["studiocontent-textures.zip"]        = "StudioContent/textures/",
        };

    static StudioPackageInstallerService()
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

        var url = $"{cdnBaseUrl}/version-{versionGuid}-{package.Name}";

        for (int attempt = 1; attempt <= MaxDownloadRetries; attempt++)
        {
            if (ct.IsCancellationRequested) return;
            long bytesThisAttempt = 0;

            try
            {
                if (package.CompressedSize >= MinSegmentBytes)
                {
                    if (await TryDownloadMultipartAsync(url, localPath, package, ct))
                        return;
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

                RobloxService.Log($"Studio DL attempt {attempt}/{MaxDownloadRetries} failed for {package.Name}: {ex.Message}");
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
            foreach (var (path, name) in downloadedPaths)
            {
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
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

    internal void ExtractPackage(
        string localPath,
        string packageName,
        string destDir,
        double extStart,
        double extEnd,
        RobloxProgressReporter reportProgress)
    {
        if (!File.Exists(localPath)) return;
        if (!packageName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;

        StudioPackageDirs.TryGetValue(packageName, out var subDir);
        subDir ??= "";
        var dest = string.IsNullOrEmpty(subDir)
            ? destDir
            : Path.Combine(destDir, subDir.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            using var archive = ZipFile.OpenRead(localPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                Interlocked.Increment(ref _completedExtractFiles);
                var total   = Interlocked.Read(ref _totalExtractFiles);
                var pct     = total > 0 ? extStart + Interlocked.Read(ref _completedExtractFiles) / (double)total * (extEnd - extStart) : extStart;
                reportProgress($"Extracting {entry.Name}", pct);

                var destPath = Path.Combine(dest, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                var dir      = Path.GetDirectoryName(destPath);
                if (dir != null) Directory.CreateDirectory(dir);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }
        catch { }
    }

    private async Task<bool> TryDownloadMultipartAsync(string url, string localPath,
        RobloxPackage package, CancellationToken ct)
    {
        var bytesAtStart = Interlocked.Read(ref _totalDownloadedBytes);
        try
        {
            long contentLength = package.CompressedSize;
            int  segs          = (int)Math.Min(MaxSegments, Math.Max(1, contentLength / MinSegmentBytes));
            long segLen        = contentLength / segs;

            var tempFiles = Enumerable.Range(0, segs).Select(_ => Path.GetTempFileName()).ToArray();
            try
            {
                await Task.WhenAll(Enumerable.Range(0, segs).Select(async i =>
                {
                    long from = i * segLen;
                    long to   = i == segs - 1 ? contentLength - 1 : from + segLen - 1;

                    using var req  = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);
                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();

                    await using var src = await resp.Content.ReadAsStreamAsync(ct);
                    await using var dst = new FileStream(tempFiles[i], FileMode.Create, FileAccess.Write, FileShare.Delete);
                    var buf = new byte[BufferSize];
                    int n;
                    while ((n = await src.ReadAsync(buf, ct)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n), ct);
                        Interlocked.Add(ref _totalDownloadedBytes, n);
                    }
                }));

                await using var dst = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.Delete);
                foreach (var tmp in tempFiles)
                {
                    await using var src = File.OpenRead(tmp);
                    await src.CopyToAsync(dst, ct);
                }

                dst.Seek(0, SeekOrigin.Begin);
                var hash = ComputeMd5(dst);
                if (hash != package.Signature)
                {
                    Interlocked.Add(ref _totalDownloadedBytes,
                        bytesAtStart - Interlocked.Read(ref _totalDownloadedBytes));
                    return false;
                }
                return true;
            }
            finally
            {
                foreach (var tmp in tempFiles)
                    try { File.Delete(tmp); } catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            Interlocked.Exchange(ref _totalDownloadedBytes, bytesAtStart);
            return false;
        }
    }

    private static string ComputeMd5(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        return ComputeMd5(fs);
    }

    private static string ComputeMd5(Stream stream)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
