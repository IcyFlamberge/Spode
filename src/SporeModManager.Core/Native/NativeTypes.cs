using System;
using System.Runtime.InteropServices;

namespace SporeModManager.Core.Native;

public static class NativeTypes
{
    [Flags]
    public enum ProcessCreationFlags : uint
    {
        CREATE_SUSPENDED = 0x00000004
    }

    [Flags]
    public enum AllocationType : uint
    {
        Commit = 0x1000,
        Release = 0x8000
    }

    [Flags]
    public enum MemoryProtection : uint
    {
        ReadWrite = 0x04
    }

    [Flags]
    public enum LoadLibraryFlags : uint
    {
        DONT_RESOLVE_DLL_REFERENCES = 0x00000001
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFO
    {
        public uint cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }
}