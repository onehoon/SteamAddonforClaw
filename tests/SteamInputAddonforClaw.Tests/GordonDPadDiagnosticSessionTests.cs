using SteamInputAddonforClaw.Diagnostics.GordonDPad;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("GordonDPadDiagnosticHub")]
public sealed class GordonDPadDiagnosticSessionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GordonDPadDiagnosticSessionTests", Guid.NewGuid().ToString("N"));
    private readonly List<IAsyncDisposable> _sessions = [];

    public GordonDPadDiagnosticSessionTests() => GordonDPadDiagnosticHub.ResetForTests();

    public void Dispose()
    {
        foreach (var session in _sessions) session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        GordonDPadDiagnosticHub.ResetForTests();
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch (IOException) { }
    }

    private GordonDPadDiagnosticSession CreateSession(
        FakePathResolver? resolver = null,
        Func<string, int, IDirectHidReader>? readerFactory = null,
        IRawInputGordonObserver? rawInput = null,
        IReadOnlySet<string>? owned = null)
    {
        var session = new GordonDPadDiagnosticSession(
            resolver ?? new FakePathResolver(),
            readerFactory ?? ((_, _) => new FakeDirectHidReader(open: false)),
            rawInput ?? new FakeRawInputObserver(register: false),
            new FakeAncestryWalker(),
            () => owned ?? new HashSet<string>(),
            (directory, fileName) => new GordonDPadDiagnosticWriter(_directory, fileName));
        _sessions.Add(session);
        return session;
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public void Snapshot_InitialState_IsStopped()
    {
        var session = CreateSession();
        Assert.Equal(GordonDPadCaptureState.Stopped, session.Snapshot.CaptureState);
    }

    [Fact]
    public async Task Start_WithNoGordonPresent_TransitionsToWaitingForGordon()
    {
        var session = CreateSession(resolver: new FakePathResolver());
        session.Start(0);
        await WaitUntil(() => session.Snapshot.CaptureState != GordonDPadCaptureState.Stopped);

        Assert.Equal(GordonDPadCaptureState.WaitingForGordon, session.Snapshot.CaptureState);
        Assert.Equal(GordonConnectionState.NotAvailable, session.Snapshot.GordonState);
        await session.StopAsync();
    }

    [Fact]
    public async Task Start_TwiceIsIdempotent()
    {
        var session = CreateSession();
        session.Start(0);
        await WaitUntil(() => session.Snapshot.CaptureState != GordonDPadCaptureState.Stopped);
        var pathAfterFirstStart = session.Snapshot.DiagnosticFilePath;

        session.Start(0); // must be a no-op, not throw, not create a second file

        Assert.Equal(pathAfterFirstStart, session.Snapshot.DiagnosticFilePath);
        await session.StopAsync();
    }

    [Fact]
    public async Task StopAsync_TwiceDoesNotThrow()
    {
        var session = CreateSession();
        session.Start(0);
        await WaitUntil(() => session.Snapshot.CaptureState != GordonDPadCaptureState.Stopped);

        await session.StopAsync();
        var exception = await Record.ExceptionAsync(() => session.StopAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task Start_CreatesTheCaptureFileImmediately()
    {
        var session = CreateSession();
        session.Start(0);
        await WaitUntil(() => session.Snapshot.DiagnosticFilePath is not null);

        Assert.True(File.Exists(session.Snapshot.DiagnosticFilePath));
        await session.StopAsync();
    }

    [Fact]
    public async Task HubLine_UpdatesTheMatchingSnapshotField()
    {
        var session = CreateSession();
        session.Start(0);
        await WaitUntil(() => session.Snapshot.CaptureState != GordonDPadCaptureState.Stopped);

        GordonDPadDiagnosticHub.Publish("Stage=Physical Up=1 Right=0 Left=0 Down=0 Mask=0x01");
        GordonDPadDiagnosticHub.Publish("Stage=Canonical Up=0 Right=1 Left=0 Down=0 Mask=0x02");
        GordonDPadDiagnosticHub.Publish("Stage=ABIDecoded Up=0 Right=0 Left=1 Down=0 Mask=0x04");
        GordonDPadDiagnosticHub.Publish("Stage=GordonReport Byte9=0x08 DPadMask=0x08");

        await WaitUntil(() => session.Snapshot.LastGordonReport is not null);
        var snapshot = session.Snapshot;
        Assert.Contains("Up=1", snapshot.LastPhysical);
        Assert.Contains("Right=1", snapshot.LastCanonical);
        Assert.Contains("Left=1", snapshot.LastAbiDecoded);
        Assert.Contains("DPadMask=0x08", snapshot.LastGordonReport);
        await session.StopAsync();
    }

    [Fact]
    public async Task StopAsync_UnsubscribesFromTheHub()
    {
        var session = CreateSession();
        session.Start(0);
        await WaitUntil(() => session.Snapshot.CaptureState != GordonDPadCaptureState.Stopped);
        await session.StopAsync();

        // Must not throw, and must not be observable in a snapshot that's meaningless post-stop.
        var exception = Record.Exception(() => GordonDPadDiagnosticHub.Publish("Stage=Physical Up=1 Right=0 Left=0 Down=0 Mask=0x01"));
        Assert.Null(exception);
        Assert.Equal(GordonDPadCaptureState.Stopped, session.Snapshot.CaptureState);
    }

    [Fact]
    public async Task Ambiguous_MultipleCandidates_DoesNotAutoAttach()
    {
        var resolver = new FakePathResolver();
        resolver.Candidates =
        [
            new GordonHidCandidate(@"\\?\hid#a", @"HID\A", 1, 0x28DE, 0x1102, 0xFF00, 0x01, 64),
            new GordonHidCandidate(@"\\?\hid#b", @"HID\B", 2, 0x28DE, 0x1102, 0xFF00, 0x01, 64),
        ];
        var session = CreateSession(resolver: resolver);

        session.Start(0);
        await WaitUntil(() => session.Snapshot.WindowsHidMode == WindowsHidObservationMode.Ambiguous);

        Assert.Equal(GordonDPadCaptureState.WaitingForGordon, session.Snapshot.CaptureState);
        Assert.Equal(GordonConnectionState.NotAvailable, session.Snapshot.GordonState);
        await session.StopAsync();
    }

    [Fact]
    public async Task SingleCandidate_DirectHidSucceeds_AttachesViaDirectHid()
    {
        var resolver = new FakePathResolver();
        resolver.Candidates = [new GordonHidCandidate(@"\\?\hid#only", @"HID\ONLY", 1, 0x28DE, 0x1102, 0xFF00, 0x01, 64)];
        var fakeReader = new FakeDirectHidReader(open: true);
        var session = CreateSession(resolver: resolver, readerFactory: (_, _) => fakeReader);

        session.Start(0);
        await WaitUntil(() => session.Snapshot.WindowsHidMode == WindowsHidObservationMode.DirectHid);

        Assert.Equal(GordonDPadCaptureState.Active, session.Snapshot.CaptureState);
        Assert.Equal(GordonConnectionState.Connected, session.Snapshot.GordonState);
        await session.StopAsync();
    }

    [Fact]
    public async Task SingleCandidate_DirectHidFails_FallsBackToRawInput()
    {
        var resolver = new FakePathResolver();
        resolver.Candidates = [new GordonHidCandidate(@"\\?\hid#only", @"HID\ONLY", 1, 0x28DE, 0x1102, 0xFF00, 0x01, 64)];
        var rawInput = new FakeRawInputObserver(register: true);
        var session = CreateSession(resolver: resolver, readerFactory: (_, _) => new FakeDirectHidReader(open: false), rawInput: rawInput);

        session.Start(0);
        await WaitUntil(() => session.Snapshot.WindowsHidMode == WindowsHidObservationMode.RawInput);

        Assert.True(rawInput.Registered);
        Assert.Equal(GordonDPadCaptureState.Active, session.Snapshot.CaptureState);
        await session.StopAsync();
    }

    [Fact]
    public async Task SingleCandidate_DirectHidFails_RawInputRegistersWithTheSelectedDevicePath()
    {
        // Regression: the Raw Input fallback must be told exactly which candidate the selector chose, so
        // it can filter WM_INPUT to that specific device rather than accepting any Gordon-shaped
        // (matching VID/PID/usage) device -- otherwise a real Steam Controller or a stale Gordon node
        // sharing the same VID/PID/usage could contaminate the capture.
        var resolver = new FakePathResolver();
        var candidate = new GordonHidCandidate(@"\\?\hid#exact", @"HID\EXACT", 1, 0x28DE, 0x1102, 0xFF00, 0x01, 64);
        resolver.Candidates = [candidate];
        var rawInput = new FakeRawInputObserver(register: true);
        var session = CreateSession(resolver: resolver, readerFactory: (_, _) => new FakeDirectHidReader(open: false), rawInput: rawInput);

        session.Start(0);
        await WaitUntil(() => rawInput.Registered);

        Assert.Equal(candidate.DevicePath, rawInput.ExpectedDevicePath);
        await session.StopAsync();
    }

    [Fact]
    public async Task OverlappingReconcileTicksDoNotCreateDuplicateAttachesOrRegistrations()
    {
        // Regression: a slow/blocked OpenAsync spanning multiple 1s reconcile ticks must not cause a
        // later tick to start a second, overlapping attach for the same still-selected candidate -- that
        // could otherwise race DetachReaderAsync/RawInput register-unregister/WNDPROC-subclass calls
        // against each other.
        var resolver = new FakePathResolver();
        resolver.Candidates = [new GordonHidCandidate(@"\\?\hid#only", @"HID\ONLY", 1, 0x28DE, 0x1102, 0xFF00, 0x01, 64)];
        var openGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerCreatedCount = 0;
        IDirectHidReader Factory(string _, int _2)
        {
            Interlocked.Increment(ref readerCreatedCount);
            return new SlowOpenFakeDirectHidReader(openGate.Task);
        }
        var rawInput = new FakeRawInputObserver(register: true);
        var session = CreateSession(resolver: resolver, readerFactory: Factory, rawInput: rawInput);

        session.Start(0);
        // Let at least two 1-second reconciliation ticks fire while OpenAsync is still blocked.
        await Task.Delay(2500);
        openGate.SetResult(true);
        await WaitUntil(() => session.Snapshot.WindowsHidMode == WindowsHidObservationMode.DirectHid, TimeSpan.FromSeconds(3));
        await Task.Delay(200); // let the single attach fully settle before asserting

        Assert.Equal(1, readerCreatedCount);
        Assert.True(rawInput.RegisterCallCount <= 1, $"Expected at most one RawInput registration, got {rawInput.RegisterCallCount}.");
        await session.StopAsync();
    }

    [Fact]
    public async Task NeitherDirectHidNorRawInputAvailable_RemainsWaitingWithNotAvailableMode()
    {
        var resolver = new FakePathResolver();
        resolver.Candidates = [new GordonHidCandidate(@"\\?\hid#only", @"HID\ONLY", 1, 0x28DE, 0x1102, 0xFF00, 0x01, 64)];
        var session = CreateSession(resolver: resolver, readerFactory: (_, _) => new FakeDirectHidReader(open: false), rawInput: new FakeRawInputObserver(register: false));

        session.Start(0);
        // Give the async attach attempt a moment to complete and settle.
        await Task.Delay(200);

        Assert.Equal(WindowsHidObservationMode.NotAvailable, session.Snapshot.WindowsHidMode);
        await session.StopAsync();
    }

    [Fact]
    public async Task DirectHidReport_TransitionGatesOnDPadMaskChange()
    {
        var resolver = new FakePathResolver();
        resolver.Candidates = [new GordonHidCandidate(@"\\?\hid#only", @"HID\ONLY", 1, 0x28DE, 0x1102, 0xFF00, 0x01, 64)];
        var fakeReader = new FakeDirectHidReader(open: true);
        var session = CreateSession(resolver: resolver, readerFactory: (_, _) => fakeReader);

        session.Start(0);
        await WaitUntil(() => fakeReader.OnReport is not null);

        var pressed = NeutralReport();
        pressed[9] = 0x08;
        fakeReader.OnReport!(pressed);
        fakeReader.OnReport!(pressed); // repeated identical report must not be treated as a new transition
        await WaitUntil(() => session.Snapshot.LastWindowsHid is not null);
        var afterFirst = session.Snapshot.LastWindowsHid;

        var released = NeutralReport();
        fakeReader.OnReport!(released);
        await WaitUntil(() => session.Snapshot.LastWindowsHid != afterFirst);

        Assert.Contains("DPadMask=0x00", session.Snapshot.LastWindowsHid);
        await session.StopAsync();
    }

    private static byte[] NeutralReport()
    {
        var report = new byte[GordonHidReportParser.ExpectedLength];
        report[0] = GordonHidReportParser.ExpectedReportId;
        return report;
    }

    private sealed class FakePathResolver : IGordonHidDevicePathResolver
    {
        internal IReadOnlyList<GordonHidCandidate> Candidates { get; set; } = [];
        public IReadOnlyList<GordonHidCandidate> FindCandidates() => Candidates;
    }

    private sealed class FakeAncestryWalker : IDeviceAncestryWalker
    {
        public IReadOnlyList<string> GetAncestorInstanceIds(uint devInst, int maxDepth = 12) => [];
    }

    private sealed class FakeDirectHidReader(bool open) : IDirectHidReader
    {
        internal Action<byte[]>? OnReport;
        public bool IsOpen { get; private set; }

        public Task<bool> OpenAsync()
        {
            IsOpen = open;
            return Task.FromResult(open);
        }

        public Task RunAsync(Action<byte[]> onReport, Action<Exception> onFault, CancellationToken cancellationToken)
        {
            OnReport = onReport;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SlowOpenFakeDirectHidReader(Task<bool> openGate) : IDirectHidReader
    {
        public bool IsOpen { get; private set; }

        public async Task<bool> OpenAsync()
        {
            await openGate.ConfigureAwait(false);
            IsOpen = true;
            return true;
        }

        public Task RunAsync(Action<byte[]> onReport, Action<Exception> onFault, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRawInputObserver(bool register) : IRawInputGordonObserver
    {
        internal bool Registered { get; private set; }
        internal string? ExpectedDevicePath { get; private set; }
        internal int RegisterCallCount { get; private set; }
        public bool Register(nint ownerWindowHandle, string expectedDevicePath, Action<byte[]> onReport)
        {
            RegisterCallCount++;
            ExpectedDevicePath = expectedDevicePath;
            Registered = register;
            return register;
        }
        public void Unregister() => Registered = false;
        public void Dispose() => Unregister();
    }
}
