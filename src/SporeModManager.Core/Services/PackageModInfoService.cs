using SporeModManager.Core.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace SporeModManager.Core.Services;

/// <summary>
/// Creates and reads generated ModInfo.xml files for traditional .package mods.
/// The generated manifest uses the same root/component shape as a .sporemod,
/// so package mods can be listed, profiled and uninstalled like any other mod.
/// </summary>
public class PackageModInfoService
{
    public const string InstallerSystemVersion = "1.0.1.3";

    public PackageModMetadata Generate(string packagePath, string targetGame = "GalacticAdventures")
    {
        var fileName = Path.GetFileName(packagePath);
        var baseName = Path.GetFileNameWithoutExtension(packagePath);
        var displayName = string.IsNullOrWhiteSpace(baseName) ? "Package mod" : baseName;
        var version = ParseVersion(displayName) ?? new Version(1, 0, 0);

        return new PackageModMetadata
        {
            DisplayName = displayName,
            Unique = "pkg." + Slug(displayName),
            Version = version,
            Author = "Unknown",
            Description = $"Traditional .package mod installed into {targetGame}.",
            PackageFileName = fileName,
            TargetGame = string.IsNullOrWhiteSpace(targetGame) ? "GalacticAdventures" : targetGame
        };
    }

    public PackageModMetadata Read(string modInfoPath)
    {
        using var stream = File.OpenRead(modInfoPath);
        var manifest = ModManifest.Load(stream);
        var component = manifest.Components.FirstOrDefault();
        return new PackageModMetadata
        {
            DisplayName = string.IsNullOrWhiteSpace(manifest.DisplayName) ? Path.GetFileName(Path.GetDirectoryName(modInfoPath)) ?? "Package mod" : manifest.DisplayName,
            Unique = manifest.Unique,
            Version = manifest.Version ?? new Version(1, 0, 0),
            Author = string.IsNullOrWhiteSpace(manifest.Author) ? "Unknown" : manifest.Author,
            Description = manifest.Description,
            PackageFileName = component?.FileNames.FirstOrDefault() ?? "",
            TargetGame = component?.Games.FirstOrDefault() is { Length: > 0 } game ? game : "GalacticAdventures",
            ModInfoPath = modInfoPath
        };
    }

    /// <summary>
    /// Writes ModInfo.xml if it does not exist, otherwise reuses the existing
    /// metadata (so user edits are preserved).
    /// </summary>
    public PackageModMetadata EnsureModInfo(string folderPath, string packageFileName, string targetGame = "GalacticAdventures")
    {
        var modInfoPath = Path.Combine(folderPath, "ModInfo.xml");
        if (File.Exists(modInfoPath))
        {
            var existing = Read(modInfoPath);
            existing.PackageFileName = packageFileName;
            existing.ModInfoPath = modInfoPath;
            if (string.IsNullOrWhiteSpace(existing.TargetGame))
                existing.TargetGame = targetGame;

            if (!string.Equals(existing.TargetGame, targetGame, StringComparison.OrdinalIgnoreCase))
            {
                existing.TargetGame = targetGame;
                Write(modInfoPath, existing);
            }
            return existing;
        }

        var metadata = Generate(Path.Combine(folderPath, packageFileName), targetGame);
        metadata.PackageFileName = packageFileName;
        metadata.ModInfoPath = modInfoPath;
        Write(modInfoPath, metadata);
        return metadata;
    }

    private static void Write(string path, PackageModMetadata metadata)
    {
        var settings = new XmlWriterSettings { Indent = true, Encoding = System.Text.Encoding.UTF8 };
        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("mod");
        writer.WriteAttributeString("displayName", metadata.DisplayName);
        writer.WriteAttributeString("unique", metadata.Unique);
        writer.WriteAttributeString("version", metadata.Version.ToString());
        writer.WriteAttributeString("author", metadata.Author);
        writer.WriteAttributeString("description", metadata.Description);
        writer.WriteAttributeString("installerSystemVersion", InstallerSystemVersion);
        writer.WriteAttributeString("generatedBy", "SporeModManager");
        writer.WriteAttributeString("modType", "package");

        writer.WriteStartElement("component");
        writer.WriteAttributeString("unique", "main");
        writer.WriteAttributeString("displayName", metadata.DisplayName);
        writer.WriteAttributeString("game", metadata.TargetGame);
        writer.WriteString(metadata.PackageFileName);
        writer.WriteEndElement();

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static Version? ParseVersion(string text)
    {
        var dotted = Regex.Match(text, @"(?<!\d)(\d+(?:\.\d+){1,3})(?!\d)");
        if (dotted.Success && Version.TryParse(dotted.Groups[1].Value, out var dottedVersion))
            return dottedVersion;

        // Also accept versions written as v1_2_3 or v1-2-3.
        var separated = Regex.Match(text, @"(?:^|[._\-\s])v(\d+(?:[._\-]\d+){1,3})(?=$|[._\-\s])", RegexOptions.IgnoreCase);
        if (separated.Success)
        {
            var normalized = separated.Groups[1].Value.Replace('_', '.').Replace('-', '.');
            if (Version.TryParse(normalized, out var separatedVersion))
                return separatedVersion;
        }
        return null;
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        var slug = new string(chars).Trim('_');
        while (slug.Contains("__")) slug = slug.Replace("__", "_");
        return string.IsNullOrWhiteSpace(slug) ? "package_mod" : slug;
    }
}