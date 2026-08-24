using System.Text.Json;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.QamHost;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class QamFrontendBridgeTests
{
    [Fact]
    public void Tdp_configuration_round_trips_through_bridge_camel_case_json()
    {
        var expected = new FrontendTdpConfiguration(true, new(21, 31), new(11, 19));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { configuration = expected }, QamFrontendBridge.BridgeJson));

        var actual = QamFrontendBridge.DecodeTdpConfiguration(document.RootElement);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0, WindowsPowerMode.BestPowerEfficiency)]
    [InlineData(1, WindowsPowerMode.Balanced)]
    [InlineData(2, WindowsPowerMode.BestPerformance)]
    public void Power_mode_ordinal_payload_decodes_through_bridge(int ordinal, WindowsPowerMode expected)
    {
        using var document = JsonDocument.Parse($"{{\"mode\":{ordinal}}}");

        Assert.Equal(expected, QamFrontendBridge.DecodePowerMode(document.RootElement));
    }

    [Fact]
    public async Task Malformed_bridge_payload_returns_bounded_error()
    {
        await using var bridge = new QamFrontendBridge();

        var response = await bridge.HandleRequestAsync("{", CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal(0, response.Id);
        Assert.NotNull(response.Error);
    }
}
