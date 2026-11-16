using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace WinUpdateManager.Core.Services;

internal static class WindowsSecurityHelper
{
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public uint HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    public static void EnablePrivileges()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
            {
                try
                {
                    EnableSinglePrivilege(hToken, "SeTakeOwnershipPrivilege");
                    EnableSinglePrivilege(hToken, "SeRestorePrivilege");
                    EnableSinglePrivilege(hToken, "SeSecurityPrivilege");
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
        }
        catch { }
    }

    private static void EnableSinglePrivilege(IntPtr hToken, string privilegeName)
    {
        try
        {
            if (LookupPrivilegeValue(null, privilegeName, out LUID luid))
            {
                TOKEN_PRIVILEGES tp = new()
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    }
                };
                AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch { }
    }

    public static bool UnlockServiceRegistryKey(string serviceName)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            EnablePrivileges();

            string subKeyPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
            var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            // 1. Take ownership using RegistryRights.TakeOwnership
            using (var key = Registry.LocalMachine.OpenSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.TakeOwnership))
            {
                if (key != null)
                {
                    var sec = new RegistrySecurity();
                    sec.SetOwner(adminSid);
                    key.SetAccessControl(sec);
                }
            }

            // 2. Open with ChangePermissions and grant Full Control to Administrators and SYSTEM
            using (var key = Registry.LocalMachine.OpenSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadKey | RegistryRights.WriteKey))
            {
                if (key != null)
                {
                    var sec = key.GetAccessControl();
                    var rules = sec.GetAccessRules(true, false, typeof(SecurityIdentifier));
                    foreach (RegistryAccessRule r in rules)
                    {
                        if (r.AccessControlType == AccessControlType.Deny)
                        {
                            sec.RemoveAccessRuleSpecific(r);
                        }
                    }

                    sec.SetAccessRule(new RegistryAccessRule(
                        adminSid,
                        RegistryRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));

                    sec.SetAccessRule(new RegistryAccessRule(
                        systemSid,
                        RegistryRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));

                    key.SetAccessControl(sec);

                    // 3. Remove "Security" binary value if WUB or custom SDDL created it
                    try
                    {
                        if (key.GetValue("Security") != null)
                        {
                            key.DeleteValue("Security", false);
                        }
                    }
                    catch { }
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? FindWubExecutable()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var searchPaths = new List<string>
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
                @"C:\Tools",
                @"C:\WUB"
            };

            foreach (var dir in searchPaths)
            {
                if (Directory.Exists(dir))
                {
                    string p64 = Path.Combine(dir, "Wub_x64.exe");
                    if (File.Exists(p64)) return p64;
                    string p = Path.Combine(dir, "Wub.exe");
                    if (File.Exists(p)) return p;
                }
            }
        }
        catch { }
        return null;
    }
}
