using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ExportDocManager.Utils;
using Microsoft.Win32;

namespace ExportDocManager.Services.Security;

internal static class RuntimeLicenseDeviceFingerprint
{
    private const int PlatformCommandTimeoutMs = 2500;

    public static string Create()
    {
        var parts = new List<string>();
        AddPart(parts, RuntimeInformation.OSArchitecture.ToString());
        AddPart(parts, Environment.MachineName);

        if (OperatingSystem.IsWindows())
        {
            AddWindowsParts(parts);
        }
        else if (OperatingSystem.IsMacOS())
        {
            AddMacOsParts(parts);
        }
        else
        {
            AddUnixParts(parts);
        }

        if (parts.Count == 0)
        {
            AddPart(parts, RuntimeInformation.ProcessArchitecture.ToString());
        }

        return string.Join(
            "|",
            parts.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsParts(List<string> parts)
    {
        AddPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"SOFTWARE\Microsoft\Cryptography", "MachineGuid"));
        AddPart(parts, ReadWindowsRegistryValue(RegistryView.Registry32, @"SOFTWARE\Microsoft\Cryptography", "MachineGuid"));
        AddPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductId"));
        AddPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer"));
        AddPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName"));
        AddPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct"));
        AddPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"HARDWARE\DESCRIPTION\System\BIOS", "BIOSVendor"));
    }

    [SupportedOSPlatform("windows")]
    private static string ReadWindowsRegistryValue(RegistryView view, string subKeyName, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var subKey = baseKey.OpenSubKey(subKeyName);
            return subKey?.GetValue(valueName)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    [SupportedOSPlatform("macos")]
    private static void AddMacOsParts(List<string> parts)
    {
        AddPart(parts, ReadFirstLine("/var/db/dbuuid"));
        string ioreg = ReadCommandOutput(
            "/usr/sbin/ioreg",
            "-rd1",
            "-c",
            "IOPlatformExpertDevice");
        AddPart(parts, ExtractMacOsIoregValue(ioreg, "IOPlatformUUID"));
        AddPart(parts, ExtractMacOsIoregValue(ioreg, "IOPlatformSerialNumber"));
    }

    private static string ExtractMacOsIoregValue(string output, string key)
    {
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        string marker = $"\"{key}\" =";
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                return line[(index + marker.Length)..].Trim().Trim('"');
            }
        }

        return string.Empty;
    }

    private static void AddUnixParts(List<string> parts)
    {
        foreach (string path in new[]
        {
            "/etc/machine-id",
            "/var/lib/dbus/machine-id",
            "/sys/class/dmi/id/product_uuid",
            "/sys/class/dmi/id/product_serial",
            "/sys/class/dmi/id/board_serial"
        })
        {
            AddPart(parts, ReadFirstLine(path));
        }
    }

    private static string ReadFirstLine(string path)
    {
        try
        {
            return File.Exists(path)
                ? File.ReadLines(path).FirstOrDefault() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadCommandOutput(string fileName, params string[] arguments)
    {
        return TryRunProcess(fileName, arguments, out string output)
            ? output
            : string.Empty;
    }

    private static bool TryRunProcess(
        string fileName,
        IEnumerable<string> arguments,
        out string output)
    {
        output = string.Empty;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                return false;
            }

            Task<string> outputTask = BoundedProcessOutput.ReadAsync(
                process.StandardOutput,
                truncationMessage: "[授权平台命令输出过长，已截断]");
            Task<string> errorTask = BoundedProcessOutput.ReadAsync(
                process.StandardError,
                truncationMessage: "[授权平台命令错误输出过长，已截断]");

            if (!process.WaitForExit(PlatformCommandTimeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                BoundedProcessOutput.DrainProcessAsync(process, TimeSpan.FromSeconds(5))
                    .GetAwaiter()
                    .GetResult();
                BoundedProcessOutput.ObserveAsync(TimeSpan.FromSeconds(5), outputTask, errorTask)
                    .GetAwaiter()
                    .GetResult();
                return false;
            }

            output = outputTask.GetAwaiter().GetResult().Trim();
            _ = errorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch
        {
            output = string.Empty;
            return false;
        }
    }

    private static void AddPart(List<string> parts, string value)
    {
        string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (IsUsablePart(normalized))
        {
            parts.Add(normalized);
        }
    }

    private static bool IsUsablePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4)
        {
            return false;
        }

        return value is not (
            "TO BE FILLED BY O.E.M." or
            "DEFAULT STRING" or
            "NONE" or
            "UNKNOWN" or
            "SYSTEM SERIAL NUMBER" or
            "NOT SPECIFIED" or
            "UNAVAILABLE" or
            "00000000-0000-0000-0000-000000000000");
    }
}
