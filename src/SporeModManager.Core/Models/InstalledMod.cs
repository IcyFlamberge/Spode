using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace SporeModManager.Core.Models;

public class InstalledFile
{
    public string Name { get; set; } = string.Empty;
    public string Game { get; set; } = "GalacticAdventures"; // None / Spore / GalacticAdventures

    public InstalledFile() { }
    public InstalledFile(string name, string game = "GalacticAdventures")
    {
        Name = name; Game = game;
    }

    public void Save(XmlDocument doc, XmlElement parent)
    {
        var el = doc.CreateElement("file");
        el.InnerText = Name;
        if (!string.IsNullOrEmpty(Game) && Game != "None")
        {
            var a = doc.CreateAttribute("game");
            a.Value = Game;
            el.Attributes.Append(a);
        }
        parent.AppendChild(el);
    }

    public static InstalledFile Load(XmlNode node)
    {
        var f = new InstalledFile(node.InnerText, "None");
        var a = node.Attributes?.GetNamedItem("game");
        f.Game = a?.Value ?? "None";
        return f;
    }
}

public class InstalledMod
{
    public string Name { get; set; } = string.Empty;
    public string Unique { get; set; } = string.Empty;
    public Version? Version { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ModType { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string ConfiguratorPath { get; set; } = string.Empty;
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public Version[] DependenciesVersions { get; set; } = Array.Empty<Version>();
    public List<InstalledFile> InstalledFiles { get; set; } = new();

    public void AddFile(string name, string game = "GalacticAdventures") => InstalledFiles.Add(new InstalledFile(name, game));

    public InstalledMod() { }
    public InstalledMod(string name, string unique = "")
    {
        Name = name;
        Unique = string.IsNullOrEmpty(unique) ? name : unique;
    }

    public void Save(XmlDocument doc, XmlElement parent)
    {
        var el = doc.CreateElement("mod");
        el.SetAttribute("name", Name);
        el.SetAttribute("unique", Unique);
        if (Version != null) el.SetAttribute("version", Version.ToString());
        if (Dependencies.Length > 0) el.SetAttribute("dependencies", string.Join("?", Dependencies));
        if (DependenciesVersions.Length > 0) el.SetAttribute("dependenciesVersions", string.Join("?", DependenciesVersions.Select(v => v.ToString())));
        if (!string.IsNullOrEmpty(ConfiguratorPath)) el.SetAttribute("configurator", ConfiguratorPath);
        if (!string.IsNullOrEmpty(ManifestPath)) el.SetAttribute("manifest", ManifestPath);
        if (!string.IsNullOrEmpty(ModType)) el.SetAttribute("type", ModType);
        if (!string.IsNullOrEmpty(Author)) el.SetAttribute("author", Author);
        if (!string.IsNullOrEmpty(Description)) el.SetAttribute("description", Description);
        if (!string.IsNullOrEmpty(DisplayName) && DisplayName != Name) el.SetAttribute("displayName", DisplayName);
        foreach (var f in InstalledFiles) f.Save(doc, el);
        parent.AppendChild(el);
    }

    public static InstalledMod Load(XmlNode node)
    {
        var m = new InstalledMod();
        var na = node.Attributes?.GetNamedItem("name");
        m.Name = na?.Value ?? "";
        var ua = node.Attributes?.GetNamedItem("unique");
        m.Unique = ua?.Value ?? m.Name;
        var va = node.Attributes?.GetNamedItem("version");
        if (va != null && Version.TryParse(va.Value, out var pv)) m.Version = pv;
        var da = node.Attributes?.GetNamedItem("dependencies");
        if (da?.Value is { } deps) m.Dependencies = deps.Split('?');
        var dva = node.Attributes?.GetNamedItem("dependenciesVersions");
        if (dva?.Value is { } depVersions)
        {
            var list = new List<Version>();
            foreach (var s in depVersions.Split('?'))
                if (Version.TryParse(s, out var v)) list.Add(v);
                else { list.Clear(); break; }
            if (list.Count > 0) m.DependenciesVersions = list.ToArray();
        }
        var dna = node.Attributes?.GetNamedItem("displayName");
        m.DisplayName = dna?.Value ?? m.Name;
        var ca = node.Attributes?.GetNamedItem("configurator");
        if (ca?.Value is { } configurator) m.ConfiguratorPath = configurator;
        var ma = node.Attributes?.GetNamedItem("manifest");
        if (ma?.Value is { } manifest) m.ManifestPath = manifest;
        var ta = node.Attributes?.GetNamedItem("type");
        if (ta?.Value is { } type) m.ModType = type;
        var aa = node.Attributes?.GetNamedItem("author");
        if (aa?.Value is { } author) m.Author = author;
        var dea = node.Attributes?.GetNamedItem("description");
        if (dea?.Value is { } description) m.Description = description;
        foreach (XmlNode c in node.ChildNodes)
        {
            if (c.Name == "file") m.InstalledFiles.Add(InstalledFile.Load(c));
        }
        if (string.IsNullOrEmpty(m.DisplayName)) m.DisplayName = m.Name;
        return m;
    }
}

public class InstalledModsStore
{
    public static readonly string FileName = "InstalledMods.config";
    public HashSet<InstalledMod> ModConfigurations { get; set; } = new();

    public InstalledMod AddMod(string name, string unique = "")
    {
        var mod = new InstalledMod(name, unique);
        ModConfigurations.Remove(mod);
        ModConfigurations.Add(mod);
        return mod;
    }

    public void RemoveMod(InstalledMod mod) => ModConfigurations.Remove(mod);

    public void Save(string path)
    {
        var doc = new XmlDocument();
        var root = doc.CreateElement("InstalledMods");
        doc.AppendChild(root);
        foreach (var m in ModConfigurations) m.Save(doc, root);
        doc.Save(path);
    }

    public bool Load(string path)
    {
        ModConfigurations.Clear();
        if (!File.Exists(path)) return false;
        var xml = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(xml)) return true;
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        foreach (XmlNode n in doc.ChildNodes)
        {
            if (n.Name == "InstalledMods")
            {
                foreach (XmlNode c in n.ChildNodes)
                {
                    if (c.Name == "mod") ModConfigurations.Add(InstalledMod.Load(c));
                }
            }
        }
        return true;
    }
}