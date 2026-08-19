using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;

namespace SteamInputAddonforClaw.Devices;

/// <summary>
/// Composition root that selects and constructs the device-specific
/// <see cref="IHandheldRoutingComposition"/> for the <see cref="IHandheldDeviceAdapter"/> supplied
/// by the application composition root. This is the one place allowed to know which concrete
/// routing composition (currently only
/// <see cref="MsiClawRoutingComposition"/>) an adapter needs; that vendor awareness must not leak
/// into <c>RoutingPipelineRuntimeCoordinator</c>, <c>RoutingPipelineExecutor</c>, the Steam Deck
/// output stage, or <see cref="IHandheldRoutingComposition"/> itself.
/// </summary>
/// <remarks>
/// Stateless pure construction/selection: it does not own, dispose, or track the compositions it
/// returns -- the caller owns the returned <see cref="IHandheldRoutingComposition"/> exactly as
/// it did before this factory existed. Returns null for an adapter this factory does not (yet)
/// know how to build a runtime composition for; it never falls back to a different backend.
/// </remarks>
internal sealed class HandheldRoutingCompositionFactory
{
    /// <param name="hardwareSupported">The startup hardware-support result
    /// (<see cref="Startup.StartupResult.HardwareSupported"/>). Forwarded unchanged to the device
    /// composition, which uses it only to gate device features that require recognized hardware
    /// (currently the OEM1 Center M mapping action path). This factory never re-evaluates it.</param>
    internal IHandheldRoutingComposition? Create(
        IHandheldDeviceAdapter adapter,
        RecoveryManager recovery,
        PowerMutationGate powerGate,
        RecoverySafetyState recoverySafety,
        bool hardwareSupported)
    {
        if (adapter is MsiClawDeviceAdapter && adapter.NativeState is MsiClawNativeStateManager nativeState)
        {
            return new MsiClawRoutingComposition(nativeState, recovery, powerGate, recoverySafety, hardwareSupported: hardwareSupported);
        }

        return null;
    }
}
