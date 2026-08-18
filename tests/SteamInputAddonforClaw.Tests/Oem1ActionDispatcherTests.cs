using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class Oem1ActionDispatcherTests
{
    private static RoutingRuntimeStatusSnapshot StatusWithSteamOutput(bool active) =>
        new(Available: true, OperationalState: RoutingOperationalState.OverrideActive, SteamOutputActive: active, NativeDirectInputActive: false);

    [Fact]
    public void Default_single_requests_quick_access_when_steam_output_active()
    {
        var pulseCount = 0;
        var dispatcher = new Oem1ActionDispatcher(
            Oem1ActionBindings.Default,
            () => StatusWithSteamOutput(true),
            () => pulseCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(1, pulseCount);
    }

    [Fact]
    public void Default_single_does_nothing_when_steam_output_inactive()
    {
        var pulseCount = 0;
        var dispatcher = new Oem1ActionDispatcher(
            Oem1ActionBindings.Default,
            () => StatusWithSteamOutput(false),
            () => pulseCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(0, pulseCount);
    }

    [Fact]
    public void Default_double_is_none()
    {
        var pulseCount = 0;
        var dispatcher = new Oem1ActionDispatcher(
            Oem1ActionBindings.Default,
            () => StatusWithSteamOutput(true),
            () => pulseCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Double));

        Assert.Equal(0, pulseCount);
    }

    [Fact]
    public void Bindings_are_selectable_and_routing_does_not_force_quick_access()
    {
        var pulseCount = 0;
        var bindings = new Oem1ActionBindings(Single: Oem1Action.None, Double: Oem1Action.SteamQuickAccess);
        var dispatcher = new Oem1ActionDispatcher(
            bindings,
            () => StatusWithSteamOutput(true),
            () => pulseCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(0, pulseCount);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Double));
        Assert.Equal(1, pulseCount);
    }

    [Fact]
    public void Routing_status_is_evaluated_fresh_at_dispatch_time()
    {
        var pulseCount = 0;
        var steamOutputActive = false;
        var dispatcher = new Oem1ActionDispatcher(
            Oem1ActionBindings.Default,
            () => StatusWithSteamOutput(steamOutputActive),
            () => pulseCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(0, pulseCount);

        steamOutputActive = true;
        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(1, pulseCount);
    }

    [Fact]
    public void Bridge_policy_request_reaches_dispatcher_and_requests_quick_access()
    {
        var pulseCount = 0;
        var dispatcher = new Oem1ActionDispatcher(
            Oem1ActionBindings.Default,
            () => StatusWithSteamOutput(true),
            () => pulseCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(1, pulseCount);
    }
}
