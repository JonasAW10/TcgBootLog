using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TcgBootLog.Services;

public static class EfiNvram
{
    public const string EfiGlobalVariableGuid = "{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}";

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFirmwareEnvironmentVariableEx(
        string lpName, string lpGuid, byte[]? pBuffer, uint nSize, out uint pdwAttributes);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;

    public static void EnableSystemEnvironmentPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken");

        try
        {
            if (!LookupPrivilegeValue(null, "SeSystemEnvironmentPrivilege", out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "LookupPrivilegeValue");

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SePrivilegeEnabled,
            };
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AdjustTokenPrivileges");
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static byte[]? ReadVariable(string name, string guid = EfiGlobalVariableGuid)
    {
        EnableSystemEnvironmentPrivilege();
        uint attrs;
        uint size = GetFirmwareEnvironmentVariableEx(name, guid, null, 0, out attrs);
        if (size == 0)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 0) return null;
            // ERROR_INSUFFICIENT_BUFFER = 122 often when probing with null — retry with guess
        }

        var buf = new byte[4096];
        size = GetFirmwareEnvironmentVariableEx(name, guid, buf, (uint)buf.Length, out attrs);
        if (size == 0)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 0)
                throw new Win32Exception(err, $"GetFirmwareEnvironmentVariableEx({name})");
            return null;
        }

        var data = new byte[size];
        Array.Copy(buf, data, (int)size);
        return data;
    }

    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
