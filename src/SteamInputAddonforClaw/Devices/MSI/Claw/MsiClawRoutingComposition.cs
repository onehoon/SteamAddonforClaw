using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Feedback;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>
/// Composes and owns the MSI Claw-specific routing input stages (native-mode session,
/// native-mode stage, physical input source/stage, physical isolation stage) that previously
/// were constructed directly in <c>App.xaml.cs</c>.
/// </summary>
/// <remarks>
/// Also implements <see cref="IHandheldRoutingComposition"/>, the generic view the routing/output
/// layer consumes -- a thin projection over the same already-created objects below, not a
/// second composition. The concrete properties remain for focused MSI implementation/tests;
/// removing any no-longer-needed concrete surface is deferred to later cleanup.
///
/// <para>
/// This class owns <see cref="NativeModeSession"/> and <see cref="PhysicalInputSource"/> --
/// <see cref="DisposeAsync"/> disposes them, in that order, matching the disposal order the
/// caller previously performed itself. <see cref="NativeModeStage"/>,
/// <see cref="PhysicalInputStage"/>, and <see cref="PhysicalIsolationStage"/> hold no resources
/// of their own beyond references to <see cref="NativeModeSession"/>/<see cref="PhysicalInputSource"/>,
/// so they are not separately disposed.
/// </para>
/// </remarks>
internal sealed class MsiClawRoutingComposition : IHandheldRoutingComposition
{
    internal MsiClawNativeModeSessionCoordinator NativeModeSession { get; }
    internal MsiClawNativeModeStage NativeModeStage { get; }
    internal MsiClawInputSource PhysicalInputSource { get; }
    internal MsiClawPhysicalInputStage PhysicalInputStage { get; }
    internal MsiClawPhysicalIsolationStage PhysicalIsolationStage { get; }
    internal MsiClawRumbleSink PhysicalRumbleSink { get; }

    private readonly IReadOnlyList<IRoutingPipelineStage> _stages;
    private readonly IReadOnlyList<IRoutingRuntimeSessionBoundaryParticipant> _sessionBoundaryParticipants;

    internal MsiClawRoutingComposition(
        MsiClawNativeStateManager nativeState,
        RecoveryManager recovery,
        PowerMutationGate powerGate,
        RecoverySafetyState recoverySafety)
    {
        NativeModeSession = new MsiClawNativeModeSessionCoordinator(
            nativeState,
            recovery,
            powerGate,
            recoverySafety);

        NativeModeStage = new MsiClawNativeModeStage(NativeModeSession);

        PhysicalInputSource = new MsiClawInputSource(() => new VorticeDirectInputDeviceEnumerator(IntPtr.Zero));

        PhysicalInputStage = new MsiClawPhysicalInputStage(
            () => new VorticeDirectInputDeviceEnumerator(IntPtr.Zero),
            PhysicalInputSource);

        PhysicalIsolationStage = new MsiClawPhysicalIsolationStage(
            PhysicalInputStage,
            NativeModeSession,
            recovery,
            new HidHideDriverClient(),
            () => Environment.ProcessPath);

        PhysicalRumbleSink = new MsiClawRumbleSink(PhysicalInputStage, new WindowsMsiClawRumbleTransport());

        _stages = [NativeModeStage, PhysicalInputStage, PhysicalIsolationStage];
        _sessionBoundaryParticipants = [NativeModeSession];
    }

    IReadOnlyList<IRoutingPipelineStage> IHandheldRoutingComposition.Stages => _stages;

    IControllerStateSnapshotSource IHandheldRoutingComposition.ControllerStateSource => PhysicalInputSource;

    IReadOnlyList<IRoutingRuntimeSessionBoundaryParticipant> IHandheldRoutingComposition.SessionBoundaryParticipants => _sessionBoundaryParticipants;

    IRoutingSafetySession? IHandheldRoutingComposition.SafetySession => NativeModeSession;
    IPhysicalRumbleSink? IHandheldRoutingComposition.PhysicalRumbleSink => PhysicalRumbleSink;

    public async ValueTask DisposeAsync()
    {
        PhysicalRumbleSink.Dispose();
        await NativeModeSession.DisposeAsync().ConfigureAwait(false);
        await PhysicalInputSource.DisposeAsync().ConfigureAwait(false);
    }
}
