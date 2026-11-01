using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SporeModManager.Core.Models;
using SporeModManager.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SporeModManager.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly GamePathService _gamePaths;
    private bool _suppress;

    [ObservableProperty] private string launcherPath = AppContext.BaseDirectory;
    [ObservableProperty] private string modsLibraryPath = Path.Combine(AppContext.BaseDirectory, "Mods");
    [ObservableProperty] private string gameExecutable = "(not set)";
    [ObservableProperty] private string sporeBinPath = "(not set)";
    [ObservableProperty] private string gaDataPath = "(not set)";
    [ObservableProperty] private string sporeDataPath = "(not set)";
    [ObservableProperty] private string versionInfo = "Select SporeApp.exe to configure the game.";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private IReadOnlyList<string> versionOptions = new[]
    {
        "Auto",
        nameof(GameVersionType.Disc),
        nameof(GameVersionType.Origin_March2017),
        nameof(GameVersionType.EA_October2024),
        nameof(GameVersionType.Steam_March2017),
        nameof(GameVersionType.GOG_October2024),
        nameof(GameVersionType.Steam_October2024)
    };
    [ObservableProperty] private string selectedVersionOption = "Auto";

    public SettingsViewModel(GamePathService gamePaths)
    {
        _gamePaths = gamePaths;
        Refresh();
    }

    public void Refresh()
    {
        GameExecutable = _gamePaths.ExecutablePath ?? "(not set)";
        SporeBinPath = _gamePaths.SporebinPath ?? "(not set)";
        GaDataPath = _gamePaths.GalacticAdventuresDataPath ?? "(DataEP1 not found)";
        SporeDataPath = _gamePaths.SporeDataPath ?? "(Data not found)";
        _suppress = true;
        SelectedVersionOption = _gamePaths.GetVersionOverride();
        _suppress = false;
        var type = _gamePaths.DetectVersion();
        VersionInfo = _gamePaths.ExecutablePath == null
            ? "Select SporeApp.exe to configure the game."
            : $"{GameVersion.GetFriendlyVersionName(type)}" + (SelectedVersionOption == "Auto" ? " (auto)" : " (manual override)");
    }

    partial void OnSelectedVersionOptionChanged(string value)
    {
        if (_suppress) return;
        _gamePaths.SetVersionOverride(value);
        Refresh();
        Status = "Game build override saved";
    }

    [RelayCommand]
    private void Browse()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select SporeApp.exe (or SporeApp_ModAPIFix.exe)",
            Filter = "Spore executable (SporeApp*.exe)|SporeApp*.exe|Executables (*.exe)|*.exe",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _gamePaths.SetExecutablePath(dlg.FileName);
            Refresh();
            Status = "Game path saved";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
    }
}