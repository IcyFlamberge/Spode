using System.Collections.Generic;
using System.IO;

namespace SporeModManager.Core.Models;

public enum GameVersionType
{
    Disc,
    Origin_March2017,
    EA_October2024,
    Steam_March2017,
    GOG_October2024,
    Steam_October2024,
    None
}

public static class GameVersion
{
    private static readonly Dictionary<long, GameVersionType> ExecutableSizes = new()
    {
        { 24909584, GameVersionType.Disc },
        { 24898224, GameVersionType.Origin_March2017 },
        { 24906040, GameVersionType.EA_October2024 },
        { 24885248, GameVersionType.Steam_March2017 },
        { 24895536, GameVersionType.GOG_October2024 },
        { 25066744, GameVersionType.Steam_October2024 }
    };

    public static bool RequiresModAPIFix(GameVersionType t) =>
        t == GameVersionType.Origin_March2017 || t == GameVersionType.EA_October2024;

    public static GameVersionType DetectVersion(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return GameVersionType.None;
        try
        {
            var len = new FileInfo(path).Length;
            return ExecutableSizes.TryGetValue(len, out var v) ? v : GameVersionType.None;
        }
        catch { return GameVersionType.None; }
    }

    public static string GetVersionName(GameVersionType t) =>
        t == GameVersionType.Disc ? "disk" : "steam_patched";

    public static string? GetExecutableFileName(GameVersionType t) => t switch
    {
        GameVersionType.Disc or GameVersionType.Steam_March2017 or GameVersionType.GOG_October2024 or GameVersionType.Steam_October2024 => "SporeApp.exe",
        GameVersionType.Origin_March2017 or GameVersionType.EA_October2024 => "SporeApp_ModAPIFix.exe",
        _ => null
    };

    public static string? GetNewDLLName(GameVersionType t) => t switch
    {
        GameVersionType.Disc => "SporeModAPI.disk.dll",
        GameVersionType.Origin_March2017 or GameVersionType.EA_October2024 or GameVersionType.Steam_March2017 or GameVersionType.GOG_October2024 or GameVersionType.Steam_October2024 => "SporeModAPI.march2017.dll",
        _ => null
    };

    public static string GetFriendlyVersionName(GameVersionType t) => t switch
    {
        GameVersionType.Disc => "Disc + Patch 5.1, July 2009",
        GameVersionType.Origin_March2017 => "Origin, March 2017",
        GameVersionType.EA_October2024 => "EA App, October 2024",
        GameVersionType.Steam_March2017 => "GOG/Steam, March 2017",
        GameVersionType.GOG_October2024 => "GOG, October 2024",
        GameVersionType.Steam_October2024 => "Steam, October 2024",
        _ => "Unknown"
    };

    public static bool IsLAACompatible(GameVersionType t) => t != GameVersionType.Steam_October2024;
}
