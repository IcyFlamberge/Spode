using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SporeModManager.Core.Models;
using SporeModManager.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SporeModManager.App.ViewModels;

public partial class ModApiViewModel : ObservableObject
{
    private readonly GitHubReleaseService _gh;
    private readonly ModApiService _api;
    private readonly SettingsService _settings;

    [ObservableProperty] private ObservableCollection<ModApiRelease> releases = new();
    [ObservableProperty] private ModApiRelease? selectedRelease;
    [ObservableProperty] private string status = "Loading releases...";
    [ObservableProperty] private double progress;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string installedTag = string.Empty;

    public ModApiViewModel(GitHubReleaseService gh, ModApiService api, SettingsService settings)
    {
        _gh = gh; _api = api; _settings = settings;
        InstalledTag = _api.GetInstalledTag() ?? "(none)";
        _ = LoadAsync();
    }

    private static string ReleasesCachePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SporeModManager", "releases-cache.json");

    [RelayCommand]
    private async Task LoadAsync()
    {
        Status = "Fetching releases from GitHub...";
        try
        {
            var list = await _gh.ListReleasesAsync("Spore-Community", "Spore-ModAPI", 30);
            Releases = new ObservableCollection<ModApiRelease>(list);
            SaveReleaseCache(list);
            RefreshLocalMarks();
            Status = $"Loaded {list.Count} releases";
            SelectPreferredRelease();
        }
        catch (Exception ex)
        {
            var cached = LoadReleaseCache();
            if (cached.Count > 0)
            {
                Releases = new ObservableCollection<ModApiRelease>(cached);
                RefreshLocalMarks();
                Status = $"GitHub is unavailable ({ex.Message}). Showing cached releases.";
                SelectPreferredRelease();
            }
            else
            {
                Status = "Error loading releases: " + ex.Message;
            }
        }
    }

    private void SelectPreferredRelease()
    {
        if (Releases.Count == 0) return;
        var s = _settings.Load();
        var tag = s.SelectedModApiTag ?? _api.GetInstalledTag();
        SelectedRelease = tag != null
            ? Releases.FirstOrDefault(r => r.TagName == tag) ?? Releases.FirstOrDefault()
            : Releases.FirstOrDefault();
    }

    /// <summary>
    /// Marks releases that already exist on this PC: downloaded into the local
    /// ModAPI cache and/or currently applied to coreLibs.
    /// </summary>
    public void RefreshLocalMarks()
    {
        var installed = _api.GetInstalledTag();
        foreach (var release in Releases)
        {
            release.IsDownloaded = _api.IsCached(release.TagName);
            release.IsInstalled = string.Equals(installed, release.TagName, StringComparison.OrdinalIgnoreCase);
        }

    }

    private static void SaveReleaseCache(List<ModApiRelease> releases)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReleasesCachePath)!);
            File.WriteAllText(ReleasesCachePath, JsonSerializer.Serialize(releases));
        }
        catch { /* cache is best-effort */ }
    }

    private static List<ModApiRelease> LoadReleaseCache()
    {
        try
        {
            if (!File.Exists(ReleasesCachePath)) return new List<ModApiRelease>();
            return JsonSerializer.Deserialize<List<ModApiRelease>>(File.ReadAllText(ReleasesCachePath)) ?? new List<ModApiRelease>();
        }
        catch { return new List<ModApiRelease>(); }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (SelectedRelease == null) { Status = "Select a release"; return; }
        try
        {
            Status = $"Downloading {SelectedRelease.TagName}... 0%";
            Progress = 0;
            ProgressText = "0%";
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                ProgressText = $"{p * 100:0}%";
                Status = $"Downloading {SelectedRelease.TagName}... {ProgressText}";
            });
            await _api.DownloadReleaseAsync(SelectedRelease, progress);
            Progress = 1;
            ProgressText = "100%";
            RefreshLocalMarks();
            Status = $"Downloaded and cached: {SelectedRelease.TagName} (100%)";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
    }

    [RelayCommand]
    private void InstallSelected()
    {
        if (SelectedRelease == null) { Status = "Select a release"; return; }
        if (!_api.IsCached(SelectedRelease.TagName)) { Status = "Download first"; return; }
        if (!_api.VerifyCacheIntegrity(SelectedRelease.TagName)) { Status = "Cache integrity check failed"; return; }
        try
        {
            Status = $"Installing {SelectedRelease.TagName} to launcher... 0%";
            Progress = 0;
            ProgressText = "0%";
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                ProgressText = $"{p * 100:0}%";
                Status = $"Installing {SelectedRelease.TagName}... {ProgressText}";
            });
            _api.InstallToLauncher(SelectedRelease.TagName, AppContext.BaseDirectory, progress);
            Progress = 1;
            ProgressText = "100%";
            var s = _settings.Load();
            s.SelectedModApiTag = SelectedRelease.TagName;
            _settings.Save(s);
            InstalledTag = SelectedRelease.TagName;
            RefreshLocalMarks();
            Status = $"Installed to launcher: {SelectedRelease.TagName} (100%)";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
    }
}