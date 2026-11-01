using System;
using System.IO;
using System.Text.Json;

namespace SporeModManager.Core.Services;

public class AppSettings
{
    public string? SelectedModApiTag { get; set; }
    public string? LauncherKitPath { get; set; }
    public string? GameExecutablePath { get; set; }
    public string? GameVersionOverride { get; set; }
    public string? ActiveProfileId { get; set; }
}

public class SettingsService
{
    private readonly string _path;
    public SettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SporeModManager", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }
    public AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings(); }
        catch { return new AppSettings(); }
    }
    public void Save(AppSettings s)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }
}