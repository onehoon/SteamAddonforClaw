using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Text.Json;

namespace SteamInputAddonforClaw.QamHost;

/// <summary>QamHost's narrow Runtime transport boundary. UI-only Runtime methods are not exposed here.</summary>
internal sealed class QamFrontendBridge : IAsyncDisposable
{
    private readonly NamedPipeAddonFrontendClient _client = new(FrontendPipeEndpoint.CreateQamForCurrentUser());
    internal NamedPipeAddonFrontendClient Client => _client;
    internal event EventHandler? StateInvalidated;
    private int _stopping;

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _client.StateInvalidated += OnStateInvalidated;
        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnStateInvalidated(object? sender, EventArgs e) => StateInvalidated?.Invoke(this, e);
    internal async Task<string> HandleRequestAsync(string payload, CancellationToken token)
    {
        if (Volatile.Read(ref _stopping) != 0) return Error(0, "QAM bridge is stopping.");
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var id = root.GetProperty("id").GetInt64();
            var method = root.GetProperty("method").GetString();
            object result = method switch
            {
                "captureStatus" => await _client.CaptureStatusAsync(token),
                "captureCpuBoost" => await _client.CaptureCpuBoostAsync(token),
                "captureTdp" => await _client.CaptureTdpAsync(token),
                "setDeviceCpuBoostEnabled" => await MutateAsync(root, token, static async (c, p, t) => (object)await c.SetDeviceCpuBoostEnabledAsync(p.GetProperty("enabled").GetBoolean(), t)),
                "setDeviceCpuBoostAc" => await MutateAsync(root, token, static async (c, p, t) => (object)await c.SetDeviceCpuBoostAcAsync(p.GetProperty("mode").Deserialize<CpuBoostMode>(), t)),
                "setDeviceCpuBoostDc" => await MutateAsync(root, token, static async (c, p, t) => (object)await c.SetDeviceCpuBoostDcAsync(p.GetProperty("mode").Deserialize<CpuBoostMode>(), t)),
                "setDeviceTdp" => await MutateAsync(root, token, static async (c, p, t) => (object)await c.SetDeviceTdpAsync(p.GetProperty("configuration").Deserialize<FrontendTdpConfiguration>() ?? throw new JsonException(), t)),
                "setDeviceTdpEnabled" => await MutateAsync(root, token, static async (c, p, t) => (object)await c.SetDeviceTdpEnabledAsync(p.GetProperty("enabled").GetBoolean(), t)),
                _ => throw new InvalidOperationException("Unsupported QAM method.")
            };
            return JsonSerializer.Serialize(new { id, ok = true, payload = result });
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FrontendTransportException)
        { return Error(TryGetId(payload), exception.Message); }
    }
    private async Task<object> MutateAsync(JsonElement root, CancellationToken token, Func<NamedPipeAddonFrontendClient, JsonElement, CancellationToken, Task<object>> mutation)
    {
        var status = await _client.CaptureStatusAsync(token).ConfigureAwait(false);
        if (status.Steam.AppId != 0) throw new InvalidOperationException("Device QAM mutation is unavailable while a game is active.");
        return await mutation(_client, root.GetProperty("payload"), token).ConfigureAwait(false);
    }
    private static string Error(long id, string message) => JsonSerializer.Serialize(new { id, ok = false, error = message });
    private static long TryGetId(string payload) => long.TryParse(JsonDocument.Parse(payload).RootElement.TryGetProperty("id", out var id) ? id.GetRawText() : null, out var value) ? value : 0;
    internal void StopAccepting() => Interlocked.Exchange(ref _stopping, 1);
    public ValueTask DisposeAsync() { StopAccepting(); return _client.DisposeAsync(); }
}
