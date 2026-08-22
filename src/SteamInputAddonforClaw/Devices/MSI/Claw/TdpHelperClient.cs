using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class TdpHelperClient : IAsyncDisposable
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);
    private readonly Lock _sync = new();
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Process? _process;
    private readonly string _pipeName = $"SteamInputAddonforClaw.Tdp.{Environment.ProcessId}.{Guid.NewGuid():N}";

    public bool TryGetAp(int index, out byte[] payload) => Invoke(new("GetAp", index, 0), out payload);
    public bool TrySetData(int block, byte value) => Invoke(new("SetData", block, value), out _);

    private bool Invoke(Request request, out byte[] payload)
    {
        payload = [];
        lock (_sync)
        {
            try
            {
                EnsureConnected();
                _writer!.WriteLine(JsonSerializer.Serialize(request));
                _writer.Flush();
                using var responseTimeout = new CancellationTokenSource(ResponseTimeout);
                var responseLine = _reader!.ReadLineAsync(responseTimeout.Token).AsTask().GetAwaiter().GetResult();
                var response = JsonSerializer.Deserialize<Response>(responseLine ?? "");
                if (response?.Ok != true) return false;
                payload = response.Payload is null ? [] : Convert.FromBase64String(response.Payload);
                return request.Operation == "SetData" || payload.Length > 0;
            }
            catch { CloseUnderLock(); return false; }
        }
    }

    private void EnsureConnected()
    {
        if (_pipe?.IsConnected == true) return;
        CloseUnderLock();
        var path = Path.Combine(AppContext.BaseDirectory, "SteamInputAddonforClaw.TdpHelper.exe");
        _process = Process.Start(new ProcessStartInfo(path, _pipeName) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory })
            ?? throw new InvalidOperationException("TDP helper could not be started.");
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        _pipe.Connect(5000);
        _reader = new StreamReader(_pipe);
        _writer = new StreamWriter(_pipe) { AutoFlush = true };
    }

    private void CloseUnderLock()
    {
        try { _pipe?.Dispose(); } catch { }
        try { if (_process is { HasExited: false }) { _process.Kill(); _process.WaitForExit(1000); } } catch { }
        _pipe = null; _reader = null; _writer = null; _process?.Dispose(); _process = null;
    }

    public ValueTask DisposeAsync() { lock (_sync) CloseUnderLock(); return ValueTask.CompletedTask; }
    private sealed record Request(string Operation, int Index, byte Value);
    private sealed record Response(bool Ok, string? Payload);
}
