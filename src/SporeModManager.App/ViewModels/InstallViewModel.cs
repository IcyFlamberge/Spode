using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SporeModManager.Core.Models;
using SporeModManager.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SporeModManager.App.ViewModels;

public partial class InstallViewModel : ObservableObject
{
    private readonly ModInstallationService _installer;
    private readonly InstalledModsStore _store = new();
    private readonly string _storePath;

    [ObservableProperty] private string status = "Ready";
    [ObservableProperty] private double progress;
    [ObservableProperty] private string packageTarget = "GalacticAdventures";

    public IReadOnlyList<string> PackageTargets { get; } = new[] { "GalacticAdventures", "Spore" };

    public InstallViewModel(ModInstallationService installer)
    {
        _installer = installer;
        _storePath = Path.Combine(installer.LauncherKitPath, InstalledModsStore.FileName);
        _store.Load(_storePath);
    }

    [RelayCommand]
    private async Task InstallFiles()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select mods to install",
            Filter = "Spore mods (*.package;*.sporemod)|*.package;*.sporemod|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        Status = "Installing...";
        Progress = 0;
        int done = 0;
        foreach (var path in dlg.FileNames)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".package")
                {
                    _installer.InstallPackage(path, name, _store, targetGame: PackageTarget);
                }
                else if (ext == ".sporemod")
                {
                    _installer.InstallSporeMod(path, name, _store, new Progress<double>(p => Progress = (done + p) / dlg.FileNames.Length));
                }
                else
                {
                    Status = $"Unsupported file: {path}";
                    continue;
                }
                done++;
                Progress = (double)done / dlg.FileNames.Length;
                Status = $"Installed {name}";
            }
            catch (Exception ex)
            {
                Status = $"Error installing {path}: {ex.Message}";
            }
        }
        _store.Save(_storePath);
        Status = "Installation complete";
    }
}