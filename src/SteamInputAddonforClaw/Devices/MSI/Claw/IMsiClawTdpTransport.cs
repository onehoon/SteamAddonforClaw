namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal interface IMsiClawTdpTransport
{
    bool TryGetAp(int index, out byte[] payload);
    bool TrySetData(int block, byte value);
    bool TryGetFan(int block, out byte[] payload) { payload = []; return false; }
    bool TrySetFan(int block, byte[] payload) => false;
    bool TryGetTemperature(int index, out byte[] payload) { payload = []; return false; }
    bool TryGetThermal(int index, out byte[] payload) { payload = []; return false; }
    bool TryGetData(int block, out byte[] payload) { payload = []; return false; }
}

internal sealed class HelperMsiClawTdpTransport : IMsiClawTdpTransport, IMsiFanDiagnosticTransport, IAsyncDisposable
{
    private readonly TdpHelperClient _client = new();
    public bool TryGetAp(int index, out byte[] payload) => _client.TryGetAp(index, out payload);
    public bool TrySetData(int block, byte value) => _client.TrySetData(block, value);
    public bool TryGetFan(int block, out byte[] payload) => _client.TryGetFan(block, out payload);
    public bool TrySetFan(int block, byte[] payload) => _client.TrySetFan(block, payload);
    public bool TryGetTemperature(int index, out byte[] payload) => _client.TryGetTemperature(index, out payload);
    public bool TryGetThermal(int index, out byte[] payload) => _client.TryGetThermal(index, out payload);
    public bool TryGetData(int block, out byte[] payload) => _client.TryGetData(block, out payload);
    public bool TryGetHelperInfo(out MsiFanHelperInfo info) => _client.TryGetHelperInfo(out info);
    public bool TryGetWmiVersion(out MsiFanWmiVersion version) => _client.TryGetWmiVersion(out version);
    public bool TryGetMethodInventory(out string[] methods) => _client.TryGetMethodInventory(out methods);
    public MsiFanOperationResult InvokeFanDiagnostic(string operation, int block, byte[]? payload) => _client.InvokeFanDiagnostic(operation, block, payload);
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
