using System.Reflection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Diagnostics.GordonDPad;

internal enum GordonDPadCaptureState { Stopped, WaitingForGordon, Active, GordonRemoved }
internal enum GordonConnectionState { NotAvailable, Connected }
internal enum NativeTraceState { NotAvailable, Active }
internal enum WindowsHidObservationMode { NotAvailable, DirectHid, RawInput, Ambiguous }

internal sealed record GordonDPadDiagnosticSnapshot(
    GordonDPadCaptureState CaptureState,
    GordonConnectionState GordonState,
    NativeTraceState NativeTraceState,
    WindowsHidObservationMode WindowsHidMode,
    string? LastPhysical,
    string? LastCanonical,
    string? LastAbiDecoded,
    string? LastGordonReport,
    string? LastWindowsHid,
    string? DiagnosticFilePath,
    string? StatusMessage);

/// <summary>
/// Coordinates one Gordon D-pad diagnostic capture session: subscribes to
/// <see cref="GordonDPadDiagnosticHub"/> (physical, canonical, and native VIIPER stages), resolves and
/// observes the Addon-owned Gordon's Windows HID reports (Direct HID, falling back to Raw Input), and
/// writes everything into one dedicated capture file. Observer-only: never mutates VIIPER/PnP/HidHide/
/// routing state, and a failure anywhere in this session degrades to a status flag, never to a routing or
/// device-lifecycle failure. Safe for Start/Stop from the UI thread while report callbacks arrive from a
/// background read task, a native-callback thread, and (for the Raw Input path) the owner window's
/// message loop.
/// </summary>
internal sealed class GordonDPadDiagnosticSession(
    IGordonHidDevicePathResolver pathResolver,
    Func<string, int, IDirectHidReader> directHidReaderFactory,
    IRawInputGordonObserver rawInputObserver,
    IDeviceAncestryWalker ancestryWalker,
    Func<IReadOnlySet<string>> ownedInstanceIdsProvider,
    Func<string, string, GordonDPadDiagnosticWriter>? writerFactory = null) : IAsyncDisposable
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(1);

    private readonly Lock _sync = new();
    private readonly Func<string, string, GordonDPadDiagnosticWriter> _writerFactory = writerFactory ?? ((directory, fileName) => new GordonDPadDiagnosticWriter(directory, fileName));

    private GordonDPadDiagnosticWriter? _writer;
    private Timer? _reconcileTimer;
    private int _reconcileInProgress;
    private nint _ownerWindowHandle;
    private Action<string>? _hubHandler;

    private GordonDPadCaptureState _captureState = GordonDPadCaptureState.Stopped;
    private GordonConnectionState _gordonState = GordonConnectionState.NotAvailable;
    private WindowsHidObservationMode _windowsHidMode = WindowsHidObservationMode.NotAvailable;
    private string? _statusMessage;
    private string? _lastPhysical;
    private string? _lastCanonical;
    private string? _lastAbiDecoded;
    private string? _lastGordonReport;
    private string? _lastWindowsHid;
    private string? _attachedDevicePath;

    private IDirectHidReader? _directHidReader;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private bool _windowsHidHasMask;
    private byte _lastWindowsHidMask;

    // Serializes every attach/detach transition (Direct HID open, Raw Input register/unregister, window
    // subclassing) onto a single chain, so a 1-second reconciliation tick can never start a new
    // attach/detach while a previous one from an earlier tick is still in flight -- without this, two
    // overlapping AttachAsync calls could race each other's DetachReaderAsync/RawInput
    // register-unregister/WNDPROC-subclass calls against each other. _attachingDevicePath additionally
    // dedups: a reconcile tick that keeps selecting the *same* candidate while an attach for it is
    // already queued/running does not queue a redundant second attach for it.
    private Task _pendingTransition = Task.CompletedTask;
    private string? _attachingDevicePath;

    private void QueueTransition(Func<Task> operation)
    {
        lock (_sync)
        {
            _pendingTransition = _pendingTransition.ContinueWith(_ => operation(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    internal GordonDPadDiagnosticSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new GordonDPadDiagnosticSnapshot(_captureState, _gordonState,
                    GordonDPadDiagnosticHub.HasSubscribers ? NativeTraceState.Active : NativeTraceState.NotAvailable,
                    _windowsHidMode, _lastPhysical, _lastCanonical, _lastAbiDecoded, _lastGordonReport, _lastWindowsHid,
                    _writer?.FilePath, _statusMessage);
            }
        }
    }

    /// <summary>Starts a capture session. Idempotent: a second call while already started is a no-op.
    /// <paramref name="ownerWindowHandle"/> is the existing WinUI main window's HWND, used only for the
    /// Raw Input fallback registration.</summary>
    internal void Start(nint ownerWindowHandle)
    {
        lock (_sync)
        {
            if (_captureState != GordonDPadCaptureState.Stopped) return;
            _ownerWindowHandle = ownerWindowHandle;

            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw", "diagnostics");
            var fileName = $"GordonDPadDiagnostic-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}.log";
            _writer = _writerFactory(directory, fileName);
            _writer.WriteHeader(BuildHeaderFields());

            _hubHandler = OnHubLine;
            GordonDPadDiagnosticHub.LineObserved += _hubHandler;

            _captureState = GordonDPadCaptureState.WaitingForGordon;
            _statusMessage = null;
        }

        ReconcileGordonAttachment();
        lock (_sync)
        {
            _reconcileTimer ??= new Timer(_ => ReconcileGordonAttachment(), null, ReconcileInterval, ReconcileInterval);
        }
    }

    /// <summary>Stops the session: cancels any in-flight HID read, unregisters Raw Input, unsubscribes
    /// from the diagnostic hub, and drains/closes the capture file. Idempotent and bounded -- never hangs
    /// application shutdown. Does not touch VIIPER, PnP, HidHide, or routing state.</summary>
    internal async Task StopAsync()
    {
        Timer? reconcileTimer;
        Action<string>? hubHandler;
        GordonDPadDiagnosticWriter? writer;
        Task pendingTransition;
        lock (_sync)
        {
            if (_captureState == GordonDPadCaptureState.Stopped) return;
            reconcileTimer = _reconcileTimer;
            _reconcileTimer = null;
            hubHandler = _hubHandler;
            _hubHandler = null;
            writer = _writer;
            _writer = null;
            pendingTransition = _pendingTransition;
            _attachingDevicePath = null;
            _captureState = GordonDPadCaptureState.Stopped;
            _gordonState = GordonConnectionState.NotAvailable;
            _windowsHidMode = WindowsHidObservationMode.NotAvailable;
            _windowsHidHasMask = false;
            _attachedDevicePath = null;
        }

        if (reconcileTimer is not null) await reconcileTimer.DisposeAsync().ConfigureAwait(false);
        if (hubHandler is not null) GordonDPadDiagnosticHub.LineObserved -= hubHandler;

        // Bounded wait for any attach/detach transition an earlier reconcile tick already queued -- it
        // may briefly (re)create a reader/registration after the state above was cleared, so the
        // unconditional detach/unregister below runs again afterward to mop that up.
        try { await pendingTransition.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch { /* bounded; never hang shutdown */ }

        await DetachReaderAsync().ConfigureAwait(false);
        rawInputObserver.Unregister();

        writer?.Stop();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private void OnHubLine(string line)
    {
        // Cheap, allocation-light stage dispatch: every hub line starts with "Stage=<Name> ...".
        string? stage = null;
        if (line.StartsWith("Stage=", StringComparison.Ordinal))
        {
            var spaceIndex = line.IndexOf(' ', 6);
            stage = spaceIndex > 6 ? line[6..spaceIndex] : line[6..];
        }

        lock (_sync)
        {
            if (_captureState == GordonDPadCaptureState.Stopped) return;
            switch (stage)
            {
                case "Physical": _lastPhysical = line; break;
                case "Canonical": _lastCanonical = line; break;
                case "ABIDecoded": _lastAbiDecoded = line; break;
                case "GordonReport": _lastGordonReport = line; break;
            }
            _writer?.WriteLine($"{DateTimeOffset.Now:O} {line}");
        }
    }

    private void ReconcileGordonAttachment()
    {
        if (Interlocked.Exchange(ref _reconcileInProgress, 1) != 0) return;
        try
        {
            lock (_sync)
            {
                if (_captureState == GordonDPadCaptureState.Stopped) return;
            }

            IReadOnlyList<GordonHidCandidate> candidates;
            try { candidates = pathResolver.FindCandidates(); }
            catch (Exception exception)
            {
                SetStatus("HID enumeration failed: " + exception.Message);
                return;
            }

            var owned = ownedInstanceIdsProvider();
            var selection = GordonHidCandidateSelector.Select(candidates, owned, candidate => ancestryWalker.GetAncestorInstanceIds(candidate.DevInst));

            switch (selection.Status)
            {
                case GordonHidSelectionStatus.NoneFound:
                    HandleNoGordonPresent();
                    return;
                case GordonHidSelectionStatus.Ambiguous:
                    HandleAmbiguous(selection);
                    return;
                case GordonHidSelectionStatus.Selected when selection.Selected is { } candidate:
                    HandleSelected(candidate, selection.OwnershipConfirmed);
                    return;
            }
        }
        finally
        {
            Volatile.Write(ref _reconcileInProgress, 0);
        }
    }

    private void HandleNoGordonPresent()
    {
        bool wasAttached;
        lock (_sync)
        {
            wasAttached = _attachedDevicePath is not null || _windowsHidMode != WindowsHidObservationMode.NotAvailable;
            _gordonState = GordonConnectionState.NotAvailable;
            _windowsHidMode = WindowsHidObservationMode.NotAvailable;
            _attachedDevicePath = null;
            _captureState = wasAttached ? GordonDPadCaptureState.GordonRemoved : GordonDPadCaptureState.WaitingForGordon;
        }
        if (wasAttached)
        {
            QueueTransition(async () =>
            {
                await DetachReaderAsync().ConfigureAwait(false);
                rawInputObserver.Unregister();
            });
            WriteStatusLine("GordonRemoved");
        }
    }

    private void HandleAmbiguous(GordonHidSelectionResult selection)
    {
        lock (_sync)
        {
            _gordonState = GordonConnectionState.NotAvailable;
            _windowsHidMode = WindowsHidObservationMode.Ambiguous;
            _captureState = GordonDPadCaptureState.WaitingForGordon;
            _statusMessage = $"Ambiguous: {selection.AllCandidates.Count} matching HID devices present; none correlate to the Addon-owned Gordon. Not auto-attaching.";
        }
        QueueTransition(async () =>
        {
            await DetachReaderAsync().ConfigureAwait(false);
            rawInputObserver.Unregister();
        });
        WriteStatusLine($"Ambiguous {selection.AllCandidates.Count} candidates, none correlated to owned Gordon");
    }

    private void HandleSelected(GordonHidCandidate candidate, bool ownershipConfirmed)
    {
        lock (_sync)
        {
            if (_attachedDevicePath == candidate.DevicePath && _windowsHidMode != WindowsHidObservationMode.NotAvailable)
            {
                _captureState = GordonDPadCaptureState.Active;
                _gordonState = GordonConnectionState.Connected;
                return; // already attached to this exact device -- nothing to do.
            }
            if (_attachingDevicePath == candidate.DevicePath) return; // an attach for this exact candidate is already queued/running.
            _attachingDevicePath = candidate.DevicePath;
        }

        QueueTransition(() => AttachAsync(candidate, ownershipConfirmed));
    }

    private async Task AttachAsync(GordonHidCandidate candidate, bool ownershipConfirmed)
    {
        try
        {
            await DetachReaderAsync().ConfigureAwait(false);
            rawInputObserver.Unregister();

            var reader = directHidReaderFactory(candidate.DevicePath, candidate.InputReportByteLength);
            var opened = false;
            try { opened = await reader.OpenAsync().ConfigureAwait(false); }
            catch { opened = false; }

            if (opened)
            {
                var cts = new CancellationTokenSource();
                lock (_sync)
                {
                    _directHidReader = reader;
                    _readerCts = cts;
                    _attachedDevicePath = candidate.DevicePath;
                    _windowsHidMode = WindowsHidObservationMode.DirectHid;
                    _gordonState = GordonConnectionState.Connected;
                    _captureState = GordonDPadCaptureState.Active;
                    _statusMessage = ownershipConfirmed ? null : "Attached to the only matching Gordon HID device present; ownership not cryptographically confirmed.";
                }
                _readerTask = reader.RunAsync(
                    report => HandleWindowsHidReport(report, "DirectHID"),
                    exception => SetStatus("Direct HID read failed: " + exception.Message),
                    cts.Token);
                WriteStatusLine($"Attached via DirectHID path={candidate.DevicePath} ownershipConfirmed={ownershipConfirmed}");
                return;
            }

            try { await reader.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }

            var registered = rawInputObserver.Register(_ownerWindowHandle, candidate.DevicePath, report => HandleWindowsHidReport(report, "RawInput"));
            lock (_sync)
            {
                _attachedDevicePath = candidate.DevicePath;
                _windowsHidMode = registered ? WindowsHidObservationMode.RawInput : WindowsHidObservationMode.NotAvailable;
                _gordonState = registered ? GordonConnectionState.Connected : GordonConnectionState.NotAvailable;
                _captureState = registered ? GordonDPadCaptureState.Active : GordonDPadCaptureState.WaitingForGordon;
                _statusMessage = registered
                    ? (ownershipConfirmed ? "Direct HID unavailable; using Raw Input fallback." : "Direct HID unavailable; using Raw Input fallback. Ownership not cryptographically confirmed.")
                    : "Neither Direct HID nor Raw Input could observe the Gordon device.";
            }
            WriteStatusLine(registered ? "Attached via RawInput fallback" : "Attach failed: neither DirectHID nor RawInput available");
        }
        finally
        {
            lock (_sync)
            {
                if (_attachingDevicePath == candidate.DevicePath) _attachingDevicePath = null;
            }
        }
    }

    private void HandleWindowsHidReport(byte[] report, string source)
    {
        var parsed = GordonHidReportParser.Parse(report);
        if (!parsed.Accepted) return;

        bool changed;
        lock (_sync)
        {
            if (_captureState == GordonDPadCaptureState.Stopped) return;
            changed = !_windowsHidHasMask || _lastWindowsHidMask != parsed.DPadMask;
            _windowsHidHasMask = true;
            _lastWindowsHidMask = parsed.DPadMask;
            if (!changed) return;
            var line = $"Stage=WindowsHID Source={source} Byte8=0x{parsed.Byte8:X2} Byte9=0x{parsed.Byte9:X2} Byte10=0x{parsed.Byte10:X2} DPadMask=0x{parsed.DPadMask:X2}";
            _lastWindowsHid = line;
            _writer?.WriteLine($"{DateTimeOffset.Now:O} {line}");
        }
    }

    private async Task DetachReaderAsync()
    {
        IDirectHidReader? reader;
        CancellationTokenSource? cts;
        Task? readerTask;
        lock (_sync)
        {
            reader = _directHidReader;
            cts = _readerCts;
            readerTask = _readerTask;
            _directHidReader = null;
            _readerCts = null;
            _readerTask = null;
        }
        if (cts is not null)
        {
            try { await cts.CancelAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        }
        if (readerTask is not null)
        {
            try { await readerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* bounded wait; never hang teardown */ }
        }
        if (reader is not null)
        {
            try { await reader.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        }
        cts?.Dispose();
    }

    private void SetStatus(string message)
    {
        lock (_sync) { _statusMessage = message; }
        WriteStatusLine(message);
    }

    private void WriteStatusLine(string message)
    {
        lock (_sync) { _writer?.WriteLine($"{DateTimeOffset.Now:O} Status={message}"); }
    }

    private List<(string Key, string Value)> BuildHeaderFields()
    {
        var payload = ViiperRuntimeInspector.ExpectedPayloadSha256;
        return
        [
            ("AddonVersion", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"),
            ("ProcessId", Environment.ProcessId.ToString()),
            ("LaunchId", AppLog.LaunchId),
            ("LoggingLevel", AppLog.MinimumLevelOverride.ToString()),
            ("ExpectedViiperPayloadSha256", payload),
        ];
    }
}
