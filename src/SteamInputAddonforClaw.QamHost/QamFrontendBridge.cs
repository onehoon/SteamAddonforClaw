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

    // SF-V2-04: the generic Quick Settings records must fail closed on omitted identities. BridgeJson
    // deliberately does not enforce required constructor parameters (legacy compatibility surface), so
    // a missing enum-valued field would silently default to enum member 0 (Device / DeviceTdpEnabled /
    // Boolean) and reserialize into a valid strict v28 wire payload. Decode the two new payloads with a
    // stricter clone so a malformed dynamic JS request is rejected before any Runtime call.
    private static readonly JsonSerializerOptions QuickSettingsBridgeJson = new(BridgeJson) { RespectRequiredConstructorParameters = true };

    internal static WindowsPowerMode DecodePowerMode(JsonElement payload) =>
        payload.GetProperty("mode").Deserialize<WindowsPowerMode>();
    private readonly NamedPipeAddonFrontendClient _client;
    internal NamedPipeAddonFrontendClient Client => _client;
    internal event EventHandler? StateInvalidated;
    private int _stopping;

    internal QamFrontendBridge() : this(FrontendPipeEndpoint.CreateQamForCurrentUser()) { }

    // Test seam only: an isolated random pipe. Production behaviour is identical to the parameterless
    // constructor.
    internal QamFrontendBridge(string pipeName)
    {
        _client = new NamedPipeAddonFrontendClient(pipeName);
        _client.StateInvalidated += OnStateInvalidated;
    }

    // SF-V2-04: bridge-local generic Quick Settings capture request. Kept private rather than making
    // the frontend-transport DTO public.
    private sealed record QuickSettingsPageBridgeRequest(QuickSettingsPageId PageId, uint? AppId = null);

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
                // SF-V2-05: the QAM Device renderer now reads/mutates only through the shared Quick
                // Settings seam. The feature-specific Device bridge operations were removed once qam.js
                // stopped calling them; the focused NamedPipeAddonFrontendClient typed Device APIs stay
                // for Main UI / other code.
                "captureQuickSettingsPage" => await CaptureQuickSettingsPageAsync(root, token),
                "mutateQuickSetting" => await MutateQuickSettingAsync(root, token),
                "captureActiveGameProfile" => await _client.CaptureActiveGameProfileAsync(token),
                "setActiveGameProfileEnabled" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileEnabledAsync(id, p.GetProperty("enabled").GetBoolean(), p.TryGetProperty("displayName", out var name) ? name.GetString() : null, t)),
                "setActiveGameCpuBoostEnabled" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileCpuBoostEnabledAsync(id, p.GetProperty("enabled").GetBoolean(), t)),
                "setActiveGameCpuBoostAc" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileCpuBoostAcAsync(id, p.GetProperty("mode").Deserialize<CpuBoostMode>(), t)),
                "setActiveGameCpuBoostDc" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileCpuBoostDcAsync(id, p.GetProperty("mode").Deserialize<CpuBoostMode>(), t)),
                "setActiveGameTdp" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileTdpAsync(id, p.GetProperty("configuration").Deserialize<FrontendGameTdpConfiguration>(BridgeJson)!, t)),
                "setActiveGameTdpEnabled" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileTdpEnabledAsync(id, p.GetProperty("enabled").GetBoolean(), t)),
                "setActiveGamePowerModeAc" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfilePowerModeAcAsync(id, DecodePowerMode(p), t)),
                "setActiveGamePowerModeEnabled" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfilePowerModeEnabledAsync(id, p.GetProperty("enabled").GetBoolean(), t)),
                "setActiveGamePowerModeDc" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfilePowerModeDcAsync(id, DecodePowerMode(p), t)),
                "setActiveGameFpsLimitEnabled" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileFpsLimitEnabledAsync(id, p.GetProperty("enabled").GetBoolean(), t)),
                "setActiveGameFpsLimitAc" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileFpsLimitAcAsync(id, p.GetProperty("fps").GetInt32(), t)),
                "setActiveGameFpsLimitDc" => await ActiveMutationAsync(root, token, static async (c, id, p, t) => (object)await c.SetGameProfileFpsLimitDcAsync(id, p.GetProperty("fps").GetInt32(), t)),
                _ => throw new InvalidOperationException("Unsupported QAM method.")
            };
            return new Response(id, true, result);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FrontendTransportException)
        { return Error(id, "Invalid or unavailable QAM bridge request."); }
    }
    // The one Device QAM mutation admission rule. Surface-owned policy only -- it is deliberately NOT
    // moved into the SF-V2-03 mutation adapter / presentation / feature Runtimes.
    private async Task EnsureDeviceMutationAdmittedAsync(CancellationToken token)
    {
        var status = await _client.CaptureStatusAsync(token).ConfigureAwait(false);
        if (!status.Steam.Active || status.Steam.AppId != 0 || status.Steam.Source != FrontendSteamSource.BigPicture)
            throw new InvalidOperationException("Device QAM mutation is available only in Big Picture with no running game.");
    }

    private async Task<object> CaptureQuickSettingsPageAsync(JsonElement root, CancellationToken token)
    {
        var request = root.GetProperty("payload").Deserialize<QuickSettingsPageBridgeRequest>(QuickSettingsBridgeJson)
            ?? throw new JsonException("Invalid Quick Settings page request.");
        if (!Enum.IsDefined(request.PageId))
            throw new JsonException("Invalid Quick Settings page id.");
        return await _client.CaptureQuickSettingsPageAsync(request.PageId, request.AppId, token).ConfigureAwait(false);
    }

    private async Task<object> MutateQuickSettingAsync(JsonElement root, CancellationToken token)
    {
        var intent = root.GetProperty("payload").Deserialize<QuickSettingsMutationIntent>(QuickSettingsBridgeJson)
            ?? throw new JsonException("Invalid Quick Settings mutation intent.");
        // Surface scope for SF-V2-04/05: only Device mutation is exposed through the generic QAM path.
        // Profile generic admission is a later focused milestone. Row/value/AppId/TDP-group validation
        // stays in the SF-V2-03 QuickSettingsMutationAdapter.
        if (intent.PageId != QuickSettingsPageId.Device)
            throw new InvalidOperationException("Only Device Quick Settings mutation is available through the QAM generic seam.");
        await EnsureDeviceMutationAdmittedAsync(token).ConfigureAwait(false);
        return await _client.MutateQuickSettingAsync(intent, token).ConfigureAwait(false);
    }
    private async Task<object> ActiveMutationAsync(JsonElement root, CancellationToken token, Func<NamedPipeAddonFrontendClient, uint, JsonElement, CancellationToken, Task<object>> mutation)
    {
        var active = await _client.CaptureActiveGameProfileAsync(token).ConfigureAwait(false);
        if (active.AppId == 0 || !active.Exists && active.Enabled) throw new InvalidOperationException("No active game.");
        return await mutation(_client, active.AppId, root.GetProperty("payload"), token).ConfigureAwait(false);
    }
    private static Response Error(long id, string message) => new(id, false, Error: message);
    internal void StopAccepting() => Interlocked.Exchange(ref _stopping, 1);
    public async ValueTask DisposeAsync()
    {
        StopAccepting();
        _client.StateInvalidated -= OnStateInvalidated;
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}
