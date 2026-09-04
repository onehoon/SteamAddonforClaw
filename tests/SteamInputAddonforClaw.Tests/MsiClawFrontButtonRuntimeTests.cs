using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// App UI PR-C: the feature-local front-button owner composes both physical-button paths (Gamebar /
/// Event88 and Center M / Event41) against one atomic front-button mapping and the supplied Full1902
/// presentation facts. It never touches the controller authority / presentation / HidHide lifecycle.
/// </summary>
public sealed class MsiClawFrontButtonRuntimeTests
{
    private static MsiClawFrontButtonRuntime Create(
        FrontButtonMappingSettings mapping,
        FakeEventSource oem1,
        FakeEventSource wing,
        bool steamDeckActive = false,
        Action? requestOverlayToggle = null,
        Func<bool>? quickAccessPulse = null,
        Func<bool>? steamPulse = null,
        Action? launchBigPicture = null,
        bool suppressionReady = true) =>
        MsiClawFrontButtonRuntime.Create(
            hardwareSupported: true,
            frontButtonMappingPreference: new FakePref(mapping),
            isSteamDeckPresentationActive: () => steamDeckActive,
            requestOverlayToggle: requestOverlayToggle ?? (() => { }),
            tryRequestQuickAccessPulse: quickAccessPulse ?? (() => false),
            tryRequestSteamPulse: steamPulse ?? (() => false),
            nativeWinGSuppressionReady: () => suppressionReady,
            oem1EventSourceOverride: oem1,
            wingEventSourceOverride: wing,
            oem1GestureDelay: new ImmediateDelay(),
            oem1GestureClock: new ZeroClock(),
            launchBigPictureOverride: launchBigPicture,
            wingGestureDelay: new ImmediateDelay());

    [Fact]
    public async Task Unsupported_hardware_wires_nothing()
    {
        var oem1 = new FakeEventSource();
        await using var runtime = MsiClawFrontButtonRuntime.Create(
            hardwareSupported: false,
            frontButtonMappingPreference: new FakePref(FrontButtonMappingSettings.Default),
            isSteamDeckPresentationActive: () => true,
            requestOverlayToggle: () => { },
            tryRequestQuickAccessPulse: () => true,
            tryRequestSteamPulse: () => true,
            oem1EventSourceOverride: oem1,
            wingEventSourceOverride: new FakeEventSource());

        Assert.False(oem1.StartCalled);
    }

