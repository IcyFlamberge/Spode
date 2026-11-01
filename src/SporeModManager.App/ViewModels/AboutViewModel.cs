using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Reflection;

namespace SporeModManager.App.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    [ObservableProperty] private string appName = "Spode";
    [ObservableProperty] private string subtitle = "Spore Mod Manager";
    [ObservableProperty] private string version = GetVersion();
    [ObservableProperty] private string author = "Dalk12";
    [ObservableProperty] private string thanksTitle = "Special thanks";
    [ObservableProperty] private string thanks =
        "emd4600 — for the ModAPI SDK, the ModAPI Launcher Kit, and the enormous amount of work that made Spore modding what it is today.";

    private static string GetVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version;
        if (version == null) return "1.0.0";
        return version.Build >= 0 ? $"{version.Major}.{version.Minor}.{version.Build}" : $"{version.Major}.{version.Minor}";
    }
}