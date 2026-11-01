using SporeModManager.Core.Models;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace SporeModManager.Core.Services;

public class ModInstallationService
{
    public string LauncherKitPath { get; }
    private readonly GamePathService _gamePaths;

    public ModInstallationService(string launcherKitPath, GamePathService gamePaths)
    {
        LauncherKitPath = launcherKitPath;
        _gamePaths = gamePaths;
    }

    public bool IsGamePathConfigured => _gamePaths.IsConfigured;

    private string GaDataPath => _gamePaths.GalacticAdventuresDataPath ?? throw new DirectoryNotFoundException("Galactic Adventures DataEP1 folder not found. Set the game path in Settings.");
    private string SporeDataPath => _gamePaths.SporeDataPath ?? throw new DirectoryNotFoundException("Spore Data folder not found. Set the game path in Settings.");
    private string ModConfigsPath => Path.Combine(LauncherKitPath, "ModConfigs");
    private string ModSettingsPath => Path.Combine(LauncherKitPath, "ModSettings");

    /// <summary>
    /// Central library of original mod files. Manual installs are copied here
    /// before touching the game, so the manager can reinstall them later even
    /// if the original download is gone.
    /// </summary>
    public string ModsLibraryPath => Path.Combine(LauncherKitPath, "Mods");

    /// <summary>
    /// Copies the mod into the library unless an identical file is already
    /// there (same name, size and timestamp). Returns the library path.
    /// </summary>
    public string EnsureModInLibrary(string inputPath)
    {
        Directory.CreateDirectory(ModsLibraryPath);
        var source = new FileInfo(inputPath);
        var destinationPath = Path.Combine(ModsLibraryPath, source.Name);

        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            return destinationPath;

        if (File.Exists(destinationPath))
        {
            var existing = new FileInfo(destinationPath);
            if (existing.Length == source.Length && existing.LastWriteTimeUtc == source.LastWriteTimeUtc)
                return destinationPath;
        }

        File.Copy(inputPath, destinationPath, true);
        File.SetLastWriteTimeUtc(destinationPath, source.LastWriteTimeUtc);
        return destinationPath;
    }

    private readonly PackageModInfoService _packageInfo = new();

