using System;
using System.IO;
using System.Reflection;

namespace SporeModManager.Core.Services;

/// <summary>
/// Extracts the licence and third-party notices embedded in the single-file
/// executable into the user data folder. The release itself stays one EXE.
/// </summary>
public static class LegalNoticeService
{
    public static void EnsureExtracted()
    {
        var assembly = Assembly.GetEntryAssembly();
        if (assembly == null) return;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SporeModManager", "licenses");
        Directory.CreateDirectory(directory);

        Extract(assembly, "Spode.Legal.LICENSE", Path.Combine(directory, "LICENSE"));
        Extract(assembly, "Spode.Legal.THIRD-PARTY-NOTICES.md", Path.Combine(directory, "THIRD-PARTY-NOTICES.md"));
    }

    private static void Extract(Assembly assembly, string resourceName, string destination)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return;

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();

        if (File.Exists(destination) && new FileInfo(destination).Length == bytes.Length)
            return;

        File.WriteAllBytes(destination, bytes);
    }
}