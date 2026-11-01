using SporeModManager.Core.Models;
using SporeModManager.Core.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SporeModManager.Core.Services;

public class SporeLauncherService
{
    public string LauncherKitPath { get; }
    private readonly GamePathService _gamePaths;

    public SporeLauncherService(string launcherKitPath, GamePathService gamePaths)
    {
        LauncherKitPath = launcherKitPath;
        _gamePaths = gamePaths;
    }

    private const uint AccessRequired = 0x0002 | 0x0020 | 0x0008 | 0x0400 | 0x0010;
    private const uint MaxWait = 15000;
    private const int WaitTimeout = 0x102;
    private const string GalacticAdventuresSteamID = "24720";

    // SHA-256 of the bundled official ModAPI.DLLInjector.dll (Launcher Kit v1.5.13).
    // Used as a tamper check for the copy distributed with Spode.
    private const string BundledInjectorSha256 = "C992F52461EE043DC49AFA4277ED1D5155AAD91F786959B6DA8A480262EDE959";

    public void Launch(IProgress<string>? progress = null)
    {
        progress?.Report("Using the game path from Settings...");
        if (_gamePaths.ExecutablePath == null)
            throw new InvalidOperationException("Spore path is not set. Open Settings and select SporeApp.exe.");

        var sporebin = _gamePaths.SporebinPath
            ?? throw new DirectoryNotFoundException("Sporebin folder not found next to the selected executable.");

        var exeType = _gamePaths.DetectVersion();
        if (exeType == GameVersionType.None)
            throw new InvalidOperationException("Unsupported Spore executable. Select SporeApp.exe from a supported Spore build.");

        // LAA warning ignored

        var exePath = _gamePaths.ResolveLaunchExecutable();
        if (exePath == null || !File.Exists(exePath))
        {
            if (GameVersion.RequiresModAPIFix(exeType))
                throw new FileNotFoundException("ModAPIFix executable missing. Run the Origin/EA compatibility fix or install ModAPI Launcher Kit.");
            throw new FileNotFoundException("Spore executable not found in: " + sporebin);
        }
        if (GameVersion.RequiresModAPIFix(exeType) && !File.Exists(Path.Combine(sporebin, "steam_api.dll")))
            throw new FileNotFoundException("steam_api.dll missing for this Origin/EA install.");

        if (exeType is GameVersionType.Steam_March2017 or GameVersionType.Steam_October2024)
        {
            var steamAppId = Path.Combine(sporebin, "steam_appid.txt");
            try
            {
                if (!File.Exists(steamAppId) || File.ReadAllText(steamAppId).Trim() != GalacticAdventuresSteamID)
                    File.WriteAllText(steamAppId, GalacticAdventuresSteamID);
            }
            catch { /* warn only */ }
        }

        var dllEnding = GameVersion.GetVersionName(exeType);
        progress?.Report("Creating suspended Spore process...");
        CreateAndInject(sporebin, exePath, exeType, dllEnding, progress);
    }

