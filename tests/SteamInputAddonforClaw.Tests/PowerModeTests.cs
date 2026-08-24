using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Profiles.Performance;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class PowerModeTests
{
    [Theory]
    [InlineData("961cc777-2547-4f9d-8174-7d86181b8a7a", PowerModeReadStatus.Known, WindowsPowerMode.BestPowerEfficiency)]
    [InlineData("00000000-0000-0000-0000-000000000000", PowerModeReadStatus.Known, WindowsPowerMode.Balanced)]
    [InlineData("ded574b5-45a0-4f42-8737-46345c09c238", PowerModeReadStatus.Known, WindowsPowerMode.BestPerformance)]
    [InlineData("11111111-1111-1111-1111-111111111111", PowerModeReadStatus.Unknown, null)]
    public void Maps_documented_guids_without_normalizing_unknown(string raw, PowerModeReadStatus status, WindowsPowerMode? mode)
    {
        var result = WindowsPowerModePolicy.Map(Guid.Parse(raw), "AC");
        Assert.Equal(status, result.Status);
        Assert.Equal(mode, result.Mode);
    }

    [Theory]
    [InlineData(WindowsPowerMode.BestPowerEfficiency, "961cc777-2547-4f9d-8174-7d86181b8a7a")]
    [InlineData(WindowsPowerMode.Balanced, "00000000-0000-0000-0000-000000000000")]
    [InlineData(WindowsPowerMode.BestPerformance, "ded574b5-45a0-4f42-8737-46345c09c238")]
    public void Maps_all_supported_modes_to_the_documented_guid(WindowsPowerMode mode, string raw) => Assert.Equal(Guid.Parse(raw), WindowsPowerModePolicy.ToGuid(mode));
}
