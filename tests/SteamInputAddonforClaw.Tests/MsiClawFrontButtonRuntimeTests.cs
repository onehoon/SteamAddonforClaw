using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Contracts.Wing;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Full1902 A2 section 15.3 / 15.5 / 15.6: the feature-local OEM1/WING front-button owner is
/// composable and functional with <c>routingRuntime == null</c>. It never touches
/// <c>AddonRoutingRuntime</c>, <c>WinGProtectionRoutingStage</c>, or
/// <c>CanonicalSteamDeckOutputStage</c>; the OEM1 Normal/Routing mapping domain and the OEM1/WING
/// Steam-pulse actions are all resolved against the supplied Full1902 presentation facts.
/// </summary>
public sealed class MsiClawFrontButtonRuntimeTests
{
    // ---- 15.3: the feature path exists independently of the legacy routing owner ----

    [Fact]
    public async Task Supported_hardware_configures_the_oem1_action_path_without_a_routing_runtime()
    {
        var bigPicture = 0;
        var oem1 = new FakeEventSource();
        await using var runtime = MsiClawFrontButtonRuntime.Create(
            hardwareSupported: true,
            oem1MappingPreference: new FakeOem1Pref(Oem1MappingSettings.Default),
            wingMappingPreference: new FakeWingPref(WingMappingSettings.Default),
            isSteamDeckPresentationActive: () => false,
            tryRequestQuickAccessPulse: () => false,
            tryRequestSteamPulse: () => false,
            oem1EventSourceOverride: oem1,
            wingEventSourceOverride: new FakeEventSource(),
            oem1GestureDelay: new ImmediateDelay(),
            oem1GestureClock: new ZeroClock(),
            launchBigPictureOverride: () => bigPicture++);

        Assert.True(oem1.StartCalled);
        oem1.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));

        Assert.Equal(1, bigPicture); // Normal-domain single -> SteamBigPicture (default)
    }

    [Fact]
    public async Task Unsupported_hardware_wires_nothing()
    {
        var oem1 = new FakeEventSource();
        await using var runtime = MsiClawFrontButtonRuntime.Create(
            hardwareSupported: false,
            oem1MappingPreference: new FakeOem1Pref(Oem1MappingSettings.Default),
            wingMappingPreference: new FakeWingPref(WingMappingSettings.Default),
            isSteamDeckPresentationActive: () => true,
            tryRequestQuickAccessPulse: () => true,
            tryRequestSteamPulse: () => true,
            oem1EventSourceOverride: oem1,
            wingEventSourceOverride: new FakeEventSource());

        Assert.False(oem1.StartCalled);
    }

    // ---- 15.5: OEM1 mapping domain follows the actual Full1902 presentation ----

    [Fact]
    public async Task Oem1_domain_is_routing_only_while_the_steamdeck_presentation_is_active()
    {
        var quickAccess = 0;
        var bigPicture = 0;
        var steamDeckActive = false;
        var oem1 = new FakeEventSource();
        await using var runtime = MsiClawFrontButtonRuntime.Create(
            hardwareSupported: true,
            oem1MappingPreference: new FakeOem1Pref(Oem1MappingSettings.Default), // NormalSingle=BigPicture, RoutingSingle=QuickAccess
            wingMappingPreference: new FakeWingPref(WingMappingSettings.Default),
            isSteamDeckPresentationActive: () => steamDeckActive,
            tryRequestQuickAccessPulse: () => { quickAccess++; return true; },
            tryRequestSteamPulse: () => false,
            oem1EventSourceOverride: oem1,
            wingEventSourceOverride: new FakeEventSource(),
            oem1GestureDelay: new ImmediateDelay(),
            oem1GestureClock: new ZeroClock(),
            launchBigPictureOverride: () => bigPicture++);

        oem1.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));
        Assert.Equal(1, bigPicture);
        Assert.Equal(0, quickAccess);

        steamDeckActive = true;
        oem1.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));
        Assert.Equal(1, bigPicture);
        Assert.Equal(1, quickAccess); // Routing-domain single -> SteamQuickAccess pulse seam
    }

    // ---- 15.6: WING SteamButton delegates to the Full1902 presentation pulse seam ----

    [Fact]
    public async Task Wing_steam_button_uses_the_presentation_pulse_seam_when_authority_is_active()
    {
        var steamPulses = 0;
        var wing = new FakeEventSource();
        await using var runtime = MsiClawFrontButtonRuntime.Create(
            hardwareSupported: true,
            oem1MappingPreference: new FakeOem1Pref(Oem1MappingSettings.Default),
            wingMappingPreference: new FakeWingPref(WingMappingSettings.Default with { Double = WingSlotBinding.Of(WingAction.SteamButton) }),
            isSteamDeckPresentationActive: () => true,
            tryRequestQuickAccessPulse: () => false,
            tryRequestSteamPulse: () => { steamPulses++; return true; },
            // Section 9.2: production leaves this false until Policy B; a test forces authority on.
            nativeWinGSuppressionReady: () => true,
            oem1EventSourceOverride: new FakeEventSource(),
            wingEventSourceOverride: wing);

        // Two Event88 within the double window -> immediate Double delivery -> SteamButton.
        wing.Emit(new MsiOemEvent(88, CenterMOemCode.Oem2));
        wing.Emit(new MsiOemEvent(88, CenterMOemCode.Oem2));

        Assert.Equal(1, steamPulses);
    }

    [Fact]
    public async Task Wing_delivery_is_gated_off_while_native_wing_suppression_is_not_ready()
    {
        var steamPulses = 0;
        var wing = new FakeEventSource();
        await using var runtime = MsiClawFrontButtonRuntime.Create(
            hardwareSupported: true,
            oem1MappingPreference: new FakeOem1Pref(Oem1MappingSettings.Default),
            wingMappingPreference: new FakeWingPref(WingMappingSettings.Default with { Double = WingSlotBinding.Of(WingAction.SteamButton) }),
            isSteamDeckPresentationActive: () => true,
            tryRequestQuickAccessPulse: () => false,
            tryRequestSteamPulse: () => { steamPulses++; return true; },
            nativeWinGSuppressionReady: () => false, // production default until Policy B
            oem1EventSourceOverride: new FakeEventSource(),
            wingEventSourceOverride: wing);

        wing.Emit(new MsiOemEvent(88, CenterMOemCode.Oem2));
        wing.Emit(new MsiOemEvent(88, CenterMOemCode.Oem2));

        Assert.Equal(0, steamPulses); // no unsafe interim double-action before Policy B
    }

    [Fact]
    public async Task Disposal_stops_the_event_sources_and_revokes_wing_authority()
    {
        var oem1 = new FakeEventSource();
        var wing = new FakeEventSource();
        var runtime = MsiClawFrontButtonRuntime.Create(
            hardwareSupported: true,
            oem1MappingPreference: new FakeOem1Pref(Oem1MappingSettings.Default),
            wingMappingPreference: new FakeWingPref(WingMappingSettings.Default),
            isSteamDeckPresentationActive: () => false,
            tryRequestQuickAccessPulse: () => false,
            tryRequestSteamPulse: () => false,
            nativeWinGSuppressionReady: () => true,
            oem1EventSourceOverride: oem1,
            wingEventSourceOverride: wing);

        Assert.True(runtime.CaptureWingAuthority().Active);
        var epochBefore = runtime.CaptureWingAuthority().Epoch;

        await runtime.DisposeAsync();

        Assert.True(oem1.DisposeCalled);
        Assert.True(wing.DisposeCalled);
        var after = runtime.CaptureWingAuthority();
        Assert.False(after.Active);
        Assert.True(after.Epoch > epochBefore);
    }

    // ---- 15.6 / 16: no legacy routing dependency in the front-button path ----

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
            "CanonicalViiperRuntime", "MsiClawNativeState", "CenterMOem1LifecycleCoordinator", "CenterMHelperOwnership",
        })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    [Fact] // Section 7/14: the host composes the front-button owner only on a Disabled boot, after
           // the presentation attach, and tears it down before the presentation owner it targets.
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

    // ---- fakes ----

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

    private sealed class ZeroClock : IOem1GestureClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) => TimeSpan.Zero;
    }

    private sealed class FakeOem1Pref(Oem1MappingSettings initial) : Settings.IOem1MappingPreference
    {
        public Oem1MappingSettings Oem1Mapping { get; } = initial;
        public event EventHandler? Oem1MappingChanged { add { } remove { } }
    }

    private sealed class FakeWingPref(WingMappingSettings initial) : Settings.IWingMappingPreference
    {
        public WingMappingSettings WingMapping { get; } = initial;
        public event EventHandler? WingMappingChanged { add { } remove { } }
    }
}
