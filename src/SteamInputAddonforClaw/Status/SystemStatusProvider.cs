using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Devices;

namespace SteamInputAddonforClaw.Status;

internal sealed class SystemStatusProvider(
    IDeviceInformationProvider deviceInformationProvider,
    IWindowsDeviceProbeContextFactory deviceProbeContextFactory,
    IHardwareCompatibilityEvaluator hardwareCompatibilityEvaluator,
    IRuntimePrerequisiteInspector prerequisiteInspector,
    // Full1902 Cleanup A: the raw Steam/BPM presentation facts (actual RunningAppID + Big Picture),
    // not the legacy effective-routing-session state. Steam/BPM selects the virtual presentation
    // only; it never decides controller authority, so status no longer runs a routing decision.
    Func<SteamPresentationSnapshot> steamPresentationProvider,
    Func<bool> recoverySafeProvider,
    // Full1902 0903 cleanup (section 4): an optional, read-only override for the FINAL derived Addon
    // operational status. A non-null result means the Runtime positively proved a healthy Full1902
    // Disabled-mode controller path (physical ownership + active presentation); null falls back to
    // the non-owned mapping below (Center M Enabled / startup still settling).
    Func<AddonStatusSnapshot?>? captureFull1902AddonStatus = null) : ISystemStatusProvider
{
    public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => CaptureCore(cancellationToken), cancellationToken);

    private SystemStatusSnapshot CaptureCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deviceProbe = deviceProbeContextFactory.Capture();
        var device = deviceInformationProvider.Capture(deviceProbe.Context);
        var hardwareCompatibility = hardwareCompatibilityEvaluator.Evaluate(deviceProbe);
        var prerequisites = prerequisiteInspector.Inspect();
        var presentation = TrySteamPresentation();
        var recoverySafe = TryRecoverySafety();
        var steam = new SteamStatusSnapshot(
            IsActive: presentation.WantsSteamDeck,
            RunningAppId: presentation.RunningAppId,
            Source: presentation.RunningAppId != 0
                ? SteamSessionSource.Actual
                : presentation.BigPictureActive
                    ? SteamSessionSource.BigPicture
                    : SteamSessionSource.Actual);

        var nonOwned = MapNonOwnedStatus(hardwareCompatibility, prerequisites, recoverySafe);
        // Full1902 0903 cleanup (section 4): a status-only override cannot become a controller
        // mutation or a capture failure -- an exception falls back to the non-owned mapping.
        AddonStatusSnapshot addon;
        try { addon = captureFull1902AddonStatus?.Invoke() ?? nonOwned; }
        catch (Exception exception)
        {
            AppLog.Debug("Status", "Full1902 Addon-status override threw; using the non-owned status.", ("Reason", exception.GetType().Name));
            addon = nonOwned;
        }
        AppLog.Debug("Status", "System status snapshot refreshed.", ("HidHide", prerequisites.HidHide.Status), ("UsbIpWin2", prerequisites.UsbIpWin2.Status), ("Viiper", prerequisites.Viiper.Status), ("AddonStatus", addon.Status));
        return new SystemStatusSnapshot(device, hardwareCompatibility, prerequisites, steam, addon, recoverySafe);
    }

    /// <summary>
    /// Full1902 Cleanup A/D: the Addon operational status when the Runtime is NOT the controller
    /// authority (Center M Enabled, or a Center M Disabled startup still committing). Derived purely
    /// from the safety/setup facts already captured -- no Steam-session routing model and no
    /// third-party controller-manager compatibility scan. A healthy non-owned state is
    /// <see cref="AddonOperationalStatus.Passive"/>: MSI Center M owns the controller and the Addon
    /// is intentionally not.
    /// </summary>
    private static AddonStatusSnapshot MapNonOwnedStatus(
        HardwareCompatibilityAssessment hardware,
        RuntimePrerequisiteAssessment prerequisites,
        bool recoverySafe)
    {
        if (!recoverySafe)
            return new(AddonOperationalStatus.Indeterminate, "Recovery state is not safe.");
        if (hardware.Status == HardwareCompatibilityStatus.Unsupported)
            return new(AddonOperationalStatus.Unsupported, "This handheld model is not supported by the current version.");
        if (hardware.Status == HardwareCompatibilityStatus.Indeterminate)
            return new(AddonOperationalStatus.Indeterminate, "Handheld model compatibility could not be verified.");
        if (!prerequisites.IsRoutingReady)
            return new(AddonOperationalStatus.SetupRequired, "Required controller components are not ready.");

        return new(AddonOperationalStatus.Passive, "MSI Center M owns the controller.");
    }

    private SteamPresentationSnapshot TrySteamPresentation() { try { return steamPresentationProvider(); } catch { return new SteamPresentationSnapshot(0, false); } }
    private bool TryRecoverySafety() { try { return recoverySafeProvider(); } catch { return false; } }
}
