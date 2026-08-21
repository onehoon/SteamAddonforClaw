using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.QamHost;

/// <summary>QamHost's narrow Runtime transport boundary. UI-only Runtime methods are not exposed here.</summary>
internal sealed class QamFrontendBridge : IAsyncDisposable
{
    private readonly NamedPipeAddonFrontendClient _client = new(FrontendPipeEndpoint.CreateQamForCurrentUser());
    internal NamedPipeAddonFrontendClient Client => _client;
    internal event EventHandler? StateInvalidated;

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _client.StateInvalidated += OnStateInvalidated;
        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnStateInvalidated(object? sender, EventArgs e) => StateInvalidated?.Invoke(this, e);
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
