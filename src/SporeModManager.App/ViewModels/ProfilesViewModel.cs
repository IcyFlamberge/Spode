using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SporeModManager.Core.Models;
using SporeModManager.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SporeModManager.App.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly ProfileService _profiles;
    private readonly ModApiService _modApi;

    [ObservableProperty] private ObservableCollection<ModProfile> profiles = new();
    [ObservableProperty] private ModProfile? selectedProfile;
    [ObservableProperty] private ObservableCollection<ProfileMod> profileMods = new();
    [ObservableProperty] private ObservableCollection<string> modApiTags = new();
    [ObservableProperty] private string? selectedModApiTag;
    [ObservableProperty] private string profileName = "";
    [ObservableProperty] private string newProfileName = "";
    [ObservableProperty] private string status = "Create a profile to start.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string activeProfileName = "(none)";
    [ObservableProperty] private string packageTarget = "GalacticAdventures";

    public IReadOnlyList<string> PackageTargets { get; } = new[] { "GalacticAdventures", "Spore" };

    private bool _suppress;

    public ProfilesViewModel(ProfileService profiles, ModApiService modApi)
    {
        _profiles = profiles;
        _modApi = modApi;
        ApplyTags(CollectLocalTags());
        Reload();
    }

    private void Reload()
    {
        var doc = _profiles.Load();
        Profiles = new ObservableCollection<ModProfile>(doc.Profiles);
        var active = doc.Profiles.FirstOrDefault(p => p.Id == doc.ActiveProfileId);
        ActiveProfileName = active?.Name ?? "(none)";
        SelectedProfile = null;
        ProfileMods = new ObservableCollection<ProfileMod>();
        ProfileName = "";
        SelectedModApiTag = null;
        Status = doc.Profiles.Count == 0 ? "Create a profile to start." : "Select a profile.";
    }

    /// <summary>
    /// Profile ModAPI versions come only from local versions downloaded via the
    /// ModAPI tab. No GitHub request is made here.
    /// </summary>
    public void RefreshLocalTags()
    {
        ApplyTags(CollectLocalTags());
        if (ModApiTags.Count == 0)
            Status = "No local ModAPI versions. Download one on the ModAPI tab first.";
    }

    private void ApplyTags(IEnumerable<string> tags)
    {
        var list = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ParseTagVersion)
            .ThenByDescending(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var current = SelectedProfile?.ModApiTag;
        ModApiTags = new ObservableCollection<string>(list);

        if (!string.IsNullOrWhiteSpace(current) && list.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            _suppress = true;
            SelectedModApiTag = current;
            _suppress = false;
        }
    }

    private static Version ParseTagVersion(string tag)
    {
        var raw = tag.TrimStart('v', 'V');
        return Version.TryParse(raw, out var v) ? v : new Version(0, 0);
    }

    private IEnumerable<string> CollectLocalTags()
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var modApiRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SporeModManager", "ModApi");
            if (Directory.Exists(modApiRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(modApiRoot))
                {
                    var hasDownload = File.Exists(Path.Combine(dir, "SporeModAPIdlls.zip"));
                    var hasExtracted = Directory.Exists(Path.Combine(dir, "extracted"));
                    if (hasDownload || hasExtracted)
                        tags.Add(Path.GetFileName(dir));
                }
            }
        }
        catch { }

        // The version currently applied to coreLibs is always a valid local choice.
        var installed = _modApi.GetInstalledTag();
        if (!string.IsNullOrWhiteSpace(installed)) tags.Add(installed);

        return tags;
    }

    partial void OnSelectedProfileChanged(ModProfile? value)
    {
        if (_suppress) return;
        if (value == null)
        {
            ProfileMods = new ObservableCollection<ProfileMod>();
            ProfileName = "";
            SelectedModApiTag = null;
            return;
        }
        _suppress = true;
        ProfileName = value.Name;
        SelectedModApiTag = value.ModApiTag;
        ProfileMods = new ObservableCollection<ProfileMod>(value.Mods);
        _suppress = false;
        Status = $"Profile: {value.Name}";
    }

    partial void OnSelectedModApiTagChanged(string? value)
    {
        if (_suppress || SelectedProfile == null) return;
        // Ignore transient nulls produced by the ComboBox before its items are loaded.
        if (value == null && !string.IsNullOrEmpty(SelectedProfile.ModApiTag)) return;
        SelectedProfile.ModApiTag = value;
        SaveCurrent();
    }

    partial void OnProfileNameChanged(string value)
    {
        if (_suppress || SelectedProfile == null) return;
        SelectedProfile.Name = value;
        SaveCurrent();
    }

    private void SaveCurrent()
    {
        if (SelectedProfile == null) return;
        _profiles.UpdateProfile(SelectedProfile.Id, p =>
        {
            p.Name = SelectedProfile.Name;
            p.ModApiTag = SelectedProfile.ModApiTag;
            p.Mods = SelectedProfile.Mods;
        });
    }

    [RelayCommand]
    private void CreateProfile()
    {
        try
        {
            var p = _profiles.CreateProfile(string.IsNullOrWhiteSpace(NewProfileName) ? "New profile" : NewProfileName);
            NewProfileName = "";
            Reload();
            _suppress = true;
            SelectedProfile = Profiles.FirstOrDefault(x => x.Id == p.Id);
            _suppress = false;
            OnSelectedProfileChanged(SelectedProfile);
            Status = $"Created profile: {p.Name}";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null) return;
        try
        {
            _profiles.DeleteProfile(SelectedProfile.Id);
            Reload();
            Status = "Profile deleted";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
    }

    [RelayCommand]
    private void AddMod()
    {
        if (SelectedProfile == null) { Status = "Select a profile first"; return; }
        var dlg = new OpenFileDialog
        {
            Title = "Add mod to profile",
            Filter = "Spore mods (*.package;*.sporemod)|*.package;*.sporemod",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var path in dlg.FileNames)
        {
            try { _profiles.AddMod(SelectedProfile.Id, path, PackageTarget); }
            catch (Exception ex) { Status = "Error: " + ex.Message; return; }
        }
        RefreshProfile();
        Status = "Mod(s) added to profile";
    }

    [RelayCommand]
    private void RemoveMod(ProfileMod? mod)
    {
        if (SelectedProfile == null || mod == null) return;
        _profiles.RemoveMod(SelectedProfile.Id, mod.Id);
        RefreshProfile();
        Status = $"Removed {mod.Name}";
    }

    private void RefreshProfile()
    {
        if (SelectedProfile == null) return;
        var doc = _profiles.Load();
        var p = doc.Profiles.FirstOrDefault(x => x.Id == SelectedProfile.Id);
        if (p == null) { Reload(); return; }
        _suppress = true;
        SelectedProfile = p;
        _suppress = false;
        OnSelectedProfileChanged(p);
    }

    [RelayCommand]
    private void SaveProfile()
    {
        SaveCurrent();
        Status = "Profile saved";
    }

    [RelayCommand]
    private async Task Activate()
    {
        if (SelectedProfile == null) { Status = "Select a profile"; return; }
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            SaveCurrent();
            Status = "Activating profile...";
            await _profiles.ActivateAsync(SelectedProfile, new Progress<string>(s => Status = s));
            var doc = _profiles.Load();
            ActiveProfileName = doc.Profiles.FirstOrDefault(p => p.Id == doc.ActiveProfileId)?.Name ?? "(none)";
            Status = "Profile activated";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Refresh() => Reload();
}