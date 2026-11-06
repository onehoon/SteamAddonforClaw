using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>Reads the MSI Center M Manual values used to seed a newly enabled TDP profile.</summary>
internal static class WindowsTdpCenterMManualSettings
{
    private const string UserScenarioPath = @"SOFTWARE\WOW6432Node\MSI\MSI Center M\Component\User Scenario";

    internal static DeviceTdpSettings? Read(HandheldDeviceModelId modelId)
    {
        if (!MsiClawTdpPolicy.TryResolve(modelId, out var policy)) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(UserScenarioPath, writable: false);
            if (key is null) return null;
            var values = new[] { "ManualPL1AC", "ManualPL2AC", "ManualPL1DC", "ManualPL2DC" }
                .Select(name => ReadInt(key.GetValue(name))).ToArray();
            if (values.Any(value => value is null)) return null;
            var ac = new TdpPowerPair { Pl1Watts = values[0]!.Value, Pl2Watts = values[1]!.Value };
            var dc = new TdpPowerPair { Pl1Watts = values[2]!.Value, Pl2Watts = values[3]!.Value };
            return policy.IsValid(ac) && policy.IsValid(dc)
                ? new DeviceTdpSettings { Enabled = true, Ac = ac, Dc = dc }
                : null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    private static int? ReadInt(object? value) => value switch
    {
        int number => number,
        long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
        string text when int.TryParse(text, out var number) => number,
        _ => null
    };
}