    /// <summary>
    /// Puts a .package mod into a dedicated library folder:
    /// Mods\&lt;mod name&gt;\&lt;file&gt;.package + generated ModInfo.xml.
    /// Reuses an identical file if it is already there.
    /// </summary>
    public PackageLibraryEntry EnsurePackageInLibrary(string inputPath, string targetGame = "GalacticAdventures")
    {
        Directory.CreateDirectory(ModsLibraryPath);
        var source = new FileInfo(inputPath);
        var baseFolderName = SanitizeFolderName(Path.GetFileNameWithoutExtension(source.Name));
        var folder = Path.Combine(ModsLibraryPath, baseFolderName);

        // A different package with the same base name must not overwrite the first one.
        if (Directory.Exists(folder))
        {
            var otherPackages = Directory.EnumerateFiles(folder, "*.package")
                .Where(p => !string.Equals(Path.GetFileName(p), source.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (otherPackages.Count > 0)
                folder = Path.Combine(ModsLibraryPath, baseFolderName + "-" + ShortHash(source.FullName + source.Length));
        }

        Directory.CreateDirectory(folder);
        var destinationPath = Path.Combine(folder, source.Name);

        if (!File.Exists(destinationPath) ||
            new FileInfo(destinationPath).Length != source.Length ||
            new FileInfo(destinationPath).LastWriteTimeUtc != source.LastWriteTimeUtc)
        {
            File.Copy(inputPath, destinationPath, true);
            File.SetLastWriteTimeUtc(destinationPath, source.LastWriteTimeUtc);
        }

        var metadata = _packageInfo.EnsureModInfo(folder, source.Name, targetGame);
        return new PackageLibraryEntry
        {
            PackagePath = destinationPath,
            ModInfoPath = metadata.ModInfoPath,
            Metadata = metadata
        };
    }

    public InstalledMod InstallPackage(string inputPath, string modName, InstalledModsStore store, bool copyToLibrary = true, string targetGame = "GalacticAdventures", PackageModMetadata? metadata = null)
    {
        PackageLibraryEntry? entry = null;
        if (copyToLibrary)
        {
            entry = EnsurePackageInLibrary(inputPath, targetGame);
            inputPath = entry.PackagePath;
            metadata = entry.Metadata;
        }
        metadata ??= _packageInfo.Generate(inputPath, targetGame);

        var dataPath = string.Equals(metadata.TargetGame, "Spore", StringComparison.OrdinalIgnoreCase)
            ? SporeDataPath
            : GaDataPath;
        var fileName = Path.GetFileName(inputPath);
        var outPath = Path.Combine(dataPath, fileName);
        CopyIfChanged(inputPath, outPath);

        var mod = store.AddMod(modName, metadata.Unique);
        mod.DisplayName = metadata.DisplayName;
        mod.Version = metadata.Version;
        mod.Author = metadata.Author;
        mod.Description = metadata.Description;
        mod.ModType = "package";
        mod.ManifestPath = entry?.ModInfoPath ?? metadata.ModInfoPath;
        mod.InstalledFiles.Add(new InstalledFile(fileName, metadata.TargetGame));
        return mod;
    }

    private static string SanitizeFolderName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "package_mod" : result;
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    private readonly List<InstalledFile> _currentFiles = new();
    private IProgress<double>? _fileProgress;
    private double _fileProgressOffset;
    private double _fileProgressScale = 1.0;
    private int _fileTotal = 1;
    private int _fileDone;

    public InstalledMod? InstallSporeMod(string inputPath, string modName, InstalledModsStore store, IProgress<double>? progress = null, bool copyToLibrary = true)
    {
        var sourcePath = copyToLibrary ? EnsureModInLibrary(inputPath) : inputPath;
        using var zip = ZipFile.OpenRead(sourcePath);
        var xmlEntry = zip.GetEntry("ModInfo.xml");
        if (xmlEntry != null)
        {
            _currentFiles.Clear();
            // parse
            ModManifest manifest;
            using (var s = xmlEntry.Open()) manifest = ModManifest.Load(s);

            // dllsBuild check
            if (manifest.DllsBuild != null)
            {
                // compare to installed ModAPI? We'll skip hard block; could throw. Keep as warning no-op.
            }

            var modPath = Path.Combine(ModConfigsPath, modName);
            var sourceStamp = SourceStamp(sourcePath);
            var markerPath = Path.Combine(modPath, ".smm-source");
            var markerMatches = File.Exists(markerPath) && File.ReadAllText(markerPath).Trim() == sourceStamp;
            var alreadyExtracted = Directory.Exists(modPath)
                && File.Exists(Path.Combine(modPath, "ModInfo.xml"))
                && (markerMatches || IsExtractionComplete(zip, modPath));

            if (!alreadyExtracted)
            {
                if (Directory.Exists(modPath)) DeleteFolder(modPath);
                Directory.CreateDirectory(modPath);
                var extractProgress = progress == null ? null : new Progress<double>(p => progress.Report(p * 0.5));
                ExtractZipWithProgress(zip, modPath, extractProgress);
                File.WriteAllText(markerPath, sourceStamp);
            }
            else if (!markerMatches)
            {
                File.WriteAllText(markerPath, sourceStamp);
            }

            _fileProgress = progress;
            _fileProgressOffset = alreadyExtracted ? 0.0 : 0.5;
            _fileProgressScale = alreadyExtracted ? 1.0 : 0.5;
            _fileTotal = Math.Max(1, CountManifestFiles(manifest));
            _fileDone = 0;

            // removals (only on install)
            foreach (var r in manifest.Removals) RemoveFiles(r);

            // prerequisites
            foreach (var p in manifest.Prerequisites) HandleComponent(p, modPath, true);

            // components: if hasCustomInstaller, install defaultChecked ones; else all components? Reference installs based on UI. Auto: defaultChecked true.
            if (manifest.HasCustomInstaller)
            {
                foreach (var c in manifest.Components)
                    if (c.DefaultChecked) HandleComponent(c, modPath, true);
                foreach (var group in manifest.ComponentGroups)
                {
                    var selected = group.FirstOrDefault(c => c.DefaultChecked) ?? group.FirstOrDefault();
                    if (selected != null) HandleComponent(selected, modPath, true);
                }
            }
            else
            {
                foreach (var c in manifest.Components) HandleComponent(c, modPath, true);
                foreach (var group in manifest.ComponentGroups)
                    foreach (var c in group) HandleComponent(c, modPath, true);
            }

            // compat files
            foreach (var c in manifest.CompatFiles) HandleCompatFile(c, modPath);

            // settings storage (empty)
            var settingsDir = Path.Combine(ModSettingsPath, manifest.Unique);
            Directory.CreateDirectory(settingsDir);

            // track
            // remove existing
            for (int i = 0; i < store.ModConfigurations.Count; i++)
            {
                var sel = store.ModConfigurations.ElementAt(i);
                if (sel.Name == modName || sel.Unique == manifest.Unique)
                {
                    store.ModConfigurations.Remove(sel);
                    if (i > 0) i--;
                }
            }
            var mod = store.AddMod(modName, manifest.Unique);
            mod.DisplayName = manifest.DisplayName;
            mod.Version = manifest.Version;
            mod.Author = manifest.Author;
            mod.Description = manifest.Description;
            mod.ModType = "sporemod";
            mod.ManifestPath = Path.Combine(modPath, "ModInfo.xml");
            mod.Dependencies = manifest.Depends;
            mod.DependenciesVersions = manifest.DependsVersions;
            mod.ConfiguratorPath = Path.Combine(modPath, "ModInfo.xml");
            mod.InstalledFiles.Clear();
            foreach (var f in _currentFiles) mod.InstalledFiles.Add(new InstalledFile(f.Name, f.Game));
            _fileProgress?.Report(1.0);
            _fileProgress = null;
            store.Save(Path.Combine(LauncherKitPath, InstalledModsStore.FileName));
            return mod;
        }
        else
        {
            // fallback: extract all by extension
            var mod = store.AddMod(modName);
            mod.DisplayName = modName;
            mod.ModType = "sporemod";
            mod.ManifestPath = string.Empty;
            using (var archive = ZipFile.Open(sourcePath, ZipArchiveMode.Read))
            {
                int total = archive.Entries.Count;
                int done = 0;
                string configsPath = ModConfigsPath;
                Directory.CreateDirectory(configsPath);
                var modPath = Path.Combine(configsPath, modName);
                Directory.CreateDirectory(modPath);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) { done++; continue; }
                    var type = GetFileType(entry.FullName);
                    var outputPath = GetOutputPath(type);
                    mod.AddFile(entry.Name, GameFromType(type));
                    var configOut = Path.Combine(modPath, entry.Name);
                    entry.ExtractToFile(configOut, true);
                    if (outputPath != null)
                    {
                        var fileOut = Path.Combine(outputPath, entry.Name);
                        File.Copy(configOut, fileOut, true);
                    }
                    done++;
                    progress?.Report(done / (double)total);
                }
            }
            store.Save(Path.Combine(LauncherKitPath, InstalledModsStore.FileName));
            return mod;
        }
    }

