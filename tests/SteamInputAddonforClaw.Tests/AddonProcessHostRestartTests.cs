using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Tray restart/overlay cleanup work order: ordinary tray Restart uses a narrow
/// restart-specific safety evaluation -- real live transition/shutdown hazards only, never the
/// permanent mandatory-Runtime policy that used to gray both Restart and Exit whenever MSI Center M
/// was exactly Disabled. AddonProcessHost is not designed for direct unit instantiation of this
/// composed decision (no test seam for the private CenterMAuthorityTransition/RuntimeHost fields), so
/// -- matching the existing AddonProcessHostResumeTests source-extraction style -- this proves the
/// exact method contract against source.</summary>
public sealed class AddonProcessHostRestartTests
{
    private static string HostSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
    }

    private static string EvaluateUserRestartBody()
    {
        var source = HostSource();
        var body = source[source.IndexOf("internal UserTerminationDecision EvaluateUserRestart()", StringComparison.Ordinal)..];
        return body[..body.IndexOf("Full1902 0903 cleanup", StringComparison.Ordinal)];
    }

    [Fact] // section 7.1/7.2: Disabled-mode startup-commit pending still blocks restart.
    public void EvaluateUserRestart_blocks_while_disabled_mode_startup_is_still_committing()
    {
        var body = EvaluateUserRestartBody();

        Assert.Contains("Volatile.Read(ref _disabledControllerStartupPending) != 0", body, StringComparison.Ordinal);
        Assert.Contains("new(false, UserTerminationBlockReason.ControllerAuthorityTransition)", body, StringComparison.Ordinal);
    }

    [Fact] // section 7.1/7.2: an in-progress Enable/Disable MSI Center M and Restart transition blocks
           // restart, read from the existing owner's IsInProgress fact -- no mirrored/second boolean.
    public void EvaluateUserRestart_blocks_while_the_center_m_authority_transition_owner_is_busy()
    {
        var body = EvaluateUserRestartBody();

        Assert.Contains("_centerMAuthorityTransition?.IsInProgress == true", body, StringComparison.Ordinal);
    }

    [Fact] // section 7.2: falls back to the RAW lower-level Runtime safety decision (RuntimeShuttingDown),
           // never the removed ControllerAuthorityMandatory / mandatory-Disabled policy.
    public void EvaluateUserRestart_falls_back_to_the_lower_level_runtime_safety_decision_only()
    {
        var body = EvaluateUserRestartBody();

        Assert.Contains("_runtimeHost?.EvaluateUserTermination() ?? new(true, UserTerminationBlockReason.None)", body, StringComparison.Ordinal);
    }

    [Fact] // section 11: the mandatory-Disabled composition is fully removed, not merely bypassed.
    public void No_mandatory_controller_authority_composition_remains()
    {
        var host = HostSource();

        foreach (var forbidden in new[]
        {
            "UserTerminationComposition",
            "MandatoryControllerRuntimePolicy",
            "IsControllerRuntimeMandatory",
            "ControllerAuthorityMandatory",
        })
            Assert.DoesNotContain(forbidden, host, StringComparison.Ordinal);
    }

    [Fact] // section 6/10: TryInitializeTray no longer takes an ordinary exit delegate and wires the
           // restart-specific decision (not the removed composed EvaluateUserTermination) into the tray.
    public void TryInitializeTray_wires_a_single_restart_action_and_the_restart_specific_decision()
    {
        var host = HostSource();

        Assert.Contains("internal bool TryInitializeTray(Action restart)", host, StringComparison.Ordinal);
        Assert.Contains(
            "new SystemTrayIcon(_trayHostWindow.Handle, () => RequestFrontendOpen(FrontendOpenReason.Tray), restart, EvaluateUserRestart)",
            host, StringComparison.Ordinal);
    }

    [Fact] // section 5: removing the tray Overlay POC wiring must not delete or disconnect the PR #489
           // production front-button Overlay seam.
    public void RequestOverlayToggle_still_exists_and_is_not_wired_into_the_tray()
    {
        var host = HostSource();

        Assert.Contains("internal void RequestOverlayToggle() => _ = CoordinateOverlayToggleAsync();", host, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestOverlayToggle", host[host.IndexOf("internal bool TryInitializeTray", StringComparison.Ordinal)..host.IndexOf("PrepareForUninstallAsync", StringComparison.Ordinal)], StringComparison.Ordinal);
    }
}
