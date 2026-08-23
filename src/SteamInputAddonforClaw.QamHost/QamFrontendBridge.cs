using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Text.Json;

namespace SteamInputAddonforClaw.QamHost;

/// <summary>QamHost's narrow Runtime transport boundary. UI-only Runtime methods are not exposed here.</summary>
internal sealed class QamFrontendBridge : IAsyncDisposable
{
    internal sealed record Response(long Id, bool Ok, object? Payload = null, string? Error = null);
    internal static readonly JsonSerializerOptions BridgeJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly NamedPipeAddonFrontendClient _client = new(FrontendPipeEndpoint.CreateQamForCurrentUser());
    internal NamedPipeAddonFrontendClient Client => _client;
    internal event EventHandler? StateInvalidated;
    private int _stopping;

    internal QamFrontendBridge() => _client.StateInvalidated += OnStateInvalidated;

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void OnStateInvalidated(object? sender, EventArgs e) => StateInvalidated?.Invoke(this, e);
    internal async Task<Response> HandleRequestAsync(string payload, CancellationToken token)
    {
        long id = 0;
        if (Volatile.Read(ref _stopping) != 0) return Error(id, "QAM bridge is stopping.");
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out id))
                throw new JsonException("Invalid or missing request id.");
            var method = root.GetProperty("method").GetString();
            object result = method switch
            {
                "captureStatus" => await _client.CaptureStatusAsync(token),
                "captureCpuBoost" => await _client.CaptureCpuBoostAsync(token),
                "captureTdp" => await _client.CaptureTdpAsync(token),
                "captureActiveGameProfile" => await _client.CaptureActiveGameProfileAsync(token),
                "setActiveGameProfileEnabled" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileEnabledAsync(id, p.GetProperty("enabled").GetBoolean(), p.TryGetProperty("displayName", out var name) ? name.GetString() : null, t)),
                "setActiveGameCpuBoostAc" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileCpuBoostAcAsync(id, p.GetProperty("mode").Deserialize<CpuBoostMode>(), t)),
                "setActiveGameCpuBoostDc" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileCpuBoostDcAsync(id, p.GetProperty("mode").Deserialize<CpuBoostMode>(), t)),
                "setActiveGameTdp" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileTdpAsync(id, p.GetProperty("configuration").Deserialize<FrontendGameTdpConfiguration>(BridgeJson)!, t)),
                "setDeviceCpuBoostEnabled" => await MutateAsync(root, token, static async (c, p, t) => (object)await c.SetDeviceCpuBoostEnabledAsync(p.GetProperty("enabled").GetBoolean(), t)),
                "setDeviceCpuBoostAc" => await MutateAsync(root, token, static async (c, p, t) => (object)await c.SetDeviceCpuBoostAcAsync(p.GetProperty("mode").Deserialize<CpuBoostMode>(), t)),
                "setDeviceCpuBoostDc" => await MutateAsync(root, token, static async (c, p, t) => (object)await c.SetDeviceCpuBoostDcAsync(p.GetProperty("mode").Deserialize<CpuBoostMode>(), t)),
                "setDeviceTdp" => await MutateAsync(root, token, static async (c, p, t) => (object)await c.SetDeviceTdpAsync(DecodeTdpConfiguration(p), t)),
                "setDeviceTdpEnabled" => await MutateAsync(root, token, static async (c, p, t) => (object)await c.SetDeviceTdpEnabledAsync(p.GetProperty("enabled").GetBoolean(), t)),
                _ => throw new InvalidOperationException("Unsupported QAM method.")
            };
            return new Response(id, true, result);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FrontendTransportException)
        { return Error(id, "Invalid or unavailable QAM bridge request."); }
    }
    private async Task<object> MutateAsync(JsonElement root, CancellationToken token, Func<NamedPipeAddonFrontendClient, JsonElement, CancellationToken, Task<object>> mutation)
    {
        var status = await _client.CaptureStatusAsync(token).ConfigureAwait(false);
        if (!status.Steam.Active || status.Steam.AppId != 0 || status.Steam.Source != FrontendSteamSource.BigPicture)
            throw new InvalidOperationException("Device QAM mutation is available only in Big Picture with no running game.");
        return await mutation(_client, root.GetProperty("payload"), token).ConfigureAwait(false);
    }
    private async Task<object> ActiveMutationAsync(JsonElement root, CancellationToken token, Func<NamedPipeAddonFrontendClient, uint, JsonElement, CancellationToken, Task<object>> mutation)
    {
        var active = await _client.CaptureActiveGameProfileAsync(token).ConfigureAwait(false);
        if (active.AppId == 0 || !active.Exists && active.Enabled) throw new InvalidOperationException("No active game.");
        return await mutation(_client, active.AppId, root.GetProperty("payload"), token).ConfigureAwait(false);
    }
    internal static FrontendTdpConfiguration DecodeTdpConfiguration(JsonElement payload) =>
        payload.GetProperty("configuration").Deserialize<FrontendTdpConfiguration>(BridgeJson)
        ?? throw new JsonException("Invalid TDP configuration.");
    private static Response Error(long id, string message) => new(id, false, Error: message);
    internal void StopAccepting() => Interlocked.Exchange(ref _stopping, 1);
    public async ValueTask DisposeAsync()
    {
        StopAccepting();
        _client.StateInvalidated -= OnStateInvalidated;
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}