    private string? FindInjectorPath()
    {
        var candidates = new[]
        {
            Path.Combine(LauncherKitPath, "ModAPI.DLLInjector.dll"),
            Path.Combine(LauncherKitPath, "dll", "ModAPI.DLLInjector.dll"),
        }.Concat(GetStandardKitPaths().Select(p => Path.Combine(p, "ModAPI.DLLInjector.dll"))).ToArray();
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    private static void VerifyBundledInjector(string injectorPath)
    {
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "ModAPI.DLLInjector.dll");
        var bundledDll = Path.Combine(AppContext.BaseDirectory, "dll", "ModAPI.DLLInjector.dll");
        var isBundled = string.Equals(Path.GetFullPath(injectorPath), Path.GetFullPath(bundledRoot), StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFullPath(injectorPath), Path.GetFullPath(bundledDll), StringComparison.OrdinalIgnoreCase);
        if (!isBundled)
            return;

        using var sha = SHA256.Create();
        using var stream = File.OpenRead(injectorPath);
        var hash = Convert.ToHexString(sha.ComputeHash(stream));
        if (!string.Equals(hash, BundledInjectorSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "ModAPI.DLLInjector.dll integrity check failed. The bundled injector was modified or replaced. Reinstall Spode from the official release.");
    }

    private static IEnumerable<string> GetStandardKitPaths()
    {
        var list = new List<string>();
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrEmpty(root)) continue;
            list.Add(Path.Combine(root, "Spore ModAPI Launcher Kit"));
            list.Add(Path.Combine(root, "SporeModAPI", "Launcher Kit"));
        }
        // also common user install
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local)) list.Add(Path.Combine(local, "Spore ModAPI Launcher Kit"));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData)) list.Add(Path.Combine(appData, "SporeModManager", "LauncherKit"));
        return list;
    }

    private void CreateAndInject(string sporebin, string exePath, GameVersionType exeType, string dllEnding, IProgress<string>? progress)
    {
        var startup = new NativeTypes.STARTUPINFO { cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeTypes.STARTUPINFO>() };
        if (!NativeMethods.CreateProcess(null, "\"" + exePath + "\"", IntPtr.Zero, IntPtr.Zero, false,
            NativeTypes.ProcessCreationFlags.CREATE_SUSPENDED, IntPtr.Zero, sporebin, ref startup, out var pi))
        {
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Failed to create Spore process");
        }

        try
        {
            progress?.Report("Locating ModAPI.DLLInjector...");
            var injectorPath = FindInjectorPath();
            if (injectorPath == null)
                throw new FileNotFoundException("ModAPI.DLLInjector.dll not found. Install Spore ModAPI Launcher Kit (https://launcherkit.sporecommunity.com/) or place the DLL next to this app.");

            VerifyBundledInjector(injectorPath);

            progress?.Report("Injecting ModAPI.DLLInjector...");
            var hInjector = InjectDLL(pi, injectorPath);
            if (hInjector == IntPtr.Zero)
                throw new InvalidOperationException("Failed to inject DLL injector");

            progress?.Report("Preparing DLL list...");
            var dlls = GetDLLsToInject(dllEnding);

            progress?.Report("Setting injection data...");
            SetInjectionData(pi, hInjector, dllEnding == "disk", dlls, injectorPath);

            progress?.Report("Resuming Spore...");
            if (NativeMethods.ResumeThread(pi.hThread) != 1)
                throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Failed to resume thread");
        }
        catch
        {
            NativeMethods.TerminateProcess(pi.hProcess, 0);
            throw;
        }
    }

    private IntPtr InjectDLL(NativeTypes.PROCESS_INFORMATION pi, string dllPath)
    {
        var retLib = NativeMethods.GetProcAddress(NativeMethods.GetModuleHandle("Kernel32.dll"), "LoadLibraryA");
        if (retLib == IntPtr.Zero) throw new InvalidOperationException("LoadLibrary unreachable");

        var hProc = NativeMethods.OpenProcess(AccessRequired, false, pi.dwProcessId);
        var objPtr = NativeMethods.VirtualAllocEx(hProc, IntPtr.Zero, (uint)dllPath.Length + 1, NativeTypes.AllocationType.Commit, NativeTypes.MemoryProtection.ReadWrite);
        if (objPtr == IntPtr.Zero) throw new System.ComponentModel.Win32Exception("Virtual alloc failure");

        var bytes = new byte[dllPath.Length + 1];
        for (int i = 0; i < dllPath.Length; i++) bytes[i] = (byte)dllPath[i];
        bytes[dllPath.Length] = 0;

        if (!NativeMethods.WriteProcessMemory(hProc, objPtr, bytes, (uint)bytes.Length, out var written) || written.ToUInt32() != bytes.Length)
            throw new System.ComponentModel.Win32Exception("Write process memory failed");

        var hThread = NativeMethods.CreateRemoteThread(hProc, IntPtr.Zero, 0, retLib, objPtr, 0, out _);
        if (hThread == IntPtr.Zero)
        {
            NativeMethods.VirtualFreeEx(hProc, objPtr, 0, NativeTypes.AllocationType.Release);
            NativeMethods.CloseHandle(hProc);
            throw new System.ComponentModel.Win32Exception("Create remote thread failed");
        }
        if (NativeMethods.WaitForSingleObject(hThread, MaxWait) == WaitTimeout)
            throw new System.ComponentModel.Win32Exception("Wait for single object failed");

        uint threadResult;
        while (NativeMethods.GetExitCodeThread(hThread, out threadResult))
        {
            if (threadResult != 0x103) break;
            Thread.Sleep(100);
        }

        NativeMethods.VirtualFreeEx(hProc, objPtr, 0, NativeTypes.AllocationType.Release);
        NativeMethods.CloseHandle(hProc);
        return new IntPtr((long)threadResult);
    }

    private void SetInjectionData(NativeTypes.PROCESS_INFORMATION pi, IntPtr hDLLInjectorHandle, bool isDiscSpore, string[] dlls, string injectorPath)
    {
        var hLocal = NativeMethods.LoadLibraryEx(injectorPath, IntPtr.Zero, NativeTypes.LoadLibraryFlags.DONT_RESOLVE_DLL_REFERENCES);
        var setPtr = NativeMethods.GetProcAddress(hLocal, "SetInjectionData");
        if (setPtr == IntPtr.Zero) throw new InvalidOperationException("Get proc address failure");
        setPtr = new IntPtr(hDLLInjectorHandle.ToInt64() + (setPtr.ToInt64() - hLocal.ToInt64()));
        NativeMethods.FreeLibrary(hLocal);

        var hProc = NativeMethods.OpenProcess(AccessRequired, false, pi.dwProcessId);

        int total = 1 + 4;
        foreach (var d in dlls) total += 4 + Encoding.Unicode.GetByteCount(d);

        var objPtr = NativeMethods.VirtualAllocEx(hProc, IntPtr.Zero, (uint)total, NativeTypes.AllocationType.Commit, NativeTypes.MemoryProtection.ReadWrite);
        if (objPtr == IntPtr.Zero) throw new System.ComponentModel.Win32Exception("Virtual alloc failure");

        var bytes = new byte[total];
        int off = 0;
        bytes[off++] = (byte)(isDiscSpore ? 1 : 0);
        foreach (var b in BitConverter.GetBytes((uint)dlls.Length)) bytes[off++] = b;
        foreach (var d in dlls)
        {
            foreach (var b in BitConverter.GetBytes((uint)d.Length)) bytes[off++] = b;
            var enc = Encoding.Unicode.GetBytes(d);
            foreach (var t in enc) bytes[off++] = t;
        }

        if (!NativeMethods.WriteProcessMemory(hProc, objPtr, bytes, (uint)bytes.Length, out var written) || written.ToUInt32() != bytes.Length)
            throw new System.ComponentModel.Win32Exception("Write process memory failed");

        var hThread = NativeMethods.CreateRemoteThread(hProc, IntPtr.Zero, 0, setPtr, objPtr, 0, out _);
        if (hThread == IntPtr.Zero)
        {
            NativeMethods.VirtualFreeEx(hProc, objPtr, 0, NativeTypes.AllocationType.Release);
            NativeMethods.CloseHandle(hProc);
            throw new System.ComponentModel.Win32Exception("Create remote thread failed");
        }
        if (NativeMethods.WaitForSingleObject(hThread, MaxWait) == WaitTimeout)
            throw new System.ComponentModel.Win32Exception("Wait for single object failed");

        uint tr;
        while (NativeMethods.GetExitCodeThread(hThread, out tr))
        {
            if (tr != 0x103) break;
            Thread.Sleep(100);
        }

        NativeMethods.VirtualFreeEx(hProc, objPtr, 0, NativeTypes.AllocationType.Release);
        NativeMethods.CloseHandle(hProc);
    }

    private string[] GetDLLsToInject(string dllEnding)
    {
        var dlls = new System.Collections.Generic.List<string>();
        var coreLibsPath = Path.Combine(LauncherKitPath, "coreLibs");
        var mLibsPath = Path.Combine(LauncherKitPath, "mLibs");
        Directory.CreateDirectory(mLibsPath);

        const string coreLibFile = "SporeModAPI.lib";
        var coreLibPath = Path.Combine(coreLibsPath, coreLibFile);
        var coreLegacyDllName = "SporeModAPI-" + dllEnding + ".dll";
        var coreLegacyDllPath = Path.Combine(LauncherKitPath, coreLegacyDllName);
        var coreDllPath = Path.Combine(coreLibsPath, GameVersion.GetNewDLLName(
            // detect type from dllEnding? use disk if disk
            dllEnding == "disk" ? GameVersionType.Disc : GameVersionType.Steam_March2017)!);
        var coreDllOutPath = Path.Combine(mLibsPath, "SporeModAPI.dll");

        foreach (var dll in new[] { coreLegacyDllPath, coreDllPath })
            if (!File.Exists(dll)) throw new FileNotFoundException("Required ModAPI DLL not found: " + dll);

        if (File.Exists(coreLibPath)) File.Copy(coreLibPath, Path.Combine(mLibsPath, coreLibFile), true);
        File.Copy(coreDllPath, coreDllOutPath, true);

        dlls.Add(coreDllOutPath);
        dlls.Add(coreLegacyDllPath);

        foreach (var file in Directory.EnumerateFiles(mLibsPath)
            .Where(x => { var l = x.ToLowerInvariant(); return l.EndsWith(".dll") && l != coreDllOutPath.ToLowerInvariant(); }))
            dlls.Add(file);

        foreach (var file in Directory.EnumerateFiles(LauncherKitPath, "*" + dllEnding + ".dll")
            .Where(x => x.ToLowerInvariant() != coreLegacyDllPath.ToLowerInvariant()))
            dlls.Add(file);

        return dlls.ToArray();
    }
}
