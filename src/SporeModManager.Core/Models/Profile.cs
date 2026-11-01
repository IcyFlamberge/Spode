using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SporeModManager.Core.Models;

public class ProfileMod
{
    [JsonIgnore]
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Unique { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty; // stored copy inside the profile folder
    public string Type { get; set; } = "sporemod"; // "sporemod" | "package"
    public string TargetGame { get; set; } = "GalacticAdventures";
    public bool Enabled { get; set; } = true;
}

public class ModProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    public string? ModApiTag { get; set; }
    public List<ProfileMod> Mods { get; set; } = new();
}

public class ProfilesDocument
{
    public List<ModProfile> Profiles { get; set; } = new();
    public string? ActiveProfileId { get; set; }
}