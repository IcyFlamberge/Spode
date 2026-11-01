using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SporeModManager.Core.Models;
using SporeModManager.Core.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SporeModManager.App.ViewModels;

public partial class ModsViewModel : ObservableObject
{
    private readonly ModInstallationService _installer;
    private readonly InstalledModsStore _store = new();
    private readonly string _storePath;

    [ObservableProperty] private ObservableCollection<InstalledMod> mods = new();
    [ObservableProperty] private InstalledMod? selectedMod;
    [ObservableProperty] private string status = "Ready";

    public ModsViewModel(ModInstallationService installer)
    {
        _installer = installer;
        _storePath = Path.Combine(installer.LauncherKitPath, InstalledModsStore.FileName);
        Reload();
    }

    public void Reload()
    {
        _store.Load(_storePath);
        foreach (var mod in _store.ModConfigurations)
        {
            if (string.IsNullOrWhiteSpace(mod.DisplayName)) mod.DisplayName = mod.Name;
            if (string.IsNullOrWhiteSpace(mod.ModType))
            {
                // Older registry entries have no type. A .sporemod has a
                // ConfiguratorPath/ModInfo.xml; a plain package has exactly one
                // installed .package file and no configurator.
                var hasManifest = !string.IsNullOrWhiteSpace(mod.ConfiguratorPath);
                var singlePackage = mod.InstalledFiles.Count == 1 &&
                    mod.InstalledFiles[0].Name.EndsWith(".package", StringComparison.OrdinalIgnoreCase);
                mod.ModType = hasManifest || !singlePackage ? "sporemod" : "package";
            }

            var manifestPath = !string.IsNullOrWhiteSpace(mod.ManifestPath)
                ? mod.ManifestPath
                : mod.ConfiguratorPath;
            if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
            {
                try
                {
                    if (mod.ModType == "sporemod")
                    {
                        using var stream = File.OpenRead(manifestPath);
                        var manifest = ModManifest.Load(stream);
                        if (string.IsNullOrWhiteSpace(mod.Author))
                            mod.Author = string.IsNullOrWhiteSpace(manifest.Author) ? "Unknown" : manifest.Author;
                        if (string.IsNullOrWhiteSpace(mod.Description))
                            mod.Description = manifest.Description;
                        if (mod.Version == null) mod.Version = manifest.Version;
                    }
                    else
                    {
                        var meta = new PackageModInfoService().Read(manifestPath);
                        if (string.IsNullOrWhiteSpace(mod.Author)) mod.Author = meta.Author;
                        if (string.IsNullOrWhiteSpace(mod.Description)) mod.Description = meta.Description;
                        if (mod.Version == null) mod.Version = meta.Version;
                    }
                }
                catch { }
            }
        }
        Mods = new ObservableCollection<InstalledMod>(_store.ModConfigurations.OrderBy(m => m.DisplayName));
    }

    [RelayCommand]
    private void UninstallSelected()
    {
        if (SelectedMod == null) { Status = "Select a mod first"; return; }
        try
        {
            _installer.RemoveMod(SelectedMod, _store);
            Status = $"Uninstalled {SelectedMod.DisplayName}";
            Reload();
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
    }

    [RelayCommand]
    private void Refresh() => Reload();
}