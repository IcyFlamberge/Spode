using System;

namespace SporeModManager.Core.Models;

/// <summary>
/// Metadata generated for a traditional .package mod so it can be shown and
/// managed like a .sporemod mod.
/// </summary>
public class PackageLibraryEntry
{
    public string PackagePath { get; set; } = string.Empty;
    public string ModInfoPath { get; set; } = string.Empty;
    public PackageModMetadata Metadata { get; set; } = new();
}

public class PackageModMetadata
{
    public string DisplayName { get; set; } = string.Empty;
    public string Unique { get; set; } = string.Empty;
    public Version Version { get; set; } = new(1, 0, 0);
    public string Author { get; set; } = "Unknown";
    public string Description { get; set; } = string.Empty;
    public string PackageFileName { get; set; } = string.Empty;
    public string TargetGame { get; set; } = "GalacticAdventures";
    public string ModInfoPath { get; set; } = string.Empty;
}
