using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Atlas.Core.Services;

public static class MachineIdentityService
{
    public static string CurrentDeviceId { get; } = BuildDeviceId(ReadMachineIdentity());

    public static string Normalize(string? value) => new((value ?? string.Empty)
        .Where(char.IsLetterOrDigit)
        .Select(char.ToUpperInvariant)
        .ToArray());

    private static string ReadMachineIdentity()
    {
        if (OperatingSystem.IsWindows())
        {
            var machineGuid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null)?.ToString();
            if (!string.IsNullOrWhiteSpace(machineGuid)) return machineGuid;
        }
        try
        {
            const string linuxMachineId = "/etc/machine-id";
            if (File.Exists(linuxMachineId)) return File.ReadAllText(linuxMachineId).Trim();
        }
        catch { }
        return $"{Environment.MachineName}|{Environment.OSVersion.VersionString}";
    }

    private static string BuildDeviceId(string source)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"Biblideo Atlas|{source.Trim().ToUpperInvariant()}"));
        var value = Convert.ToHexString(bytes)[..20];
        return $"ATL-{value[..4]}-{value[4..8]}-{value[8..12]}-{value[12..16]}-{value[16..20]}";
    }
}
