using SteamInputAddonforClaw.QamHost;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class QamFrontendBridgeTests
{
    [Fact]
    public async Task Malformed_binding_payload_returns_bounded_error_without_throwing()
    {
        await using var bridge = new QamFrontendBridge();

        var response = await bridge.HandleRequestAsync("{not-json", CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal(0, response.Id);
        Assert.Equal("Invalid or unavailable QAM bridge request.", response.Error);
    }

    [Fact]
    public async Task Missing_request_id_returns_bounded_error_without_throwing()
    {
        await using var bridge = new QamFrontendBridge();

        var response = await bridge.HandleRequestAsync("{\"method\":\"captureStatus\"}", CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal(0, response.Id);
        Assert.Equal("Invalid or unavailable QAM bridge request.", response.Error);
    }
}
