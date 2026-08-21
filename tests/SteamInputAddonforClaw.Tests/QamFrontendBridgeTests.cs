using SteamInputAddonforClaw.QamHost;
using System.Text.Json;
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

    [Fact]
    public void Production_bridge_serialization_uses_javascript_field_names()
    {
        var json = JsonSerializer.Serialize(new QamFrontendBridge.Response(17, true, new { value = 1 }), QamFrontendBridge.BridgeJson);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(17, document.RootElement.GetProperty("id").GetInt64());
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("Id", out _));
    }
}
