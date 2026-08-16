using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>
/// Composes the MSI Claw-specific routing input stages (native-mode session, native-mode stage,
/// physical input source/stage, physical isolation stage) that previously were constructed
/// directly in <c>App.xaml.cs</c>. This class owns construction only -- the caller retains
/// lifetime/cleanup ownership of the created objects exactly as before; this type does not
/// implement <see cref="IAsyncDisposable"/> and must not be given disposal responsibility.
/// </summary>
internal sealed class MsiClawRoutingComposition
{
    internal MsiClawNativeModeSessionCoordinator NativeModeSession { get; }
    internal MsiClawNativeModeStage NativeModeStage { get; }
    internal MsiClawInputSource PhysicalInputSource { get; }
    internal MsiClawPhysicalInputStage PhysicalInputStage { get; }
    internal MsiClawPhysicalIsolationStage PhysicalIsolationStage { get; }

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
    }
}
