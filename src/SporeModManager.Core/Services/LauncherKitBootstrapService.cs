using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SporeModManager.Core.Services;

/// <summary>
/// Downloads the official ModAPI Launcher Kit installer and extracts just
/// ModAPI.DLLInjector.dll. The kit is MIT-licensed; its injector is required
/// by Spore to load ModAPI and mod DLLs.
/// </summary>
public class LauncherKitBootstrapService
{
    private const string InstallerUrl =
        "https://github.com/Spore-Community/ModAPI-Launcher-Kit/releases/latest/download/ModAPI.InterimSetup.exe";
    private const string EmbeddedInjectorResource = "Spode.Injector.ModAPI.DLLInjector.dll";
    private const string InjectorSha256 = "C992F52461EE043DC49AFA4277ED1D5155AAD91F786959B6DA8A480262EDE959";

    private readonly string _installDir;

    public LauncherKitBootstrapService(string? installDir = null)
    {
        _installDir = installDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SporeModManager", "LauncherKit");
    }

    public string InjectorPath => Path.Combine(_installDir, "ModAPI.DLLInjector.dll");

    public bool IsAvailable => FindExistingInjector() != null;

    public async Task<string> EnsureInjectorAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var existing = FindExistingInjector();
        if (existing != null)
        {
            if (!string.Equals(existing, InjectorPath, StringComparison.OrdinalIgnoreCase) || HasExpectedHash(existing))
                return existing;

            try { File.Delete(existing); } catch { }
        }

        if (TryExtractEmbeddedInjector(progress))
            return InjectorPath;

        Directory.CreateDirectory(_installDir);
        var setupPath = Path.Combine(_installDir, "ModAPI.InterimSetup.exe");

        progress?.Report("Downloading ModAPI Launcher Kit injector... 0%");
        using (var http = new HttpClient())
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SporeModManager/1.0");
            using var response = await http.GetAsync(InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(setupPath);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0)
                    progress?.Report($"Downloading ModAPI Launcher Kit injector... {read * 100.0 / total:0}%");
            }
        }

        progress?.Report("Extracting ModAPI.DLLInjector.dll...");
        ExtractInjector(setupPath, InjectorPath);
        try { File.Delete(setupPath); } catch { }
        progress?.Report("ModAPI.DLLInjector.dll is ready");
        return InjectorPath;
    }

    private static bool HasExpectedHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(hash, InjectorSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private bool TryExtractEmbeddedInjector(IProgress<string>? progress)
    {
        var assembly = Assembly.GetEntryAssembly();
        if (assembly == null) return false;

        using var resource = assembly.GetManifestResourceStream(EmbeddedInjectorResource);
        if (resource == null) return false;

        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        var bytes = memory.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(hash, InjectorSha256, StringComparison.OrdinalIgnoreCase))
            return false;

        Directory.CreateDirectory(_installDir);
        File.WriteAllBytes(InjectorPath, bytes);
        progress?.Report("ModAPI.DLLInjector.dll extracted from Spode.exe");
        return true;
    }

    private string? FindExistingInjector()
    {
        foreach (var candidate in GetCandidates())
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    private IEnumerable<string> GetCandidates()
    {
        yield return InjectorPath;
        yield return Path.Combine(AppContext.BaseDirectory, "ModAPI.DLLInjector.dll");
        yield return Path.Combine(AppContext.BaseDirectory, "dll", "ModAPI.DLLInjector.dll");

        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            if (string.IsNullOrEmpty(root)) continue;
            yield return Path.Combine(root, "Spore ModAPI Launcher Kit", "ModAPI.DLLInjector.dll");
            yield return Path.Combine(root, "SporeModAPI", "Launcher Kit", "ModAPI.DLLInjector.dll");
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
            yield return Path.Combine(local, "Spore ModAPI Launcher Kit", "ModAPI.DLLInjector.dll");
    }

    /// <summary>
    /// ModAPI.InterimSetup.exe is a PE with an embedded ZIP appended.
    /// ZipArchive cannot read it directly because of the PE overlay, so we
    /// slice from the first local file header to the end of central directory.
    /// </summary>
    private static void ExtractInjector(string setupPath, string outputPath)
    {
        var bytes = File.ReadAllBytes(setupPath);
        var span = bytes.AsSpan();
        int zipStart = span.IndexOf("PK\x03\x04"u8);
        int eocd = span.LastIndexOf("PK\x05\x06"u8);
        if (zipStart < 0 || eocd < zipStart)
            throw new InvalidDataException("ModAPI.InterimSetup.exe does not contain the expected embedded archive.");

        int commentLength = eocd + 22 <= bytes.Length ? BitConverter.ToUInt16(bytes, eocd + 20) : 0;
        int zipEnd = Math.Min(bytes.Length, eocd + 22 + commentLength);

        using var stream = new MemoryStream(bytes, zipStart, zipEnd - zipStart, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("ModAPI.DLLInjector.dll")
            ?? throw new InvalidDataException("ModAPI.DLLInjector.dll was not found in the Launcher Kit installer.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var input = entry.Open();
        using var output = File.Create(outputPath);
        input.CopyTo(output);
    }
}
