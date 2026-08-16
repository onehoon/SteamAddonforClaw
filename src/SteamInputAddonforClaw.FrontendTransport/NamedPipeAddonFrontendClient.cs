using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.FrontendTransport;

public sealed class NamedPipeAddonFrontendClient : IAddonFrontendControl, IAsyncDisposable
{
    private readonly string _pipeName; private readonly int _version; private readonly ConcurrentDictionary<long, TaskCompletionSource<FrontendWireEnvelope>> _pending = new();
    private readonly CancellationTokenSource _lifetime = new(); private readonly SemaphoreSlim _writeGate = new(1, 1); private NamedPipeClientStream? _pipe; private Task? _readLoop; private long _nextRequestId; private int _disposed;
    public event EventHandler? StateInvalidated;
    public NamedPipeAddonFrontendClient(string pipeName) : this(pipeName, FrontendTransportProtocol.CurrentVersion) { }
    internal NamedPipeAddonFrontendClient(string pipeName, int version) { _pipeName = pipeName; _version = version; }
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_pipe is not null) throw new InvalidOperationException("Client is already connected.");
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false); await FrontendWireCodec.WriteAsync(pipe, new(_version, FrontendWireMessageKind.Handshake), _writeGate, cancellationToken).ConfigureAwait(false); var reply = await FrontendWireCodec.ReadAsync(pipe, cancellationToken).ConfigureAwait(false); if (reply.Kind == FrontendWireMessageKind.ProtocolError) throw new FrontendProtocolException(reply.Error?.Message ?? "Protocol rejected."); if (reply.Kind != FrontendWireMessageKind.HandshakeAccepted || reply.ProtocolVersion != _version) throw new FrontendProtocolException("Protocol handshake failed."); _pipe = pipe; _readLoop = ReadLoopAsync(); }
        catch { await pipe.DisposeAsync().ConfigureAwait(false); throw; }
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
        token.ThrowIfCancellationRequested(); var pipe = _pipe ?? throw new FrontendTransportException("Client is not connected."); var id = Interlocked.Increment(ref _nextRequestId); var tcs = new TaskCompletionSource<FrontendWireEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously); if (!_pending.TryAdd(id, tcs)) throw new FrontendTransportException("Duplicate request id.");
        using var registration = token.Register(() => { _pending.TryRemove(id, out _); _ = SendCancelSafelyAsync(id); tcs.TrySetCanceled(token); });
        try { await FrontendWireCodec.WriteAsync(pipe, new(_version, FrontendWireMessageKind.Request, id, method, Payload: payload), _writeGate, token).ConfigureAwait(false); var response = await tcs.Task.ConfigureAwait(false); if (response.Error is { } error) { if (error.Code == FrontendRemoteErrorCode.Cancelled) throw new OperationCanceledException(token); throw new FrontendRemoteException(error.Code, error.Message); } return FrontendWireCodec.Decode<T>(response.Payload); }
        finally { _pending.TryRemove(id, out _); }
    }
    private async Task SendCancelSafelyAsync(long id) { try { if (_pipe is { } pipe) await FrontendWireCodec.WriteAsync(pipe, new(_version, FrontendWireMessageKind.CancelRequest, id), _writeGate, _lifetime.Token).ConfigureAwait(false); } catch { } }
    private async Task ReadLoopAsync()
    { try { while (!_lifetime.IsCancellationRequested && _pipe is { } pipe) { var message = await FrontendWireCodec.ReadAsync(pipe, _lifetime.Token).ConfigureAwait(false); if (message.Kind == FrontendWireMessageKind.Notification && message.Notification == FrontendNotificationKind.StateInvalidated) StateInvalidated?.Invoke(this, EventArgs.Empty); else if (message.Kind == FrontendWireMessageKind.Response && message.RequestId is > 0 and var id && _pending.TryGetValue(id, out var tcs)) tcs.TrySetResult(message); else throw new FrontendProtocolException("Unexpected wire message."); } } catch (Exception e) when (e is not OperationCanceledException) { FailPending(new FrontendTransportException("Pipe connection closed.", e)); } }
    private void FailPending(Exception e) { foreach (var item in _pending) item.Value.TrySetException(e); _pending.Clear(); }
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; _lifetime.Cancel(); _pipe?.Dispose(); if (_readLoop is not null) try { await _readLoop.ConfigureAwait(false); } catch { } FailPending(new FrontendTransportException("Client disposed.")); _writeGate.Dispose(); _lifetime.Dispose(); }
}