    private static void CopyIfChanged(string source, string target)
    {
        if (!File.Exists(source)) return;
        if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length)
            return;
        if (File.Exists(target)) File.Delete(target);
        File.Copy(source, target, true);
    }

    private static int CountManifestFiles(ModManifest manifest)
    {
        int total = manifest.Prerequisites.Sum(c => c.FileNames.Count);
        if (manifest.HasCustomInstaller)
        {
            total += manifest.Components.Where(c => c.DefaultChecked).Sum(c => c.FileNames.Count);
            foreach (var group in manifest.ComponentGroups)
            {
                var selected = group.FirstOrDefault(c => c.DefaultChecked) ?? group.FirstOrDefault();
                if (selected != null) total += selected.FileNames.Count;
            }
        }
        else
        {
            total += manifest.Components.Sum(c => c.FileNames.Count);
            foreach (var group in manifest.ComponentGroups)
                total += group.Sum(c => c.FileNames.Count);
        }
        total += manifest.CompatFiles.Sum(c => c.FileNames.Count);
        return Math.Max(total, 1);
    }

    private static bool IsExtractionComplete(ZipArchive zip, string modPath)
    {
        foreach (var entry in zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
        {
            var path = Path.Combine(modPath, entry.Name);
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length != entry.Length) return false;
        }
        return true;
    }

    private static string SourceStamp(string path)
    {
        var info = new FileInfo(path);
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }

    private static void ExtractZipWithProgress(ZipArchive zip, string modPath, IProgress<double>? progress)
    {
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        long total = entries.Sum(e => e.Length);
        long done = 0;
        int index = 0;
        foreach (var entry in entries)
        {
            var destination = Path.Combine(modPath, entry.Name);
            using (var input = entry.Open())
            using (var output = File.Create(destination))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    done += read;
                    if (total > 0) progress?.Report(done / (double)total);
                }
            }
            index++;
            if (total == 0) progress?.Report(index / (double)entries.Count);
        }
        progress?.Report(1.0);
    }

    private void HandleComponent(ModComponent c, string modPath, bool copy)
    {
        for (int i = 0; i < c.FileNames.Count; i++)
        {
            HandleFile(c, i, modPath, copy);
        }
    }

    private void HandleCompatFile(ModComponent c, string modPath)
    {
        // check targets exist
        bool allExist = true;
        var targets = new List<string>();
        for (int i = 0; i < c.CompatTargetFileNames.Count; i++)
        {
            var game = (i < c.CompatTargetGames.Count) ? c.CompatTargetGames[i] : "";
            var targetPaths = GetTargetPaths(c.CompatTargetFileNames[i], game);
            if (targetPaths.Length == 1)
            {
                if (!File.Exists(targetPaths[0])) { allExist = false; break; }
            }
            else if (targetPaths.Length == 2)
            {
                if (!(File.Exists(targetPaths[0]) || File.Exists(targetPaths[1]))) { allExist = false; break; }
            }
            targets.AddRange(targetPaths);
        }
        if (!allExist) return;
        if (c.RemoveTargets)
        {
            foreach (var t in targets) if (File.Exists(t)) File.Delete(t);
        }
        HandleComponent(c, modPath, true);
    }

    private void HandleFile(ModComponent c, int index, string modPath, bool copy)
    {
        var fileName = c.FileNames[index];
        var game = (index < c.Games.Count) ? c.Games[index] : "";
        bool isDll = string.IsNullOrWhiteSpace(game) || game.Equals("None", StringComparison.OrdinalIgnoreCase) || game.Equals("launcher", StringComparison.OrdinalIgnoreCase);
        // map game strings
        if (game.Equals("GalacticAdventures", StringComparison.OrdinalIgnoreCase)) game = "GalacticAdventures";
        else if (game.Equals("Spore", StringComparison.OrdinalIgnoreCase)) game = "Spore";
        else game = "";

        if (game == "GalacticAdventures")
        {
            _currentFiles.Add(new InstalledFile(fileName, "GalacticAdventures"));
            if (copy) CopyIfChanged(Path.Combine(modPath, fileName), Path.Combine(GaDataPath, fileName));
        }
        else if (game == "Spore")
        {
            _currentFiles.Add(new InstalledFile(fileName, "Spore"));
            if (copy) CopyIfChanged(Path.Combine(modPath, fileName), Path.Combine(SporeDataPath, fileName));
        }
        else
        {
            // dll / launcher folder (legacy handling)
            isDll = true;
            HandleDll(fileName, modPath, copy);
        }

        _fileDone++;
        _fileProgress?.Report(_fileProgressOffset + _fileProgressScale * Math.Min(1.0, _fileDone / (double)_fileTotal));
    }

    private void HandleDll(string fileName, string modPath, bool copy)
    {
        // legacy placement
        if (fileName.EndsWith("-disk.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("-steam.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("-steam_patched.dll", StringComparison.OrdinalIgnoreCase))
        {
            _currentFiles.Add(new InstalledFile(fileName, "None"));
            if (copy) CopyIfChanged(Path.Combine(modPath, fileName), Path.Combine(LauncherKitPath, fileName));
        }
        else
        {
            // create variants
            foreach (var suffix in new[] { "-disk.dll", "-steam.dll", "-steam_patched.dll" })
            {
                var outName = fileName.Replace(".dll", suffix, StringComparison.OrdinalIgnoreCase);
                _currentFiles.Add(new InstalledFile(outName, "None"));
                if (copy)
                {
                    var inName = fileName.Replace(".dll", suffix, StringComparison.OrdinalIgnoreCase);
                    CopyIfChanged(Path.Combine(modPath, inName), Path.Combine(LauncherKitPath, outName));
                }
            }
        }
    }

    private void RemoveFiles(ModRemoval r)
    {
        for (int i = 0; i < r.FileNames.Count; i++)
        {
            var game = (i < r.Games.Count) ? r.Games[i] : "";
            DirectoryInfo[] dirs;
            if (game.Equals("GalacticAdventures", StringComparison.OrdinalIgnoreCase))
                dirs = new[] { new DirectoryInfo(GaDataPath) };
            else if (game.Equals("Spore", StringComparison.OrdinalIgnoreCase))
                dirs = new[] { new DirectoryInfo(SporeDataPath) };
            else
                dirs = new[] { new DirectoryInfo(LauncherKitPath), new DirectoryInfo(Path.Combine(LauncherKitPath, "mLibs")) };
            var pattern = r.FileNames[i];
            foreach (var d in dirs)
            {
                if (!d.Exists) continue;
                foreach (var f in d.EnumerateFiles(pattern))
                    if (File.Exists(f.FullName)) File.Delete(f.FullName);
            }
        }
    }

    private string[] GetTargetPaths(string fileName, string game)
    {
        if (game.Equals("GalacticAdventures", StringComparison.OrdinalIgnoreCase))
            return new[] { Path.Combine(GaDataPath, fileName) };
        if (game.Equals("Spore", StringComparison.OrdinalIgnoreCase))
            return new[] { Path.Combine(SporeDataPath, fileName) };
        return new[] { Path.Combine(LauncherKitPath, fileName), Path.Combine(LauncherKitPath, "mLibs", fileName) };
    }

    private static FileType GetFileType(string fileName)
    {
        fileName = fileName.ToLowerInvariant();
        if (fileName.EndsWith(".package")) return FileType.Package;
        if (fileName.EndsWith(".dll")) return FileType.DLL;
        if (fileName.EndsWith(".sporemod")) return FileType.SporeMod;
        return FileType.None;
    }

    private string? GetOutputPath(FileType type) => type switch
    {
        FileType.Package => GaDataPath,
        FileType.Spore_Package => SporeDataPath,
        FileType.DLL => LauncherKitPath,
        _ => null
    };

    private static string GameFromType(FileType type) => type switch
    {
        FileType.Package => "GalacticAdventures",
        FileType.Spore_Package => "Spore",
        _ => "None"
    };

    private static void DeleteFolder(string path)
    {
        foreach (var s in Directory.EnumerateFiles(path)) File.Delete(s);
        foreach (var s in Directory.EnumerateDirectories(path)) DeleteFolder(s);
        Directory.Delete(path);
    }

    public bool AreInstalledFilesPresent(InstalledMod mod)
    {
        if (mod.InstalledFiles.Count == 0) return false;
        foreach (var f in mod.InstalledFiles)
        {
            string? basePath = f.Game switch
            {
                "GalacticAdventures" => GaDataPath,
                "Spore" => SporeDataPath,
                "None" => LauncherKitPath,
                _ => null
            };
            if (basePath == null) continue;
            if (!File.Exists(Path.Combine(basePath, f.Name))) return false;
        }
        return true;
    }

    public void RemoveMod(InstalledMod mod, InstalledModsStore store)
    {
        foreach (var f in mod.InstalledFiles)
        {
            // Do not delete a file that is still referenced by another installed mod.
            bool shared = store.ModConfigurations
                .Where(m => !string.Equals(m.Name, mod.Name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(m => m.InstalledFiles)
                .Any(other => string.Equals(other.Name, f.Name, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(other.Game, f.Game, StringComparison.OrdinalIgnoreCase));
            if (shared) continue;

            string? outputPath = f.Game switch
            {
                "GalacticAdventures" => GaDataPath,
                "Spore" => SporeDataPath,
                "None" => LauncherKitPath,
                _ => null
            };
            if (outputPath != null)
            {
                var p = Path.Combine(outputPath, f.Name);
                if (File.Exists(p)) File.Delete(p);
            }
        }
        // remove mod config folder
        var cfg = Path.Combine(ModConfigsPath, mod.Name);
        if (Directory.Exists(cfg)) DeleteFolder(cfg);
        store.RemoveMod(mod);
        store.Save(Path.Combine(LauncherKitPath, InstalledModsStore.FileName));
    }

    private enum FileType { None, Package, Spore_Package, DLL, SporeMod }
}

// extension helper
internal static class StringExt
{
    public static string Replace(this string s, string old, string @new, StringComparison sc)
    {
        if (string.IsNullOrEmpty(old)) return s;
        var sb = new System.Text.StringBuilder();
        int pos = 0;
        while (true)
        {
            int idx = s.IndexOf(old, pos, sc);
            if (idx < 0) { sb.Append(s, pos, s.Length - pos); break; }
            sb.Append(s, pos, idx - pos);
            sb.Append(@new);
            pos = idx + old.Length;
        }
        return sb.ToString();
    }
}
