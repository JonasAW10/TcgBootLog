using System.Management;
using Microsoft.Win32;

namespace TcgBootLog.Services;

public enum SecurityFeatureKind
{
    Hypervisor,
    Vbs,
    Hvci,
    SecureBoot,
    DriverSignature,
    CodeIntegrity,
    VulnerableDrivers,
}

public sealed class SecurityFeatureStatus
{
    public SecurityFeatureKind Kind { get; init; }
    public string Name { get; init; } = "";
    public bool Ok { get; init; }
    public string Summary { get; init; } = "";
    public string Detail { get; init; } = "";
}

public static class WindowsSecurityChecker
{
    public static List<SecurityFeatureStatus> CheckAll()
    {
        var dg = TryReadDeviceGuard();
        var list = new List<SecurityFeatureStatus>
        {
            CheckHypervisor(dg),
            CheckVbs(dg),
            CheckHvci(dg),
            CheckSecureBoot(),
            CheckDriverSignature(),
            CheckCodeIntegrity(dg),
            CheckVulnerableDriverBlocklist(),
        };
        return list;
    }

    private sealed class DeviceGuardInfo
    {
        public uint? VbsStatus;                 // 0 Off, 1 Enabled, 2 Running
        public uint[]? SecurityServicesRunning; // 1=CredGuard, 2=HVCI
        public uint[]? SecurityServicesConfigured;
        public uint? CodeIntegrityPolicy;       // 0 Off, 1 Audit, 2 Enforced
        public bool? HypervisorPresent;
    }

