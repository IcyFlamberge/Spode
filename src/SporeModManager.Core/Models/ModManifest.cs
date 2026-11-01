using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace SporeModManager.Core.Models;

public class ModComponent
{
    public string Unique { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> FileNames { get; set; } = new();
    public List<string> Games { get; set; } = new(); // empty/invalid -> launcher (None)
    public List<string> CompatTargetFileNames { get; set; } = new();
    public List<string> CompatTargetGames { get; set; } = new();
    public bool RemoveTargets { get; set; }
    public bool DefaultChecked { get; set; } = true;
    public string ImagePlacement { get; set; } = "None";
}

public class ModRemoval
{
    public List<string> FileNames { get; set; } = new();
    public List<string> Games { get; set; } = new();
}

public class ModManifest
{
    public string DisplayName { get; set; } = string.Empty;
    public string Unique { get; set; } = string.Empty;
    public Version? Version { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InstallerSystemVersion { get; set; } = string.Empty;
    public Version? DllsBuild { get; set; }
    public bool HasCustomInstaller { get; set; }
    public bool IsExperimental { get; set; }
    public bool RequiresGalaxyReset { get; set; }
    public bool CausesSaveDataDependency { get; set; }
    public string[] Depends { get; set; } = Array.Empty<string>();
    public Version[] DependsVersions { get; set; } = Array.Empty<Version>();
    public List<ModComponent> Prerequisites { get; set; } = new();
    public List<ModComponent> Components { get; set; } = new();
    public List<List<ModComponent>> ComponentGroups { get; set; } = new(); // groups of components
    public List<ModComponent> CompatFiles { get; set; } = new();
    public List<ModRemoval> Removals { get; set; } = new();

    public static ModManifest Load(Stream stream)
    {
        var doc = new XmlDocument();
        doc.Load(stream);
        var root = doc.SelectSingleNode("/mod");
        if (root == null) throw new InvalidDataException("ModInfo.xml: missing <mod> root");

        var m = new ModManifest();
        m.DisplayName = Attr(root, "displayName") ?? Attr(root, "name") ?? "";
        m.Unique = Attr(root, "unique") ?? m.DisplayName;
        var v = Attr(root, "version");
        if (v != null && Version.TryParse(v, out var pv)) m.Version = pv;
        m.Author = Attr(root, "author") ?? "";
        m.Description = Attr(root, "description") ?? "";
        m.InstallerSystemVersion = Attr(root, "installerSystemVersion") ?? "";
        var db = Attr(root, "dllsBuild");
        if (db != null && Version.TryParse(db, out var dv)) m.DllsBuild = dv;
        if (bool.TryParse(Attr(root, "hasCustomInstaller"), out var hci)) m.HasCustomInstaller = hci;
        if (bool.TryParse(Attr(root, "isExperimental"), out var ie)) m.IsExperimental = ie;
        if (bool.TryParse(Attr(root, "requiresGalaxyReset"), out var rgr)) m.RequiresGalaxyReset = rgr;
        if (bool.TryParse(Attr(root, "causesSaveDataDependency"), out var csd)) m.CausesSaveDataDependency = csd;
        var depends = Attr(root, "depends");
        if (!string.IsNullOrWhiteSpace(depends)) m.Depends = depends.Split('?');
        var dependsVersions = Attr(root, "dependsVersions");
        if (!string.IsNullOrWhiteSpace(dependsVersions))
        {
            var list = new List<Version>();
            foreach (var s in dependsVersions.Split('?'))
                if (Version.TryParse(s, out var v2)) list.Add(v2);
                else list.Add(null!);
            // keep only parsed? We'll store parsed where possible
            m.DependsVersions = list.Where(x => x != null).ToArray();
        }

        foreach (XmlNode node in root.ChildNodes)
        {
            var name = node.Name.ToLowerInvariant();
            if (name == "prerequisite")
            {
                m.Prerequisites.Add(ParseComponent(node));
            }
            else if (name == "remove")
            {
                var r = new ModRemoval();
                r.FileNames = SplitQ(node.InnerText);
                var g = Attr(node, "game");
                if (g != null) r.Games = SplitQ(g);
                else foreach (var _ in r.FileNames) r.Games.Add("");
                m.Removals.Add(r);
            }
            else if (name == "compatfile")
            {
                m.CompatFiles.Add(ParseComponent(node, compat: true));
            }
            else if (name == "component")
            {
                m.Components.Add(ParseComponent(node, hasUnique: true));
            }
            else if (name == "componentgroup")
            {
                var group = new List<ModComponent>();
                foreach (XmlNode sub in node.ChildNodes)
                {
                    if (sub.Name.ToLowerInvariant() == "component")
                        group.Add(ParseComponent(sub, hasUnique: true));
                }
                m.ComponentGroups.Add(group);
                // also flatten? keep separate
            }
        }

        if (string.IsNullOrEmpty(m.DisplayName)) m.DisplayName = m.Unique;
        if (string.IsNullOrEmpty(m.Unique)) m.Unique = m.DisplayName;
        return m;
    }

    private static ModComponent ParseComponent(XmlNode node, bool compat = false, bool hasUnique = false)
    {
        // hasUnique heuristic: component nodes have unique attr
        var c = new ModComponent();
        c.FileNames = SplitQ(node.InnerText);
        if (Attr(node, "unique") != null) c.Unique = Attr(node, "unique")!;
        if (Attr(node, "displayName") != null) c.DisplayName = Attr(node, "displayName")!;
        else if (!string.IsNullOrEmpty(c.Unique)) c.DisplayName = c.Unique;
        if (Attr(node, "description") != null) c.Description = Attr(node, "description")!;
        var game = Attr(node, "game");
        if (game != null) c.Games = SplitQ(game);
        else foreach (var _ in c.FileNames) c.Games.Add("");
        if (compat)
        {
            var ctf = Attr(node, "compatTargetFileName");
            if (ctf != null) c.CompatTargetFileNames = SplitQ(ctf);
            var ctg = Attr(node, "compatTargetGame");
            if (ctg != null) c.CompatTargetGames = SplitQ(ctg);
            var rt = Attr(node, "removeTargets");
            if (rt != null && bool.TryParse(rt, out var b)) c.RemoveTargets = b;
        }
        var dc = Attr(node, "defaultChecked");
        if (dc != null && bool.TryParse(dc, out var dcb)) c.DefaultChecked = dcb;
        var ip = Attr(node, "imagePlacement");
        if (ip != null) c.ImagePlacement = ip;
        if (string.IsNullOrEmpty(c.DisplayName)) c.DisplayName = c.Unique;
        return c;
    }

    private static List<string> SplitQ(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new List<string>();
        if (s.Contains("?")) return s.Split('?').ToList();
        return new List<string> { s };
    }

    private static string? Attr(XmlNode node, string name) =>
        node.Attributes?.GetNamedItem(name)?.Value;
}