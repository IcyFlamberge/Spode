using SporeModManager.Core.Models;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SporeModManager.Core.Services;

public class ModApiService
{
    public string CacheRoot { get; }
    public ModApiService(string? cacheRoot = null)
    {
        CacheRoot = cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SporeModManager", "ModApi");
        Directory.CreateDirectory(CacheRoot);
    }

    public string GetReleaseCacheDir(string tag) => Path.Combine(CacheRoot, Sanitize(tag));
    public string GetInstalledTagMarkerPath() => Path.Combine(CacheRoot, "installed_tag.txt");

    private static string Sanitize(string tag) => string.Concat(tag.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public bool IsCached(string tag)
    {
        var dir = GetReleaseCacheDir(tag);
        return Directory.Exists(dir) && File.Exists(Path.Combine(dir, "SporeModAPIdlls.zip"));
    }

    public async Task DownloadReleaseAsync(ModApiRelease release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = GetReleaseCacheDir(release.TagName);
        Directory.CreateDirectory(dir);

        var dllsAsset = release.Assets.FirstOrDefault(a => a.Name.Equals("SporeModAPIdlls.zip", StringComparison.OrdinalIgnoreCase));
        var legacyAsset = release.Assets.FirstOrDefault(a => a.Name.Equals("SporeModAPIlegacydlls.zip", StringComparison.OrdinalIgnoreCase));

        if (dllsAsset == null) throw new InvalidOperationException("Release has no SporeModAPIdlls.zip");

        // download dlls zip
        await DownloadFileAsync(dllsAsset.DownloadUrl, Path.Combine(dir, "SporeModAPIdlls.zip"), p => progress?.Report(p * 0.5), ct);
        if (legacyAsset != null)
            await DownloadFileAsync(legacyAsset.DownloadUrl, Path.Combine(dir, "SporeModAPIlegacydlls.zip"), p => progress?.Report(0.5 + p * 0.3), ct);

        // extract
        progress?.Report(0.85);
        var extractDir = Path.Combine(dir, "extracted");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);

        using (var zip = ZipFile.OpenRead(Path.Combine(dir, "SporeModAPIdlls.zip")))
            foreach (var e in zip.Entries) e.ExtractToFile(Path.Combine(extractDir, e.FullName), true);

        if (File.Exists(Path.Combine(dir, "SporeModAPIlegacydlls.zip")))
            using (var zip = ZipFile.OpenRead(Path.Combine(dir, "SporeModAPIlegacydlls.zip")))
                foreach (var e in zip.Entries) e.ExtractToFile(Path.Combine(extractDir, e.FullName), true);

        // store manifest with hashes
        var manifest = new Dictionary<string, string>();
        foreach (var f in Directory.EnumerateFiles(extractDir, "*", SearchOption.AllDirectories))
        {
            manifest[Path.GetRelativePath(extractDir, f)] = ComputeSha256(f);
        }
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        // store tag marker
        File.WriteAllText(Path.Combine(dir, "tag.txt"), release.TagName);

        progress?.Report(1.0);
    }

    private async Task DownloadFileAsync(string url, string dest, Action<double> progress, CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SporeModManager/1.0");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress(read / (double)total);
        }
    }

    public bool VerifyCacheIntegrity(string tag)
    {
        var dir = GetReleaseCacheDir(tag);
        var manifestPath = Path.Combine(dir, "manifest.json");
        var extractDir = Path.Combine(dir, "extracted");
        if (!File.Exists(manifestPath) || !Directory.Exists(extractDir)) return false;
        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath));
        if (manifest == null) return false;
        foreach (var kv in manifest)
        {
            var p = Path.Combine(extractDir, kv.Key);
            if (!File.Exists(p)) return false;
            if (ComputeSha256(p) != kv.Value) return false;
        }
        // required files
        foreach (var req in new[] { "SporeModAPIdlls.zip" })
            if (!File.Exists(Path.Combine(dir, req))) return false;
        return true;
    }

    public void InstallToLauncher(string tag, string launcherKitPath, IProgress<double>? progress = null)
    {
        var dir = GetReleaseCacheDir(tag);
        var extractDir = Path.Combine(dir, "extracted");
        if (!Directory.Exists(extractDir)) throw new DirectoryNotFoundException("ModAPI not downloaded/cached: " + tag);

        var coreLibs = Path.Combine(launcherKitPath, "coreLibs");
        Directory.CreateDirectory(coreLibs);
        Directory.CreateDirectory(Path.Combine(launcherKitPath, "mLibs"));

        var files = new[] { "SporeModAPI.combined.dll", "SporeModAPI.disk.dll", "SporeModAPI.march2017.dll", "SporeModAPI.lib" };
        int i = 0;
        foreach (var f in files)
        {
            var src = Path.Combine(extractDir, f);
            if (!File.Exists(src)) throw new FileNotFoundException("Missing in cache: " + f);
            File.Copy(src, Path.Combine(coreLibs, f), true);
            i++;
            progress?.Report(i / (double)(files.Length + 2));
        }
        // legacy
        foreach (var f in new[] { "SporeModAPI-disk.dll", "SporeModAPI-steam_patched.dll" })
        {
            var src = Path.Combine(extractDir, f);
            if (File.Exists(src))
            {
                File.Copy(src, Path.Combine(launcherKitPath, f), true);
            }
        }
        progress?.Report(1.0);
        File.WriteAllText(GetInstalledTagMarkerPath(), tag);
    }

    public string? GetInstalledTag()
    {
        var p = GetInstalledTagMarkerPath();
        return File.Exists(p) ? File.ReadAllText(p).Trim() : null;
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash);
    }
}