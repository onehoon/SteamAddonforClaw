using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class Oem1ActionDispatcherTests
{
    private static RoutingRuntimeStatusSnapshot StatusWithSteamOutput(bool active, bool available = true) =>
        new(Available: available, OperationalState: RoutingOperationalState.OverrideActive, SteamOutputActive: active, NativeDirectInputActive: false);

    private static Oem1ActionDispatcher CreateDispatcher(
        Func<RoutingRuntimeStatusSnapshot> captureRoutingStatus,
        Action? requestQuickAccessPulse = null,
        Action? launchBigPicture = null,
        Oem1MappingSettings? mapping = null,
        Func<Oem1MappingSettings>? captureMapping = null,
        Action<Oem1HotkeyBinding>? sendHotkey = null,
        Action<Oem1LaunchApplicationBinding>? launchApplication = null) =>
        new(
            captureMapping ?? (() => mapping ?? Oem1MappingSettings.Default),
            captureRoutingStatus,
            requestQuickAccessPulse ?? (() => { }),
            launchBigPicture ?? (() => { }),
            // Test seams: neither OS-level key injection nor a real process launch ever happens here.
            sendHotkey ?? (_ => { }),
            launchApplication ?? (_ => { }));

    // ---- Locked defaults ----

    [Fact]
    public void Defaults_are_remapping_on_bigpicture_quickaccess_and_no_double_press_actions()
    {
        var defaults = Oem1MappingSettings.Default;

        Assert.True(defaults.RemappingEnabled);
        Assert.Equal(Oem1Action.SteamBigPicture, defaults.NormalSingle.Action);
        Assert.Equal(Oem1Action.None, defaults.NormalDouble.Action);
        Assert.Equal(Oem1Action.SteamQuickAccess, defaults.RoutingSingle.Action);
        Assert.Equal(Oem1Action.None, defaults.RoutingDouble.Action);
    }

    // ---- Domain selection: ONLY SteamOutputActive ----

    [Theory]
    // Oem1Gesture is internal, so the discovered (public) test method takes a bool and maps it here.
    [InlineData(false, false, Oem1BindingSlot.NormalSingle)]
    [InlineData(false, true, Oem1BindingSlot.NormalDouble)]
    [InlineData(true, false, Oem1BindingSlot.RoutingSingle)]
    [InlineData(true, true, Oem1BindingSlot.RoutingDouble)]
    public void Slot_resolution_uses_only_steam_output_active_and_the_gesture(bool steamOutputActive, bool doublePress, Oem1BindingSlot expected) =>
        Assert.Equal(expected, Oem1MappingSlots.Resolve(steamOutputActive, doublePress ? Oem1Gesture.Double : Oem1Gesture.Single));

    [Fact]
    public void Routing_runtime_unavailable_single_launches_big_picture_not_quick_access()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => RoutingRuntimeStatusSnapshot.Unavailable,
            () => pulseCount++,
            () => bigPictureCount++);

        var ok = dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.True(ok);
        Assert.Equal(1, bigPictureCount);
        Assert.Equal(0, pulseCount);
    }

    [Fact]
    public void Routing_feature_disabled_single_launches_big_picture_not_quick_access()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(active: false, available: false),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(1, bigPictureCount);
        Assert.Equal(0, pulseCount);
    }

    [Fact]
    public void Routing_enabled_but_inactive_single_launches_big_picture_not_quick_access()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(active: false, available: true),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(1, bigPictureCount);
        Assert.Equal(0, pulseCount);
    }

    [Fact]
    public void Actual_steam_deck_output_active_single_requests_quick_access_not_big_picture()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(active: true),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(1, pulseCount);
        Assert.Equal(0, bigPictureCount);
    }

    [Fact]
    public void Route_stopping_returns_to_big_picture_on_the_very_next_press_without_reconfiguration()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var steamOutputActive = true;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(steamOutputActive),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(1, pulseCount);

        steamOutputActive = false;
        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(1, bigPictureCount);
        Assert.Equal(1, pulseCount);
    }

    // ---- Mapping semantics ----

    [Fact]
    public void Normal_default_double_is_none()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Double));

        Assert.Equal(0, pulseCount);
        Assert.Equal(0, bigPictureCount);
    }

    [Fact]
    public void Routing_active_double_is_fixed_quick_access()
    {
        var pulseCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(true),
            () => pulseCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Double));

        Assert.Equal(1, pulseCount);
    }

    [Fact]
    public void Routing_active_fixed_quick_access_ignores_remapping_switch()
    {
        var pulseCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(true),
            () => pulseCount++,
            mapping: Oem1MappingSettings.Default with { RemappingEnabled = false });

        Assert.True(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
        Assert.Equal(1, pulseCount);
    }

    [Fact]
    public void All_four_slots_resolve_independently()
    {
        var hotkeys = 0;
        var launches = 0;
        var pulses = 0;
        var bigPictures = 0;
        var steamOutputActive = false;
        var mapping = Oem1MappingSettings.Default with
        {
            NormalSingle = Oem1SlotBinding.Of(Oem1Action.SteamBigPicture),
            NormalDouble = Oem1SlotBinding.Of(Oem1Action.KeyboardHotkey) with { Hotkey = new(Oem1HotkeyModifiers.Control, Oem1HotkeyKey.F12) },
            RoutingSingle = Oem1SlotBinding.Of(Oem1Action.SteamQuickAccess),
            RoutingDouble = Oem1SlotBinding.Of(Oem1Action.LaunchApplication) with { Launch = new(@"C:\fake\app.exe") }
        };
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(steamOutputActive),
            () => pulses++,
            () => bigPictures++,
            mapping: mapping,
            sendHotkey: _ => hotkeys++,
            launchApplication: _ => launches++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Double));
        steamOutputActive = true;
        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Double));

        Assert.Equal(1, bigPictures);
        Assert.Equal(1, hotkeys);
        Assert.Equal(2, pulses);
        Assert.Equal(0, launches);
    }

    [Fact]
    public void Hotkey_action_receives_the_slots_own_persisted_hotkey()
    {
        Oem1HotkeyBinding? sent = null;
        var expected = new Oem1HotkeyBinding(Oem1HotkeyModifiers.Control | Oem1HotkeyModifiers.Shift, Oem1HotkeyKey.S);
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            mapping: Oem1MappingSettings.Default with
            {
                NormalSingle = Oem1SlotBinding.Of(Oem1Action.KeyboardHotkey) with { Hotkey = expected }
            },
            sendHotkey: hotkey => sent = hotkey);

        Assert.True(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
        Assert.Equal(expected, sent);
    }

    [Fact]
    public void Routing_active_launch_mapping_cannot_override_quick_access()
    {
        Oem1LaunchApplicationBinding? launched = null;
        var expected = new Oem1LaunchApplicationBinding(@"C:\fake\app.exe", "--windowed");
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(true),
            mapping: Oem1MappingSettings.Default with
            {
                RoutingSingle = Oem1SlotBinding.Of(Oem1Action.LaunchApplication) with { Launch = expected }
            },
            launchApplication: application => launched = application);

        Assert.True(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
        Assert.Null(launched);
    }

    [Fact]
    public void Mapping_is_captured_fresh_at_dispatch_time()
    {
        var bigPictures = 0;
        var hotkeys = 0;
        var mapping = Oem1MappingSettings.Default;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            launchBigPicture: () => bigPictures++,
            captureMapping: () => mapping,
            sendHotkey: _ => hotkeys++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(1, bigPictures);

        mapping = mapping with { NormalSingle = Oem1SlotBinding.Of(Oem1Action.KeyboardHotkey) with { Hotkey = new(Key: Oem1HotkeyKey.F5) } };
        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(1, bigPictures);
        Assert.Equal(1, hotkeys);
    }

    [Fact]
    public void Routing_status_is_evaluated_fresh_at_dispatch_time()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var steamOutputActive = false;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(steamOutputActive),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(0, pulseCount);
        Assert.Equal(1, bigPictureCount);

        steamOutputActive = true;
        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(1, pulseCount);
        Assert.Equal(1, bigPictureCount);
    }

    // ---- Global remapping switch ----

    [Fact]
    public void Remapping_off_executes_nothing_at_all()
    {
        var pulses = 0;
        var bigPictures = 0;
        var hotkeys = 0;
        var launches = 0;
        var steamOutputActive = false;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(steamOutputActive),
            () => pulses++,
            () => bigPictures++,
            mapping: Oem1MappingSettings.Default with
            {
                RemappingEnabled = false,
                NormalDouble = Oem1SlotBinding.Of(Oem1Action.KeyboardHotkey) with { Hotkey = new(Key: Oem1HotkeyKey.F12) },
                RoutingDouble = Oem1SlotBinding.Of(Oem1Action.LaunchApplication) with { Launch = new(@"C:\fake\app.exe") }
            },
            sendHotkey: _ => hotkeys++,
            launchApplication: _ => launches++);

        Assert.True(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
        Assert.True(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Double)));
        steamOutputActive = true;
        Assert.True(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
        Assert.True(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Double)));

        Assert.Equal(2, pulses + bigPictures + hotkeys + launches);
    }

    [Fact]
    public void Remapping_off_does_not_erase_the_mappings_and_on_restores_them()
    {
        var bigPictures = 0;
        var mapping = Oem1MappingSettings.Default;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            launchBigPicture: () => bigPictures++,
            captureMapping: () => mapping);

        mapping = mapping with { RemappingEnabled = false };
        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(0, bigPictures);
        Assert.Equal(Oem1Action.SteamBigPicture, mapping.NormalSingle.Action);

        mapping = mapping with { RemappingEnabled = true };
        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(1, bigPictures);
    }

    // ---- Capability validation ----

    [Fact]
    public void A_persisted_binding_invalid_for_its_slot_is_refused_rather_than_executed()
    {
        var bigPictures = 0;
        var pulses = 0;
        // Only reachable by a hand-edited settings file or an older build -- the settings UI never
        // offers Big Picture for a routing slot, because it reads the same capability catalog.
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(true),
            () => pulses++,
            () => bigPictures++,
            mapping: Oem1MappingSettings.Default with { RoutingSingle = Oem1SlotBinding.Of(Oem1Action.SteamBigPicture) });

        var ok = dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        // Refused, but not a replacement-action FAILURE: nothing executed, so custom OEM1 authority
        // must not be revoked over a configuration problem.
        Assert.True(ok);
        Assert.Equal(0, bigPictures);
        Assert.Equal(1, pulses);
    }

    [Fact]
    public void Quick_access_persisted_into_a_normal_slot_is_refused()
    {
        var pulses = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            () => pulses++,
            mapping: Oem1MappingSettings.Default with { NormalSingle = Oem1SlotBinding.Of(Oem1Action.SteamQuickAccess) });

        Assert.True(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
        Assert.Equal(0, pulses);
    }

    [Fact]
    public void Bridge_policy_request_reaches_dispatcher_and_launches_big_picture_when_inactive()
    {
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            launchBigPicture: () => bigPictureCount++);
        var source = new ImmediateMsiEventSource();
        var recognizer = new Oem1GestureRecognizer(
            doubleClickEnabled: false,
            TimeSpan.FromMilliseconds(250),
            new ImmediateDelay(),
            new ZeroClock());
        using var bridge = new Oem1EventGestureBridge(source, recognizer);
        bridge.PolicyRequested += request => dispatcher.Dispatch(request);
        bridge.SetCustomAuthority(true);

        source.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));

        Assert.Equal(1, bigPictureCount);
    }

    // ---- Failure ----

    [Fact]
    public void Big_picture_backend_exception_causes_dispatch_to_report_failure()
    {
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            launchBigPicture: () => throw new InvalidOperationException("steam launch failed"));

        var ok = dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.False(ok);
    }

    [Fact]
    public void Quick_access_pulse_exception_causes_dispatch_to_report_failure()
    {
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(true),
            requestQuickAccessPulse: () => throw new InvalidOperationException("pulse failed"));

        var ok = dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.False(ok);
    }

    [Fact]
    public void Hotkey_execution_exception_causes_dispatch_to_report_failure()
    {
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            mapping: Oem1MappingSettings.Default with { NormalSingle = Oem1SlotBinding.Of(Oem1Action.KeyboardHotkey) },
            sendHotkey: _ => throw new InvalidOperationException("SendInput failed"));

        Assert.False(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
    }

    [Fact]
    public void Application_launch_exception_causes_dispatch_to_report_failure()
    {
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            mapping: Oem1MappingSettings.Default with { NormalSingle = Oem1SlotBinding.Of(Oem1Action.LaunchApplication) },
            launchApplication: _ => throw new InvalidOperationException("launch failed"));

        Assert.False(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
    }

    [Fact]
    public void Routing_status_capture_exception_causes_dispatch_to_report_failure()
    {
        // Review fix (BLOCKER): status capture and domain resolution must share the same failure
        // boundary as action execution -- if this throws, it must still be treated as an OEM1
        // replacement-action failure (fail-open), not escape Dispatch uncaught (which would leave
        // Oem1EventGestureBridge merely logging a subscriber failure without ever reaching
        // OnOem1ActionFailed, so suppression could remain armed with no action ever selected).
        var dispatcher = CreateDispatcher(() => throw new InvalidOperationException("status unavailable"));

        var ok = dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.False(ok);
    }

    [Fact]
    public void Mapping_capture_exception_causes_dispatch_to_report_failure()
    {
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            captureMapping: () => throw new InvalidOperationException("settings unavailable"));

        Assert.False(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
    }

    [Fact]
    public void Routing_unavailable_is_not_a_dispatch_failure()
    {
        var dispatcher = CreateDispatcher(() => RoutingRuntimeStatusSnapshot.Unavailable);

        var ok = dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.True(ok);
    }

    private sealed class ImmediateMsiEventSource : IMsiEventSource
    {
        public event Action<MsiOemEvent>? EventReceived;
        public bool Start() => true;
        internal void Emit(MsiOemEvent value) => EventReceived?.Invoke(value);
        public void Dispose() { }
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
}
