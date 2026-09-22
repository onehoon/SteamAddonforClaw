using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;

namespace SteamInputAddonforClaw.ClawHud;

internal interface IClawHudControlClient
{
    Task<ClawHudControlResult<ClawHudRuntimeInfo>> GetRuntimeInfoAsync(CancellationToken cancellationToken = default);
    Task<ClawHudControlResult<ClawHudSettingsSnapshot>> GetSettingsSnapshotAsync(CancellationToken cancellationToken = default);
    Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudVisibilityModeAsync(ClawHudWireVisibilityMode mode, CancellationToken cancellationToken = default);
    Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudSizeOffsetAsync(int offset, CancellationToken cancellationToken = default);
    Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudFontAsync(ClawHudWireFont font, CancellationToken cancellationToken = default);
    Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudAlignmentAsync(ClawHudWireAlignment alignment, CancellationToken cancellationToken = default);
    Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudBackgroundModeAsync(ClawHudWireBackgroundMode mode, CancellationToken cancellationToken = default);
    Task<ClawHudControlResult<ClawHudSettingsSnapshot>> PreviewHudOpacityAsync(ushort opacityPercent, CancellationToken cancellationToken = default);
    Task<ClawHudControlResult<ClawHudSettingsSnapshot>> CommitHudOpacityAsync(ushort opacityPercent, CancellationToken cancellationToken = default);
    Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetIntelVrrRangeFixEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<ClawHudControlResult<ClawHudUnit>> RequestShutdownAsync(CancellationToken cancellationToken = default);
}

internal sealed class ClawHudControlClient : IClawHudControlClient
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
    private readonly string _pipeName;
    private readonly TimeSpan _timeout;
    private uint _nextRequestId;

    internal ClawHudControlClient(TimeSpan? timeout = null, string? pipeName = null)
    {
        _timeout = timeout ?? DefaultTimeout;
        _pipeName = pipeName ?? $"ClawHUD.Control.{Process.GetCurrentProcess().SessionId}";
    }

    public Task<ClawHudControlResult<ClawHudRuntimeInfo>> GetRuntimeInfoAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.GetRuntimeInfo, id => new(ClawHudControlOperation.GetRuntimeInfo, id), r => r.RuntimeInfo, cancellationToken);

    public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> GetSettingsSnapshotAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.GetSettingsSnapshot, id => new(ClawHudControlOperation.GetSettingsSnapshot, id), r => r.Snapshot, cancellationToken);

    public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.SetHudEnabled, id => new(ClawHudControlOperation.SetHudEnabled, id, Flag: enabled), r => r.Snapshot, cancellationToken);

    public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudVisibilityModeAsync(ClawHudWireVisibilityMode mode, CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.SetHudVisibilityMode, id => new(ClawHudControlOperation.SetHudVisibilityMode, id, WireEnum: (byte)mode), r => r.Snapshot, cancellationToken);

    public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudSizeOffsetAsync(int offset, CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.SetHudSizeOffset, id => new(ClawHudControlOperation.SetHudSizeOffset, id, SizeOffset: offset), r => r.Snapshot, cancellationToken);

    public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudFontAsync(ClawHudWireFont font, CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.SetHudFont, id => new(ClawHudControlOperation.SetHudFont, id, WireEnum: (byte)font), r => r.Snapshot, cancellationToken);

    public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudAlignmentAsync(ClawHudWireAlignment alignment, CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.SetHudAlignment, id => new(ClawHudControlOperation.SetHudAlignment, id, WireEnum: (byte)alignment), r => r.Snapshot, cancellationToken);

    public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudBackgroundModeAsync(ClawHudWireBackgroundMode mode, CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.SetHudBackgroundMode, id => new(ClawHudControlOperation.SetHudBackgroundMode, id, WireEnum: (byte)mode), r => r.Snapshot, cancellationToken);

    public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> PreviewHudOpacityAsync(ushort opacityPercent, CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.PreviewHudOpacity, id => new(ClawHudControlOperation.PreviewHudOpacity, id, OpacityPercent: opacityPercent), r => r.Snapshot, cancellationToken);

    public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> CommitHudOpacityAsync(ushort opacityPercent, CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.CommitHudOpacity, id => new(ClawHudControlOperation.CommitHudOpacity, id, OpacityPercent: opacityPercent), r => r.Snapshot, cancellationToken);

    public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetIntelVrrRangeFixEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.SetIntelVrrRangeFixEnabled, id => new(ClawHudControlOperation.SetIntelVrrRangeFixEnabled, id, Flag: enabled), r => r.Snapshot, cancellationToken);

    public Task<ClawHudControlResult<ClawHudUnit>> RequestShutdownAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(ClawHudControlOperation.RequestShutdown, id => new(ClawHudControlOperation.RequestShutdown, id), r => r.EmptySuccess ? new ClawHudUnit() : null, cancellationToken);

    private uint NextRequestId()
    {
        var id = unchecked((uint)Interlocked.Increment(ref _nextRequestId));
        if (id != 0) return id;
        return unchecked((uint)Interlocked.Increment(ref _nextRequestId));
    }

    private async Task<ClawHudControlResult<T>> ExecuteAsync<T>(
        ClawHudControlOperation operation,
        Func<uint, ClawHudControlRequest> requestFactory,
        Func<ClawHudControlDecodeResult, T?> valueSelector,
        CancellationToken cancellationToken)
        where T : class
    {
        var requestId = NextRequestId();
        var request = ClawHudControlCodec.EncodeRequest(requestFactory(requestId));
        using var timeoutSource = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(linked.Token).ConfigureAwait(false);
            pipe.ReadMode = PipeTransmissionMode.Message;
            await pipe.WriteAsync(request, linked.Token).ConfigureAwait(false);
            await pipe.FlushAsync(linked.Token).ConfigureAwait(false);

            var buffer = new byte[ClawHudControlProtocol.MaxFrameBytes];
            var total = 0;
            do
            {
                var read = await pipe.ReadAsync(buffer.AsMemory(total), linked.Token).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
            }
            while (!pipe.IsMessageComplete && total < buffer.Length);

            if (total == 0) return ClawHudControlResult<T>.Transport;
            if (!pipe.IsMessageComplete) return ClawHudControlResult<T>.Malformed;
            var decoded = ClawHudControlCodec.DecodeResponse(buffer.AsSpan(0, total), requestId, operation);
            return decoded.Outcome switch
            {
                ClawHudControlDecodeOutcome.Success when valueSelector(decoded) is { } value => ClawHudControlResult<T>.Success(value),
                ClawHudControlDecodeOutcome.Success => ClawHudControlResult<T>.Malformed,
                ClawHudControlDecodeOutcome.ProtocolError => ClawHudControlResult<T>.Protocol(decoded.Status),
                _ => ClawHudControlResult<T>.Malformed,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ClawHudControlResult<T>.Timeout;
        }
        catch (TimeoutException)
        {
            return ClawHudControlResult<T>.Timeout;
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or UnauthorizedAccessException)
        {
            return ClawHudControlResult<T>.Transport;
        }
    }
}
