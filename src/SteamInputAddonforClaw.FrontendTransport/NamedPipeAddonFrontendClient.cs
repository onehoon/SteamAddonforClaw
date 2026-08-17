using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.FrontendTransport;

public sealed class NamedPipeAddonFrontendClient : IAddonFrontendControl, IAsyncDisposable
{
    private readonly string _pipeName; private readonly int _version; private readonly ConcurrentDictionary<long, TaskCompletionSource<FrontendWireEnvelope>> _pending = new(); private readonly ConcurrentDictionary<long, byte> _cancelled = new();
    private readonly CancellationTokenSource _lifetime = new(); private readonly SemaphoreSlim _writeGate = new(1, 1); private readonly object _connectionGate = new(); private NamedPipeClientStream? _pipe; private Exception? _disconnectReason; private Task? _readLoop; private long _nextRequestId; private int _disposed; private int _connecting;
    public event EventHandler? StateInvalidated;
    public NamedPipeAddonFrontendClient(string pipeName) : this(pipeName, FrontendTransportProtocol.CurrentVersion) { }
    internal NamedPipeAddonFrontendClient(string pipeName, int version) { _pipeName = pipeName; _version = version; }
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_pipe is not null) throw new InvalidOperationException("Client is already connected.");
        if (Interlocked.Exchange(ref _connecting, 1) != 0) throw new InvalidOperationException("Client connection attempt is already in progress.");
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false); await FrontendWireCodec.WriteAsync(pipe, new(_version, FrontendWireMessageKind.Handshake), _writeGate, timeout.Token).ConfigureAwait(false); var reply = await FrontendWireCodec.ReadAsync(pipe, timeout.Token).ConfigureAwait(false); if (reply.Kind == FrontendWireMessageKind.ProtocolError) throw new FrontendProtocolException(reply.Error?.Message ?? "Protocol rejected."); if (reply.Kind != FrontendWireMessageKind.HandshakeAccepted || reply.ProtocolVersion != _version) throw new FrontendProtocolException("Protocol handshake failed."); lock (_connectionGate) { ObjectDisposedException.ThrowIf(_disposed != 0, this); _pipe = pipe; _readLoop = ReadLoopAsync(); } }
        catch { await pipe.DisposeAsync().ConfigureAwait(false); throw; }
        finally { Volatile.Write(ref _connecting, 0); }
    }
    public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken t = default) => SendAsync<FrontendBootstrapSnapshot>(FrontendRpcMethod.GetBootstrap, null, t);
    public Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken t = default) => SendAsync<FrontendStatusSnapshot>(FrontendRpcMethod.CaptureStatus, null, t);
    public Task<FrontendLaunchAtStartupResult> SetLaunchAtWindowsStartupAsync(bool enabled, CancellationToken t = default) => SendAsync<FrontendLaunchAtStartupResult>(FrontendRpcMethod.SetLaunchAtWindowsStartup, FrontendWireCodec.Payload(new SetLaunchAtWindowsStartupRequest(enabled)), t);
    public Task<FrontendSettingsSnapshot> SetRouteInSteamBigPictureAsync(bool enabled, CancellationToken t = default) => SendAsync<FrontendSettingsSnapshot>(FrontendRpcMethod.SetRouteInSteamBigPicture, FrontendWireCodec.Payload(new SetRouteInSteamBigPictureRequest(enabled)), t);
    public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken t = default) => SendAsync<FrontendSettingsSnapshot>(FrontendRpcMethod.SetLogLevel, FrontendWireCodec.Payload(new SetLogLevelRequest(level)), t);
    public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken t = default) => SendAsync<FrontendSettingsSnapshot>(FrontendRpcMethod.SuppressDeveloperMenuWarning, null, t);
    public Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken t = default) => SendAsync<FrontendDeveloperSnapshot>(FrontendRpcMethod.SetDeveloperTestMode, FrontendWireCodec.Payload(new SetDeveloperTestModeRequest(enabled)), t);
    public Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken t = default) => SendAsync<FrontendPrerequisiteSetupResult>(FrontendRpcMethod.RunPrerequisiteSetup, null, t);
    public Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken t = default) => SendAsync<FrontendEnvironmentReportResult>(FrontendRpcMethod.GenerateEnvironmentReport, null, t);
    private async Task<T> SendAsync<T>(FrontendRpcMethod method, JsonElement? payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var pipe = _pipe ?? throw new FrontendTransportException("Client is not connected.", _disconnectReason); var id = Interlocked.Increment(ref _nextRequestId); var tcs = new TaskCompletionSource<FrontendWireEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously); if (!_pending.TryAdd(id, tcs)) throw new FrontendTransportException("Duplicate request id.");
        var requestWriteState = 0;
        using var registration = token.Register(() =>
        {
            if (Interlocked.CompareExchange(ref requestWriteState, 2, 0) == 0)
            {
                _pending.TryRemove(id, out _);
            }
            else
            {
                _cancelled.TryAdd(id, 0);
                _pending.TryRemove(id, out _);
                _ = SendCancelSafelyAsync(id);
            }
            tcs.TrySetCanceled(token);
        });
        try { await FrontendWireCodec.WriteAsync(pipe, new(_version, FrontendWireMessageKind.Request, id, method, Payload: payload), _writeGate, token, _lifetime.Token, () => { if (Interlocked.CompareExchange(ref requestWriteState, 1, 0) != 0) throw new OperationCanceledException(token); }).ConfigureAwait(false); var response = await tcs.Task.ConfigureAwait(false); if (response.Error is { } error) { if (error.Code == FrontendRemoteErrorCode.Cancelled) throw new OperationCanceledException(token); throw new FrontendRemoteException(error.Code, error.Message); } try { return FrontendWireCodec.Decode<T>(response.Payload); } catch (FrontendProtocolException exception) { MarkDisconnected(exception); throw; } }
        finally { _pending.TryRemove(id, out _); }
    }
    private async Task SendCancelSafelyAsync(long id) { try { if (_pipe is { } pipe) await FrontendWireCodec.WriteAsync(pipe, new(_version, FrontendWireMessageKind.CancelRequest, id), _writeGate, _lifetime.Token).ConfigureAwait(false); } catch { } }
    private async Task ReadLoopAsync()
    { try { while (!_lifetime.IsCancellationRequested && _pipe is { } pipe) { var message = await FrontendWireCodec.ReadAsync(pipe, _lifetime.Token).ConfigureAwait(false); if (message.ProtocolVersion != FrontendTransportProtocol.CurrentVersion) throw new FrontendProtocolException("Protocol version mismatch."); if (message.Kind == FrontendWireMessageKind.Notification && message.Notification == FrontendNotificationKind.StateInvalidated) StateInvalidated?.Invoke(this, EventArgs.Empty); else if (message.Kind == FrontendWireMessageKind.Response && message.RequestId is > 0 and var id) { if (_pending.TryGetValue(id, out var tcs)) tcs.TrySetResult(message); else if (!_cancelled.TryRemove(id, out _)) throw new FrontendProtocolException("Unexpected response correlation id."); } else throw new FrontendProtocolException("Unexpected wire message."); } } catch (Exception e) when (e is not OperationCanceledException) { MarkDisconnected(e); } }
    private void MarkDisconnected(Exception exception)
    {
        _disconnectReason = exception;
        Interlocked.Exchange(ref _pipe, null)?.Dispose();
        FailPending(new FrontendTransportException("Pipe connection closed.", exception));
    }
    private void FailPending(Exception e) { foreach (var item in _pending) item.Value.TrySetException(e); _pending.Clear(); _cancelled.Clear(); }
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; _lifetime.Cancel(); NamedPipeClientStream? pipe; lock (_connectionGate) pipe = Interlocked.Exchange(ref _pipe, null); pipe?.Dispose(); if (_readLoop is not null) try { await _readLoop.ConfigureAwait(false); } catch { } FailPending(new FrontendTransportException("Client disposed.")); _writeGate.Dispose(); _lifetime.Dispose(); }
}