    [Fact]
    public async Task Default_normal_domain_center_m_press_launches_big_picture_and_gamebar_press_toggles_overlay()
    {
        var overlay = 0;
        var bigPicture = 0;
        var oem1 = new FakeEventSource();
        var wing = new FakeEventSource();
        await using var runtime = Create(FrontButtonMappingSettings.Default, oem1, wing,
            requestOverlayToggle: () => overlay++, launchBigPicture: () => bigPicture++);

        Assert.True(oem1.StartCalled);

        oem1.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));
        Assert.Equal(1, bigPicture);   // Normal / Center M default = Steam Big Picture

        wing.Emit(new MsiOemEvent(88, CenterMOemCode.Oem2));
        Assert.Equal(1, overlay);      // Normal / Gamebar default = Quick Settings Overlay
    }

    [Fact]
    public async Task Steam_domain_defaults_route_to_the_system_button_pulse_seams()
    {
        var steamPulses = 0;
        var quickAccess = 0;
        var oem1 = new FakeEventSource();
        var wing = new FakeEventSource();
        await using var runtime = Create(FrontButtonMappingSettings.Default, oem1, wing,
            steamDeckActive: true,
            quickAccessPulse: () => { quickAccess++; return true; },
            steamPulse: () => { steamPulses++; return true; });

        wing.Emit(new MsiOemEvent(88, CenterMOemCode.Oem2));
        oem1.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));

        Assert.Equal(1, steamPulses);  // Steam / Gamebar default = Steam Button
        Assert.Equal(1, quickAccess);  // Steam / Center M default = Steam Quick Access
    }

    [Fact]
    public async Task Domain_follows_the_actual_steamdeck_presentation_not_raw_steam_demand()
    {
        var overlay = 0;
        var steamPulses = 0;
        var steamDeckActive = false;
        var oem1 = new FakeEventSource();
        var wing = new FakeEventSource();
        await using var runtime = MsiClawFrontButtonRuntime.Create(
            hardwareSupported: true,
            frontButtonMappingPreference: new FakePref(FrontButtonMappingSettings.Default),
            isSteamDeckPresentationActive: () => steamDeckActive,
            requestOverlayToggle: () => overlay++,
            tryRequestQuickAccessPulse: () => false,
            tryRequestSteamPulse: () => { steamPulses++; return true; },
            nativeWinGSuppressionReady: () => true,
            oem1EventSourceOverride: oem1,
            wingEventSourceOverride: wing,
            oem1GestureDelay: new ImmediateDelay(),
            oem1GestureClock: new ZeroClock(),
            wingGestureDelay: new ImmediateDelay());

        wing.Emit(new MsiOemEvent(88, CenterMOemCode.Oem2));
        Assert.Equal(1, overlay);
        Assert.Equal(0, steamPulses);

        steamDeckActive = true;
        wing.Emit(new MsiOemEvent(88, CenterMOemCode.Oem2));
        Assert.Equal(1, overlay);
        Assert.Equal(1, steamPulses);
    }

    [Fact]
    public async Task Gamebar_delivery_is_gated_off_while_native_wing_suppression_is_not_armed()
    {
        var overlay = 0;
        var wing = new FakeEventSource();
        await using var runtime = Create(FrontButtonMappingSettings.Default, new FakeEventSource(), wing,
            requestOverlayToggle: () => overlay++, suppressionReady: false);

        wing.Emit(new MsiOemEvent(88, CenterMOemCode.Oem2));

        Assert.Equal(0, overlay);
    }

    [Fact]
    public async Task A_single_press_resolves_immediately_with_no_double_click_delay()
    {
        // The recognizers are wired with double-click disabled: one Event41 delivers one action with
        // no held/heavy delay object involved at all.
        var overlay = 0;
        var oem1 = new FakeEventSource();
        var mapping = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.CenterM, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay));
        await using var runtime = MsiClawFrontButtonRuntime.Create(
            hardwareSupported: true,
            frontButtonMappingPreference: new FakePref(mapping),
            isSteamDeckPresentationActive: () => false,
            requestOverlayToggle: () => overlay++,
            tryRequestQuickAccessPulse: () => false,
            tryRequestSteamPulse: () => false,
            oem1EventSourceOverride: oem1,
            wingEventSourceOverride: new FakeEventSource(),
            oem1GestureDelay: new NeverDelay(),
            oem1GestureClock: new ZeroClock());

        oem1.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));

        Assert.Equal(1, overlay);
    }

    [Fact]
    public async Task Disposal_stops_the_event_sources_and_revokes_wing_authority()
    {
        var oem1 = new FakeEventSource();
        var wing = new FakeEventSource();
        var runtime = Create(FrontButtonMappingSettings.Default, oem1, wing);

        Assert.True(runtime.CaptureWingAuthority().Active);
        var epochBefore = runtime.CaptureWingAuthority().Epoch;

        await runtime.DisposeAsync();

        Assert.True(oem1.DisposeCalled);
        Assert.True(wing.DisposeCalled);
        var after = runtime.CaptureWingAuthority();
        Assert.False(after.Active);
        Assert.True(after.Epoch > epochBefore);
    }

    [Fact]
    public void Front_button_runtime_has_no_legacy_routing_or_deck_output_stage_dependency()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName,
            "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs"));

        foreach (var forbidden in new[]
        {
            "AddonRoutingRuntime", "WinGProtectionRoutingStage", "CanonicalSteamDeckOutputStage",
            "RoutingRuntimeStatusSnapshot", "RoutingPipeline", "HandheldRoutingComposition",
        })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_composes_the_front_button_owner_after_attach_and_disposes_it_first()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var host = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

        Assert.True(
            host.IndexOf("AttachInitialAsync(source, snapshot", StringComparison.Ordinal)
            < host.IndexOf("MsiClawFrontButtonRuntime.Create(", StringComparison.Ordinal));
        Assert.True(
            host.IndexOf("_frontButtonRuntime.DisposeAsync", StringComparison.Ordinal)
            < host.IndexOf("_presentationOwnership.DisposeAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_binds_wing_authority_to_the_wing_suppression_guard_armed_fact()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var host = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
        var runtime = File.ReadAllText(Path.Combine(dir!.FullName,
            "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs"));

        Assert.Contains("nativeWinGSuppressionReady: () => _winGSuppressionGuard.IsArmed", host, StringComparison.Ordinal);
        Assert.Contains("requestOverlayToggle: RequestOverlayToggle", host, StringComparison.Ordinal);
        // The stale "production always false" comment must be gone.
        Assert.DoesNotContain("currently always", runtime, StringComparison.Ordinal);
    }

    private sealed class FakeEventSource : IMsiEventSource
    {
        public event Action<MsiOemEvent>? EventReceived;
        internal bool StartCalled { get; private set; }
        internal bool DisposeCalled { get; private set; }
        public bool Start() { StartCalled = true; return true; }
        internal void Emit(MsiOemEvent value) => EventReceived?.Invoke(value);
        public void Dispose() => DisposeCalled = true;
    }

    private sealed class ImmediateDelay : IOem1GestureDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NeverDelay : IOem1GestureDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => new TaskCompletionSource().Task;
    }

    private sealed class ZeroClock : IOem1GestureClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) => TimeSpan.Zero;
    }

    private sealed class FakePref(FrontButtonMappingSettings initial) : Settings.IFrontButtonMappingPreference
    {
        public FrontButtonMappingSettings FrontButtonMapping { get; } = initial;
        public event EventHandler? FrontButtonMappingChanged { add { } remove { } }
    }
}