    private static DeviceGuardInfo TryReadDeviceGuard()
    {
        var info = new DeviceGuardInfo();
        try
        {
            using var cs = new ManagementObjectSearcher("SELECT HypervisorPresent FROM Win32_ComputerSystem");
            foreach (ManagementObject o in cs.Get())
            {
                info.HypervisorPresent = o["HypervisorPresent"] is bool b && b;
                break;
            }
        }
        catch { /* ignore */ }

        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\DeviceGuard");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_DeviceGuard"));
            foreach (ManagementObject o in searcher.Get())
            {
                info.VbsStatus = AsUInt(o["VirtualizationBasedSecurityStatus"]);
                info.SecurityServicesRunning = AsUIntArray(o["SecurityServicesRunning"]);
                info.SecurityServicesConfigured = AsUIntArray(o["SecurityServicesConfigured"]);
                info.CodeIntegrityPolicy = AsUInt(o["CodeIntegrityPolicyEnforcementStatus"]);
                break;
            }
        }
        catch
        {
            // Older Windows / access denied — fall back to registry only
        }

        return info;
    }

    private static SecurityFeatureStatus CheckHypervisor(DeviceGuardInfo dg)
    {
        bool running = dg.HypervisorPresent == true
                       || dg.VbsStatus == 2
                       || (dg.SecurityServicesRunning?.Contains(2u) ?? false);

        // Registry hint: HypervisorLaunchType in BCD is harder; also check HVCI scenario
        if (!running)
        {
            int? hvci = ReadDword(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
            int? vbs = ReadDword(@"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity");
            if (hvci == 1 || vbs == 1)
            {
                return new SecurityFeatureStatus
                {
                    Kind = SecurityFeatureKind.Hypervisor,
                    Name = "Windows Hypervisor",
                    Ok = false,
                    Summary = "Configured but not reported running",
                    Detail = "DeviceGuard policy enabled; HypervisorPresent=false",
                };
            }
        }

        return new SecurityFeatureStatus
        {
            Kind = SecurityFeatureKind.Hypervisor,
            Name = "Windows Hypervisor",
            Ok = running,
            Summary = running ? "Running" : "Not running",
            Detail = dg.HypervisorPresent == true
                ? "Win32_ComputerSystem.HypervisorPresent = True"
                : "HypervisorPresent = False / unavailable",
        };
    }

    private static SecurityFeatureStatus CheckVbs(DeviceGuardInfo dg)
    {
        bool running = dg.VbsStatus == 2;
        bool enabled = dg.VbsStatus is 1 or 2;
        if (dg.VbsStatus == null)
        {
            int? reg = ReadDword(@"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity");
            enabled = reg == 1;
            running = enabled; // best effort without WMI
        }

        return new SecurityFeatureStatus
        {
            Kind = SecurityFeatureKind.Vbs,
            Name = "VBS (Virtualization-based Security)",
            Ok = running,
            Summary = dg.VbsStatus switch
            {
                2 => "Running",
                1 => "Enabled (not running)",
                0 => "Off",
                _ => enabled ? "Enabled (registry)" : "Off / unknown",
            },
            Detail = $"VirtualizationBasedSecurityStatus={dg.VbsStatus?.ToString() ?? "n/a"}",
        };
    }

    private static SecurityFeatureStatus CheckHvci(DeviceGuardInfo dg)
    {
        bool running = dg.SecurityServicesRunning?.Contains(2u) ?? false;
        bool configured = dg.SecurityServicesConfigured?.Contains(2u) ?? false;
        if (dg.SecurityServicesRunning == null)
        {
            int? reg = ReadDword(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity",
                "Enabled");
            running = reg == 1;
            configured = running;
        }

        return new SecurityFeatureStatus
        {
            Kind = SecurityFeatureKind.Hvci,
            Name = "HVCI (Memory Integrity)",
            Ok = running,
            Summary = running ? "On" : configured ? "Configured / Off" : "Off",
            Detail = running
                ? "SecurityServicesRunning includes HVCI (2)"
                : "HVCI not running",
        };
    }

    private static SecurityFeatureStatus CheckSecureBoot()
    {
        // Primary: UEFI NVRAM EFI_GLOBAL_VARIABLE SecureBoot (1 byte: 1=on, 0=off)
        try
        {
            byte[]? sb = EfiNvram.ReadVariable("SecureBoot");
            if (sb is { Length: > 0 })
            {
                bool on = sb[0] != 0;
                string setupDetail = "";
                try
                {
                    byte[]? setup = EfiNvram.ReadVariable("SetupMode");
                    if (setup is { Length: > 0 })
                        setupDetail = $", SetupMode={setup[0]}{(setup[0] != 0 ? " (setup)" : " (user)")}";
                }
                catch { /* optional */ }

                return new SecurityFeatureStatus
                {
                    Kind = SecurityFeatureKind.SecureBoot,
                    Name = "Secure Boot",
                    Ok = on,
                    Summary = on ? "On" : "Off",
                    Detail = $"EFI NVRAM SecureBoot={sb[0]}{setupDetail}",
                };
            }
        }
        catch (Exception ex)
        {
            // Fall through to registry mirror
            int? state = ReadDword(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
            bool on = state == 1;
            return new SecurityFeatureStatus
            {
                Kind = SecurityFeatureKind.SecureBoot,
                Name = "Secure Boot",
                Ok = on,
                Summary = on ? "On (registry fallback)" : "Off (registry fallback)",
                Detail = $"NVRAM read failed: {ex.Message}; registry UEFISecureBootEnabled={state?.ToString() ?? "missing"}",
            };
        }

        // Variable missing — typically Legacy BIOS / no EFI
        int? reg = ReadDword(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        return new SecurityFeatureStatus
        {
            Kind = SecurityFeatureKind.SecureBoot,
            Name = "Secure Boot",
            Ok = reg == 1,
            Summary = reg == 1 ? "On (registry)" : "Off / unavailable",
            Detail = "EFI NVRAM SecureBoot variable not present; registry UEFISecureBootEnabled=" +
                     (reg?.ToString() ?? "missing"),
        };
    }

    private static SecurityFeatureStatus CheckDriverSignature()
    {
        // Test signing weakens driver signature enforcement
        bool testSigning = IsTestSigningOn();
        bool ok = !testSigning;

        return new SecurityFeatureStatus
        {
            Kind = SecurityFeatureKind.DriverSignature,
            Name = "Driver Signature Enforcement",
            Ok = ok,
            Summary = testSigning ? "Weakened (Test Signing ON)" : "Enforced",
            Detail = testSigning
                ? "TESTSIGNING found in SystemStartOptions / CI policy"
                : "Test signing not detected",
        };
    }

    private static SecurityFeatureStatus CheckCodeIntegrity(DeviceGuardInfo dg)
    {
        uint? policy = dg.CodeIntegrityPolicy;
        if (policy == null)
        {
            // Fallback: if CI\Config exists and no testsigning, treat as likely enforced
            bool test = IsTestSigningOn();
            return new SecurityFeatureStatus
            {
                Kind = SecurityFeatureKind.CodeIntegrity,
                Name = "Code Integrity",
                Ok = !test,
                Summary = test ? "Not fully enforced (test signing)" : "Likely enforced",
                Detail = "DeviceGuard CodeIntegrityPolicyEnforcementStatus unavailable",
            };
        }

        // 0 Off, 1 Audit, 2 Enforced
        bool ok = policy == 2;
        string summary = policy switch
        {
            2 => "Enforced",
            1 => "Audit mode",
            0 => "Off",
            _ => $"Status {policy}",
        };

        return new SecurityFeatureStatus
        {
            Kind = SecurityFeatureKind.CodeIntegrity,
            Name = "Code Integrity",
            Ok = ok,
            Summary = summary,
            Detail = $"CodeIntegrityPolicyEnforcementStatus = {policy}",
        };
    }

    private static SecurityFeatureStatus CheckVulnerableDriverBlocklist()
    {
        // 1 = blocklist enabled → vulnerable drivers are NOT allowed (good)
        // 0 / missing = blocklist disabled → vulnerable drivers may be allowed (bad)
        int? value = ReadDword(@"SYSTEM\CurrentControlSet\Control\CI\Config", "VulnerableDriverBlocklistEnable");
        bool blocklistOn = value == 1;
        bool vulnerableAllowed = !blocklistOn;

        return new SecurityFeatureStatus
        {
            Kind = SecurityFeatureKind.VulnerableDrivers,
            Name = "Vulnerable Driver Blocklist",
            Ok = blocklistOn,
            Summary = blocklistOn
                ? "Blocklist ON (vulnerable drivers blocked)"
                : "Blocklist OFF (vulnerable drivers allowed)",
            Detail = $"HKLM\\SYSTEM\\CurrentControlSet\\Control\\CI\\Config\\VulnerableDriverBlocklistEnable = {value?.ToString() ?? "missing (treated as off)"}" +
                     (vulnerableAllowed ? " — vulnerable drivers are allowed" : ""),
        };
    }

    private static bool IsTestSigningOn()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
            if (key?.GetValue("SystemStartOptions") is string opts &&
                opts.Contains("TESTSIGNING", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { /* ignore */ }

        // Additional CI policy hint
        int? ci = ReadDword(@"SYSTEM\CurrentControlSet\Control\CI", "TestSigning");
        if (ci == 1) return true;

        return false;
    }

    private static int? ReadDword(string subKey, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            object? v = key?.GetValue(name);
            return v switch
            {
                int i => i,
                uint u => unchecked((int)u),
                long l => (int)l,
                byte[] b when b.Length >= 4 => BitConverter.ToInt32(b, 0),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static uint? AsUInt(object? o) => o switch
    {
        uint u => u,
        int i => unchecked((uint)i),
        ushort us => us,
        short s => unchecked((uint)s),
        _ => null,
    };

    private static uint[]? AsUIntArray(object? o)
    {
        if (o is uint[] ua) return ua;
        if (o is int[] ia) return ia.Select(i => unchecked((uint)i)).ToArray();
        if (o is ushort[] usa) return usa.Select(u => (uint)u).ToArray();
        if (o is Array arr)
        {
            var list = new List<uint>();
            foreach (var item in arr)
            {
                var u = AsUInt(item);
                if (u != null) list.Add(u.Value);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }
        return null;
    }
}
