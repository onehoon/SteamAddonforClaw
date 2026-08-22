namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal interface IMsiClawTdpTransport
{
    bool TryGetAp(int index, out byte[] payload);
    bool TrySetData(int block, byte value);
}

internal sealed class HelperMsiClawTdpTransport : IMsiClawTdpTransport, IAsyncDisposable
{
    private readonly TdpHelperClient _client = new();
    public bool TryGetAp(int index, out byte[] payload) => _client.TryGetAp(index, out payload);
    public bool TrySetData(int block, byte value) => _client.TrySetData(block, value);
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
