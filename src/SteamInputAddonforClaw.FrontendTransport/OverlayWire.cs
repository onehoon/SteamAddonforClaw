using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamInputAddonforClaw.FrontendTransport;

internal static class OverlayTransportProtocol
{
    internal const int CurrentVersion = 2;
    internal const int MaxFrameBytes = 64 * 1024;
}

internal enum OverlayWireMessageKind { Handshake, HandshakeAccepted, Command, State, DismissRequested, ProtocolError }
internal enum OverlayCommand { Show, Hide, Shutdown }
internal enum OverlayState { Ready, Visible, Hidden }

internal sealed record OverlayWireMessage(
    int ProtocolVersion,
    OverlayWireMessageKind Kind,
    OverlayCommand? Command = null,
    OverlayState? State = null,
    string? Error = null);

internal static class OverlayWireCodec
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    internal static async Task WriteAsync(Stream stream, OverlayWireMessage message, SemaphoreSlim gate, CancellationToken token)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        if (payload.Length is 0 or > OverlayTransportProtocol.MaxFrameBytes)
            throw new FrontendProtocolException("Invalid Overlay frame length.");

        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(prefix, token).ConfigureAwait(false);
            await stream.WriteAsync(payload, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    internal static async Task<OverlayWireMessage> ReadAsync(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4];
        await ReadExactlyAsync(stream, prefix, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > OverlayTransportProtocol.MaxFrameBytes)
            throw new FrontendProtocolException("Invalid Overlay frame length.");

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, token).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<OverlayWireMessage>(payload, Json)
                ?? throw new FrontendProtocolException("Invalid Overlay JSON frame.");
        }
        catch (JsonException exception)
        {
            throw new FrontendProtocolException($"Invalid Overlay JSON frame: {exception.Message}");
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> target, CancellationToken token)
    {
        var offset = 0;
        while (offset < target.Length)
        {
            var read = await stream.ReadAsync(target[offset..], token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}

internal sealed class NamedPipeOverlayServer : IAsyncDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private readonly string _pipeName;
    private readonly Func<NamedPipeServerStream> _pipeFactory;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _sync = new();
    private readonly TaskCompletionSource _serverReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _ready = NewSignal();
    private TaskCompletionSource? _acknowledgement;
    private NamedPipeServerStream? _activePipe;
    private Task? _acceptLoop;
    private bool _readyState;
    private OverlayState _state = OverlayState.Hidden;
    private int _started;
    private int _disposed;

    internal event Action<NamedPipeOverlayServer>? DismissRequested;

    internal NamedPipeOverlayServer(string pipeName)
        : this(pipeName, () => new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly)) { }

    internal NamedPipeOverlayServer(string pipeName, Func<NamedPipeServerStream> pipeFactory)
    {
        _pipeName = pipeName;
        _pipeFactory = pipeFactory;
    }

    internal bool IsReady { get { lock (_sync) return _readyState; } }
    internal OverlayState State { get { lock (_sync) return _state; } }

    internal async Task StartAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Overlay server already started.");
        _acceptLoop = AcceptLoopAsync();
        await _serverReady.Task.WaitAsync(token).ConfigureAwait(false);
    }

    internal async Task<bool> WaitForReadyAsync(TimeSpan timeout, CancellationToken token = default)
    {
        try
        {
            await _ready.Task.WaitAsync(timeout, token).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
    }

    internal async Task<bool> SendCommandAsync(OverlayCommand command, CancellationToken token = default)
    {
        await _commandGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!await WaitForReadyAsync(CommandTimeout, token).ConfigureAwait(false)) return false;
            NamedPipeServerStream pipe;
            TaskCompletionSource acknowledgement;
            lock (_sync)
            {
                pipe = _activePipe ?? throw new IOException("Overlay pipe is disconnected.");
                acknowledgement = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _acknowledgement = acknowledgement;
            }

            var expected = command switch
            {
                OverlayCommand.Show => OverlayState.Visible,
                OverlayCommand.Hide => OverlayState.Hidden,
                OverlayCommand.Shutdown => OverlayState.Hidden,
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Command, Command: command), _writeGate, token).ConfigureAwait(false);
            if (command == OverlayCommand.Shutdown) return true;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await acknowledgement.Task.WaitAsync(CommandTimeout, linked.Token).ConfigureAwait(false);
            return State == expected;
        }
        catch (TimeoutException) { return false; }
        catch (IOException) { return false; }
        catch (ObjectDisposedException) { return false; }
        catch (OperationCanceledException) when (token.IsCancellationRequested || _lifetime.IsCancellationRequested) { return false; }
        finally { _commandGate.Release(); }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = _pipeFactory();
                _serverReady.TrySetResult();
            }
            catch (Exception exception)
            {
                _serverReady.TrySetException(exception);
                return;
            }

            Interlocked.Exchange(ref _activePipe, pipe);
            try
            {
                await pipe.WaitForConnectionAsync(_lifetime.Token).ConfigureAwait(false);
                await ServeAsync(pipe).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception) when (!_lifetime.IsCancellationRequested) { }
            finally
            {
                Interlocked.Exchange(ref _activePipe, null)?.Dispose();
                lock (_sync)
                {
                    _readyState = false;
                    _ready = NewSignal();
                    _acknowledgement?.TrySetCanceled();
                    _acknowledgement = null;
                }
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ServeAsync(Stream pipe)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        using var writeGate = new SemaphoreSlim(1, 1);
        var hello = await OverlayWireCodec.ReadAsync(pipe, connection.Token).ConfigureAwait(false);
        if (hello.Kind != OverlayWireMessageKind.Handshake || hello.ProtocolVersion != OverlayTransportProtocol.CurrentVersion)
        {
            await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ProtocolError, Error: "Overlay protocol version mismatch."), writeGate, connection.Token).ConfigureAwait(false);
            return;
        }

        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.HandshakeAccepted), writeGate, connection.Token).ConfigureAwait(false);
        while (!connection.IsCancellationRequested)
        {
            var message = await OverlayWireCodec.ReadAsync(pipe, connection.Token).ConfigureAwait(false);
            if (message.ProtocolVersion != OverlayTransportProtocol.CurrentVersion)
                throw new FrontendProtocolException("Invalid Overlay state message.");

            if (message.Kind == OverlayWireMessageKind.DismissRequested && message.Command is null && message.State is null && message.Error is null)
            {
                DismissRequested?.Invoke(this);
                continue;
            }

            if (message.Kind != OverlayWireMessageKind.State || message.State is null)
                throw new FrontendProtocolException("Invalid Overlay state message.");

            lock (_sync)
            {
                if (message.State == OverlayState.Ready)
                {
                    _state = OverlayState.Hidden;
                    _readyState = true;
                    _ready.TrySetResult();
                }
                else
                {
                    _state = message.State.Value;
                }
                if (message.State is OverlayState.Visible or OverlayState.Hidden)
                    _acknowledgement?.TrySetResult();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        Interlocked.Exchange(ref _activePipe, null)?.Dispose();
        if (_acceptLoop is not null) try { await _acceptLoop.ConfigureAwait(false); } catch { }
        _commandGate.Dispose();
        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class NamedPipeOverlayClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private int _disposed;

    internal NamedPipeOverlayClient(string pipeName) => _pipeName = pipeName;

    internal async Task RunAsync(Func<OverlayCommand, Task> commandHandler, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        _pipe = pipe;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await pipe.ConnectAsync(5000, linked.Token).ConfigureAwait(false);
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), _writeGate, linked.Token).ConfigureAwait(false);
        var accepted = await OverlayWireCodec.ReadAsync(pipe, linked.Token).ConfigureAwait(false);
        if (accepted.Kind != OverlayWireMessageKind.HandshakeAccepted || accepted.ProtocolVersion != OverlayTransportProtocol.CurrentVersion)
            throw new FrontendProtocolException("Overlay handshake was rejected.");

        await SendStateAsync(pipe, OverlayState.Ready, linked.Token).ConfigureAwait(false);
        while (!linked.IsCancellationRequested)
        {
            var message = await OverlayWireCodec.ReadAsync(pipe, linked.Token).ConfigureAwait(false);
            if (message.ProtocolVersion != OverlayTransportProtocol.CurrentVersion || message.Kind != OverlayWireMessageKind.Command || message.Command is null)
                throw new FrontendProtocolException("Invalid Overlay command message.");
            await commandHandler(message.Command.Value).ConfigureAwait(false);
            if (message.Command == OverlayCommand.Show)
                await SendStateAsync(pipe, OverlayState.Visible, linked.Token).ConfigureAwait(false);
            else if (message.Command == OverlayCommand.Hide)
                await SendStateAsync(pipe, OverlayState.Hidden, linked.Token).ConfigureAwait(false);
            else
                return;
        }
    }

    private async Task SendStateAsync(Stream pipe, OverlayState state, CancellationToken token) =>
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: state), _writeGate, token).ConfigureAwait(false);

    internal async Task SendDismissRequestedAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var pipe = _pipe ?? throw new IOException("Overlay pipe is not connected.");
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.DismissRequested), _writeGate, token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _lifetime.Cancel();
            _pipe?.Dispose();
            _writeGate.Dispose();
            _lifetime.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
