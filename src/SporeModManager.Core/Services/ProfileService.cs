using SporeModManager.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SporeModManager.Core.Services;

public class ProfileService
{
    private readonly SettingsService _settings;
    private readonly ModInstallationService _installer;
    private readonly ModApiService _modApi;
    private readonly GitHubReleaseService _gh;
    private readonly string _root;
    private readonly string _profilesPath;
    private readonly PackageModInfoService _packageInfo = new();

    public ProfileService(SettingsService settings, ModInstallationService installer, ModApiService modApi, GitHubReleaseService gh)
    {
        _settings = settings;
        _installer = installer;
        _modApi = modApi;
        _gh = gh;
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SporeModManager", "Profiles");
        _profilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SporeModManager", "profiles.json");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(_profilesPath)!);
    }

    public ProfilesDocument Load()
    {
        if (!File.Exists(_profilesPath)) return new ProfilesDocument();
        try
        {
            var doc = JsonSerializer.Deserialize<ProfilesDocument>(File.ReadAllText(_profilesPath)) ?? new ProfilesDocument();
            BackfillMetadata(doc);
            return doc;
        }
        catch { return new ProfilesDocument(); }
    }

    private void BackfillMetadata(ProfilesDocument doc)
    {
        foreach (var profile in doc.Profiles)
        {
            foreach (var mod in profile.Mods)
            {
                if (string.IsNullOrWhiteSpace(mod.DisplayName))
                    mod.DisplayName = mod.Name;
                if (!string.IsNullOrWhiteSpace(mod.DisplayName) && !string.IsNullOrWhiteSpace(mod.Version))
                    continue;
                if (!File.Exists(mod.SourcePath)) continue;

                try
                {
                    if (mod.Type.Equals("package", StringComparison.OrdinalIgnoreCase))
                    {
                        var meta = _packageInfo.Generate(mod.SourcePath);
                        if (string.IsNullOrWhiteSpace(mod.DisplayName)) mod.DisplayName = meta.DisplayName;
                        if (string.IsNullOrWhiteSpace(mod.Unique)) mod.Unique = meta.Unique;
                        if (string.IsNullOrWhiteSpace(mod.Version)) mod.Version = meta.Version.ToString();
                        if (string.IsNullOrWhiteSpace(mod.Author)) mod.Author = meta.Author;
                        if (string.IsNullOrWhiteSpace(mod.Description)) mod.Description = meta.Description;
                        if (string.IsNullOrWhiteSpace(mod.TargetGame)) mod.TargetGame = meta.TargetGame;
                    }
                    else
                    {
                        var manifest = TryReadManifest(mod.SourcePath);
                        if (manifest != null)
                        {
                            if (string.IsNullOrWhiteSpace(mod.DisplayName)) mod.DisplayName = manifest.DisplayName;
                            if (string.IsNullOrWhiteSpace(mod.Unique)) mod.Unique = manifest.Unique;
                            if (string.IsNullOrWhiteSpace(mod.Version)) mod.Version = manifest.Version?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(mod.Author)) mod.Author = string.IsNullOrWhiteSpace(manifest.Author) ? "Unknown" : manifest.Author;
                            if (string.IsNullOrWhiteSpace(mod.Description)) mod.Description = manifest.Description;
                        }
                    }
                }
                catch { }
            }
        }
    }

    public void Save(ProfilesDocument doc)
    {
        File.WriteAllText(_profilesPath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        var s = _settings.Load();
        s.ActiveProfileId = doc.ActiveProfileId;
        _settings.Save(s);
    }

    public ModProfile CreateProfile(string name)
    {
        var doc = Load();
        var settings = _settings.Load();
        var p = new ModProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New profile" : name.Trim(),
            ModApiTag = settings.SelectedModApiTag ?? _modApi.GetInstalledTag()
        };
        doc.Profiles.Add(p);
        Save(doc);
        return p;
    }

    public void DeleteProfile(string profileId)
    {
        var doc = Load();
        doc.Profiles.RemoveAll(p => p.Id == profileId);
        if (doc.ActiveProfileId == profileId) doc.ActiveProfileId = null;
        Save(doc);
        var dir = Path.Combine(_root, profileId);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    public string GetProfileModsDirectory(string profileId)
    {
        var dir = Path.Combine(_root, profileId, "mods");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public ProfileMod AddMod(string profileId, string sourcePath, string targetGame = "GalacticAdventures")
    {
        var doc = Load();
        var profile = doc.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new InvalidOperationException("Profile not found");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException(sourcePath);

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext != ".sporemod" && ext != ".package")
            throw new InvalidOperationException("Only .sporemod and .package files are supported");

        var name = Path.GetFileNameWithoutExtension(sourcePath);
        string dest;
        string displayName = name;
        string unique = name;
        string version = "";
        string author = "Unknown";
        string description = "";
        string resolvedTarget = string.IsNullOrWhiteSpace(targetGame) ? "GalacticAdventures" : targetGame;

        if (ext == ".package")
        {
            // Package mods live in the central Mods library with generated ModInfo.xml.
            var entry = _installer.EnsurePackageInLibrary(sourcePath, resolvedTarget);
            dest = entry.PackagePath;
            var meta = entry.Metadata;
            displayName = meta.DisplayName;
            unique = meta.Unique;
            version = meta.Version.ToString();
            author = meta.Author;
            description = meta.Description;
            resolvedTarget = meta.TargetGame;
        }
        else
        {
            // .sporemod files stay isolated per profile.
            var destDir = GetProfileModsDirectory(profileId);
            dest = Path.Combine(destDir, Path.GetFileName(sourcePath));
            var sourceInfo = new FileInfo(sourcePath);
            var destInfo = new FileInfo(dest);
            if (!destInfo.Exists || destInfo.Length != sourceInfo.Length || destInfo.LastWriteTimeUtc != sourceInfo.LastWriteTimeUtc)
            {
                File.Copy(sourcePath, dest, true);
                File.SetLastWriteTimeUtc(dest, sourceInfo.LastWriteTimeUtc);
            }

            var manifest = TryReadManifest(dest);
            if (manifest != null)
            {
                displayName = manifest.DisplayName;
                unique = manifest.Unique;
                version = manifest.Version?.ToString() ?? "";
                author = string.IsNullOrWhiteSpace(manifest.Author) ? "Unknown" : manifest.Author;
                description = manifest.Description;
            }
        }

        var existing = profile.Mods.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.SourcePath = dest;
            existing.Type = ext.TrimStart('.');
            existing.DisplayName = displayName;
            existing.Unique = unique;
            existing.Version = version;
            existing.Author = author;
            existing.Description = description;
            existing.TargetGame = resolvedTarget;
            existing.Enabled = true;
        }
        else
        {
            existing = new ProfileMod
            {
                Name = name,
                SourcePath = dest,
                Type = ext.TrimStart('.'),
                DisplayName = displayName,
                Unique = unique,
                Version = version,
                Author = author,
                Description = description,
                TargetGame = resolvedTarget,
                Enabled = true
            };
            profile.Mods.Add(existing);
        }
        Save(doc);
        return existing;
    }

    private static ModManifest? TryReadManifest(string sporemodPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(sporemodPath);
            var entry = zip.GetEntry("ModInfo.xml");
            if (entry == null) return null;
            using var stream = entry.Open();
            return ModManifest.Load(stream);
        }
        catch
        {
            return null;
        }
    }

    public void RemoveMod(string profileId, string modId)
    {
        var doc = Load();
        var profile = doc.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;
        var mod = profile.Mods.FirstOrDefault(m => m.Id == modId);
        if (mod == null) return;
        profile.Mods.Remove(mod);

        // Only delete copies owned by this profile; package files in the
        // central Mods library must survive removal from a profile.
        var profileModsDir = GetProfileModsDirectory(profileId);
        if (mod.SourcePath.StartsWith(profileModsDir, StringComparison.OrdinalIgnoreCase))
        {
            try { if (File.Exists(mod.SourcePath)) File.Delete(mod.SourcePath); } catch { }
        }
        Save(doc);
    }

    public void ToggleMod(string profileId, string modId, bool enabled)
    {
        var doc = Load();
        var profile = doc.Profiles.FirstOrDefault(p => p.Id == profileId);
        var mod = profile?.Mods.FirstOrDefault(m => m.Id == modId);
        if (mod == null) return;
        mod.Enabled = enabled;
        Save(doc);
    }

    public void UpdateProfile(string profileId, Action<ModProfile> update)
    {
        var doc = Load();
        var profile = doc.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;
        update(profile);
        Save(doc);
    }

    public async Task EnsureModApiAsync(string tag, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!_modApi.IsCached(tag))
        {
            progress?.Report($"Downloading ModAPI {tag}... 0%");
            var releases = await _gh.ListReleasesAsync("Spore-Community", "Spore-ModAPI", 50, ct);
            var release = releases.FirstOrDefault(r => string.Equals(r.TagName, tag, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"ModAPI release {tag} not found on GitHub");
            var downloadProgress = new Progress<double>(p =>
                progress?.Report($"Downloading ModAPI {tag}... {p * 100:0}%"));
            await _modApi.DownloadReleaseAsync(release, downloadProgress, ct);
        }
        if (!_modApi.VerifyCacheIntegrity(tag))
            throw new InvalidOperationException($"ModAPI cache for {tag} is corrupted");

        if (string.Equals(_modApi.GetInstalledTag(), tag, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report($"ModAPI {tag} is already applied");
            return;
        }

        progress?.Report($"Applying ModAPI {tag}...");
        _modApi.InstallToLauncher(tag, _installer.LauncherKitPath, null);
    }

    public async Task ApplyActiveProfileModApiAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var doc = Load();
        var active = doc.Profiles.FirstOrDefault(p => p.Id == doc.ActiveProfileId);
        if (active?.ModApiTag is { Length: > 0 } tag)
            await EnsureModApiAsync(tag, progress, ct);
    }

    public async Task ActivateAsync(ModProfile profile, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report($"Activating profile: {profile.Name}");

        // 1) ModAPI version for this profile.
        if (!string.IsNullOrWhiteSpace(profile.ModApiTag))
            await EnsureModApiAsync(profile.ModApiTag!, progress, ct);

        if (profile.Mods.Any(m => m.Enabled) && !_installer.IsGamePathConfigured)
            throw new InvalidOperationException("Spore path is not set. Open Settings and select SporeApp.exe before activating a profile.");

        // 2) Read the current installed-mod registry.
        var doc = Load();
        var previous = doc.ActiveProfileId == null ? null : doc.Profiles.FirstOrDefault(p => p.Id == doc.ActiveProfileId);
        var storePath = Path.Combine(_installer.LauncherKitPath, InstalledModsStore.FileName);
        var store = new InstalledModsStore();
        store.Load(storePath);

        // Already active and everything is in place: do not reinstall anything.
        if (doc.ActiveProfileId == profile.Id && IsProfileApplied(profile, store))
        {
            progress?.Report("Profile is already active");
            return;
        }

        // 3) Remove only mods that must disappear:
        //    - mods from the previous profile that are not in the new profile;
        //    - disabled mods of the target profile;
        //    - target mods whose files are missing/broken.
        var targetNames = profile.Mods.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toRemove = new List<InstalledMod>();

        if (previous != null)
        {
            foreach (var pm in previous.Mods)
            {
                if (targetNames.Contains(pm.Name)) continue;
                toRemove.AddRange(store.ModConfigurations.Where(m =>
                    string.Equals(m.Name, pm.Name, StringComparison.OrdinalIgnoreCase)));
            }
        }

        foreach (var pm in profile.Mods)
        {
            var installed = store.ModConfigurations.FirstOrDefault(m =>
                string.Equals(m.Name, pm.Name, StringComparison.OrdinalIgnoreCase));
            if (installed == null) continue;
            if (!pm.Enabled || !_installer.AreInstalledFilesPresent(installed))
                toRemove.Add(installed);
        }

        foreach (var m in toRemove.DistinctBy(m => m.Name).ToList())
        {
            progress?.Report($"Removing mod: {m.DisplayName}");
            try { _installer.RemoveMod(m, store); } catch { }
        }

        // 4) Install only enabled mods that are not already installed.
        foreach (var pm in profile.Mods.Where(m => m.Enabled))
        {
            var installed = store.ModConfigurations.FirstOrDefault(m =>
                string.Equals(m.Name, pm.Name, StringComparison.OrdinalIgnoreCase));
            if (installed != null && _installer.AreInstalledFilesPresent(installed))
            {
                progress?.Report($"Already installed: {pm.Name}");
                continue;
            }

            if (!File.Exists(pm.SourcePath))
            {
                progress?.Report($"Skipping missing file: {pm.SourcePath}");
                continue;
            }

            progress?.Report($"Installing {pm.Name}...");
            if (pm.Type.Equals("package", StringComparison.OrdinalIgnoreCase))
            {
                var metadata = new PackageModMetadata
                {
                    DisplayName = string.IsNullOrWhiteSpace(pm.DisplayName) ? pm.Name : pm.DisplayName,
                    Unique = string.IsNullOrWhiteSpace(pm.Unique) ? pm.Name : pm.Unique,
                    Version = Version.TryParse(pm.Version, out var version) ? version : new Version(1, 0, 0),
                    Author = string.IsNullOrWhiteSpace(pm.Author) ? "Unknown" : pm.Author,
                    Description = pm.Description,
                    PackageFileName = Path.GetFileName(pm.SourcePath),
                    TargetGame = string.IsNullOrWhiteSpace(pm.TargetGame) ? "GalacticAdventures" : pm.TargetGame
                };
                _installer.InstallPackage(pm.SourcePath, pm.Name, store, copyToLibrary: false, targetGame: metadata.TargetGame, metadata: metadata);
            }
            else
            {
                var modProgress = new Progress<double>(p =>
                    progress?.Report($"Installing {pm.Name}... {p * 100:0}%"));
                _installer.InstallSporeMod(pm.SourcePath, pm.Name, store, modProgress, copyToLibrary: false);
            }
        }

        store.Save(storePath);
        doc = Load();
        doc.ActiveProfileId = profile.Id;
        Save(doc);
        progress?.Report("Profile activated");
    }

    private bool IsProfileApplied(ModProfile profile, InstalledModsStore store)
    {
        foreach (var pm in profile.Mods)
        {
            var installed = store.ModConfigurations.FirstOrDefault(m =>
                string.Equals(m.Name, pm.Name, StringComparison.OrdinalIgnoreCase));
            if (pm.Enabled)
            {
                if (installed == null || !_installer.AreInstalledFilesPresent(installed))
                    return false;
            }
            else if (installed != null)
            {
                return false;
            }
        }
        return true;
    }
}
