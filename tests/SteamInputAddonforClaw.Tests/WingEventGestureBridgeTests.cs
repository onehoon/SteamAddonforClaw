using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.GameBar;
using SteamInputAddonforClaw.Wing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WingEventGestureBridgeTests
{
    [Fact]
    public void Foreground_probe_captures_boundary_identity_before_queue_and_uses_it_as_first_snapshot()
    {
        var source = ProbeSource();
        var timestamp = source.IndexOf("var triggerTimestamp = Stopwatch.GetTimestamp();", StringComparison.Ordinal);
        var capture = source.IndexOf("initialIdentity = CaptureIdentity();", StringComparison.Ordinal);
        var queue = source.IndexOf("Task.Run(() => ObserveAsync(probeId, triggerTimestamp, initialIdentity))", StringComparison.Ordinal);
        var observe = source.IndexOf("private static async Task ObserveAsync(", StringComparison.Ordinal);
        var initialSnapshot = source.IndexOf("var current = CaptureSnapshot(initialIdentity);", observe, StringComparison.Ordinal);
        var nextSample = source.IndexOf("var identity = CaptureIdentity();", initialSnapshot, StringComparison.Ordinal);

        Assert.True(timestamp >= 0 && timestamp < capture, "the trigger timestamp must be captured before the boundary identity");
        Assert.True(capture < queue, "the HWND/PID boundary sample must happen before queuing background work");
        Assert.True(queue < observe && observe < initialSnapshot && initialSnapshot < nextSample,
            "the captured boundary identity must seed the first snapshot before later foreground samples");
    }

    [Fact]
    public void Accepted_event88_starts_diagnostic_and_still_delivers_gesture_when_diagnostic_throws()
    {
        using var source = new FakeMsiEventSource();
        using var recognizer = new WingGestureRecognizer(() => false);
        var deliveries = new List<WingGestureDelivery>();
        var diagnosticCalls = 0;
        var dispatcher = CreateDispatcher();
        using var bridge = new WingEventGestureBridge(
            source,
            recognizer,
            () => new(true, 17),
            dispatcher,
            () =>
            {
                diagnosticCalls++;
                Assert.Empty(deliveries);
                throw new InvalidOperationException("diagnostic failure");
            });
        recognizer.GestureRecognized += deliveries.Add;

        source.Emit(CenterMOemCode.Oem2);

        Assert.Equal(1, diagnosticCalls);
        Assert.Equal([new WingGestureDelivery(WingGesture.Single, 17)], deliveries);
    }

    [Fact]
    public void Center_m_event_does_not_start_wing_diagnostic()
    {
        using var source = new FakeMsiEventSource();
        using var recognizer = new WingGestureRecognizer(() => false);
        var diagnosticCalls = 0;
        using var bridge = new WingEventGestureBridge(
            source,
            recognizer,
            () => new(true, 18),
            CreateDispatcher(),
            () => diagnosticCalls++);

        source.Emit(CenterMOemCode.Oem1);

        Assert.Equal(0, diagnosticCalls);
    }

    [Fact]
    public void Foreground_identity_changes_only_when_hwnd_or_pid_changes()
    {
        var identity = new WingGameBarForegroundIdentity(new(0x1234), 42);

        Assert.Equal(identity, new WingGameBarForegroundIdentity(new(0x1234), 42));
        Assert.NotEqual(identity, new WingGameBarForegroundIdentity(new(0x5678), 42));
        Assert.NotEqual(identity, new WingGameBarForegroundIdentity(new(0x1234), 43));
    }

    private static WingActionDispatcher CreateDispatcher() => new(
        () => FrontButtonMappingSettings.Default,
        () => false,
        new FrontButtonActionExecutor(() => { }, () => { }, () => true, () => true));

    private static string ProbeSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "SteamInputAddonforClaw", "GameBar", "WingGameBarDiagnosticProbe.cs"));
    }

    private sealed class FakeMsiEventSource : IMsiEventSource
    {
        public event Action<MsiOemEvent>? EventReceived;

        public bool Start() => true;

        public void Emit(CenterMOemCode code) => EventReceived?.Invoke(new(0, code));

        public void Dispose() { }
    }
}
