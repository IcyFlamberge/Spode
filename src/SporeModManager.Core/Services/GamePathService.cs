using SporeModManager.Core.Models;
using System;
using System.IO;

namespace SporeModManager.Core.Services;

/// <summary>
/// Resolves all game paths from a user-selected Spore executable.
/// No registry probing and no automatic install discovery: the player is in control.
/// </summary>
public class GamePathService
{
    private readonly SettingsService _settings;

    public GamePathService(SettingsService settings) { _settings = settings; }

    public string? ExecutablePath
    {
        get
        {
            var p = _settings.Load().GameExecutablePath;
            return !string.IsNullOrWhiteSpace(p) && File.Exists(p) ? p : null;
        }
    }

    public string? SporebinPath => ExecutablePath is { } exe ? Path.GetDirectoryName(exe) : null;

    public string? GameRoot => SporebinPath is { } bin ? Directory.GetParent(bin)?.FullName : null;

    public string? GalacticAdventuresDataPath => ResolveDataPath("DataEP1");
    public string? SporeDataPath => ResolveDataPath("Data");

    private string? ResolveDataPath(string folderName)
    {
        if (GameRoot is not { } root) return null;
        var candidate = Path.Combine(root, folderName);
        return Directory.Exists(candidate) ? candidate : null;
    }

    public bool IsConfigured => ExecutablePath != null &&
        (GalacticAdventuresDataPath != null || SporeDataPath != null);

    public void SetExecutablePath(string path)
    {
        path = Path.GetFullPath(path);
        var s = _settings.Load();
        s.GameExecutablePath = path;
        _settings.Save(s);
    }

    public void SetVersionOverride(string? value)
    {
        var s = _settings.Load();
        s.GameVersionOverride = string.IsNullOrWhiteSpace(value) || value == "Auto" ? null : value;
        _settings.Save(s);
    }

    public string GetVersionOverride() => _settings.Load().GameVersionOverride ?? "Auto";

    public GameVersionType DetectVersion()
    {
        var exe = ExecutablePath;
        if (exe == null) return GameVersionType.None;

        var overrideValue = _settings.Load().GameVersionOverride;
        if (!string.IsNullOrWhiteSpace(overrideValue) && Enum.TryParse<GameVersionType>(overrideValue, out var forced) && forced != GameVersionType.None)
            return forced;

        var v = GameVersion.DetectVersion(exe);
        if (v != GameVersionType.None) return v;

        // If the user points at SporeApp_ModAPIFix.exe, use the base executable's size.
        var bin = SporebinPath;
        if (bin != null)
        {
            var baseExe = Path.Combine(bin, "SporeApp.exe");
            if (File.Exists(baseExe))
            {
                var bv = GameVersion.DetectVersion(baseExe);
                if (bv != GameVersionType.None) return bv;
            }
        }
        return GameVersionType.None;
    }

    public string? ResolveLaunchExecutable()
    {
        if (ExecutablePath == null || SporebinPath == null) return null;
        var type = DetectVersion();
        if (type == GameVersionType.None) return null;
        var preferred = Path.Combine(SporebinPath, GameVersion.GetExecutableFileName(type)!);
        return File.Exists(preferred) ? preferred : ExecutablePath;
    }

    public string Describe()
    {
        if (ExecutablePath == null) return "Game path not set";
        var type = DetectVersion();
        return $"{GameVersion.GetFriendlyVersionName(type)} — {ExecutablePath}";
    }
}