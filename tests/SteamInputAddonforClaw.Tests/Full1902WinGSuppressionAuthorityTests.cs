using System.Text.RegularExpressions;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Full1902 Policy B: native Win+G / Xbox Game Bar suppression is owned by the Center M Disabled /
/// Addon controller-authority lifetime. The full Disabled-mode controller path in
/// <c>AddonProcessHost</c> is not unit-constructable (real VIIPER / DirectInput / PID takeover), so
/// the required ordering and fail-closed invariants (work order section 14.2/14.3/14.5-14.9) are
/// asserted structurally against the production source. The low-level hook mechanics themselves are
/// covered by <c>WinGSuppressionGuardTests</c>, and the WING readiness seam by
/// <c>MsiClawFrontButtonRuntimeTests</c>.
/// </summary>
public sealed class Full1902WinGSuppressionAuthorityTests
{
    private static string HostSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
    }

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"method not found: {signature}");
        var next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        var alt = source.IndexOf("\n    internal ", start + signature.Length, StringComparison.Ordinal);
        if (alt >= 0 && (next < 0 || alt < next)) next = alt;
        return next < 0 ? source[start..] : source[start..next];
    }

    // ---- 14.2: hook installed on the one process-owned path, before the first Disabled attach ----

    [Fact]
    public void Hook_is_installed_once_by_InitializeRuntimeAsync_before_the_disabled_controller_path()
    {
        var body = Method(HostSource(), "internal async Task InitializeRuntimeAsync()");
        var lines = body.Split('\n').Where(l => !l.TrimStart().StartsWith("//")).ToArray();
        var codeOnly = string.Join('\n', lines);

        var start = codeOnly.IndexOf("_winGSuppressionGuard.Start();", StringComparison.Ordinal);
        Assert.True(start > 0, "InitializeRuntimeAsync must install the Win+G hook");

        // Must precede the composition build and the first real await (the Disabled controller path).
        var disabledPath = codeOnly.IndexOf("await TryStartDisabledModeControllerAsync", StringComparison.Ordinal);
        var firstConfigureAwait = codeOnly.IndexOf(".ConfigureAwait(false)", StringComparison.Ordinal);
        Assert.True(start < disabledPath);
        Assert.True(firstConfigureAwait < 0 || start < firstConfigureAwait);
    }

    [Fact]
    public void StartRuntimeEventWatchers_no_longer_installs_the_hook()
    {
        var body = Method(HostSource(), "internal void StartRuntimeEventWatchers()");
        Assert.DoesNotContain("_winGSuppressionGuard.Start()", body);
    }

    // ---- 14.2/14.3: arm + prove before the first live presentation; arm failure is fail-closed ----

    [Fact]
    public void Disabled_startup_arms_and_proves_suppression_before_the_first_AttachInitialAsync()
    {
        var body = Method(HostSource(), "private async Task TryStartDisabledModeControllerAsync(");
        var ensure = body.IndexOf("EnsureAddonAuthorityWinGSuppression()", StringComparison.Ordinal);
        var attach = body.IndexOf("AttachInitialAsync(source, snapshot", StringComparison.Ordinal);
        Assert.True(ensure > 0 && ensure < attach, "suppression must be armed/proven before the first live presentation attach");

        // The arm check gates the attach: a false result releases the presentation and returns.
        var gate = body[ensure..attach];
        Assert.Contains("ReleaseForCenterMEnableAsync", gate);
        Assert.Contains("return;", gate);
    }

    [Fact]
    public void Ensure_helper_requires_both_EnsureArmed_and_IsArmed_and_never_retries()
    {
        var body = Method(HostSource(), "private bool EnsureAddonAuthorityWinGSuppression()");
        Assert.Contains("EnsureArmed()", body);
        Assert.Contains("IsArmed", body);
        Assert.DoesNotContain("while", body);
        Assert.DoesNotContain("for (", body);
        Assert.DoesNotContain("Task.Delay", body);
    }

    // ---- 14.4: WING production readiness is bound to the existing guard seam ----

    [Fact]
    public void Wing_readiness_callback_is_bound_to_the_guard_IsArmed_seam_not_a_new_boolean()
    {
        var source = HostSource();
        Assert.Contains("nativeWinGSuppressionReady: () => _winGSuppressionGuard.IsArmed", source);
        // no duplicated authority boolean
        Assert.DoesNotContain("AddonAuthoritySuppressionActive", source);
        Assert.DoesNotContain("WingAuthorityManager", source);
        Assert.DoesNotContain("GameBarPolicyService", source);
    }

    // ---- 14.5/14.7: presentation switching / recovery never disarms or reinstalls ----

    [Fact]
    public void Suppression_is_only_ever_disarmed_on_stock_authority_release_or_process_teardown()
    {
        var source = HostSource();
        // Exactly two Disarm() sites: the stock authority-release seam and the process-exit finalizer.
        var disarms = Regex.Matches(source, @"_winGSuppressionGuard\.Disarm\(\)|guard\.Disarm\(\)").Count;
        Assert.Equal(1, disarms); // the authority-release seam; FinalizeWinGGuardAfterRoutingShutdown uses Dispose()

        // The presentation reconcile path (Steam/BPM switch + recovery) must not disarm or reinstall.
        var reconcile = Method(source, "private async Task ReconcileControllerPresentationAsync(");
        Assert.DoesNotContain("Disarm(", reconcile);
        Assert.DoesNotContain("_winGSuppressionGuard.Start()", reconcile);
        // ...but it IS fail-closed on a dropped hook (section 5.7).
        Assert.Contains("_winGSuppressionGuard.IsArmed", reconcile);
    }

    [Fact]
    public void Overlay_capture_resume_does_not_touch_suppression()
    {
        var source = HostSource();
        // The Overlay pause/resume handlers must not arm/disarm/reinstall suppression.
        foreach (var sig in new[] { "HandleOverlayCloseReasonAsync", "ResumeAfterOverlayAsync" })
        {
            if (source.Contains("private async Task " + sig))
            {
                var body = Method(source, "private async Task " + sig);
                Assert.DoesNotContain("_winGSuppressionGuard", body);
            }
        }
    }

    // ---- 14.8: stock authority release retires Addon output before disarming ----

    [Fact]
    public void Stock_authority_release_disarms_only_after_physical_pid1901_restore_succeeds()
    {
        var source = HostSource();
        var release = source.IndexOf("VirtualPresentationReleaseFailed", StringComparison.Ordinal);
        Assert.True(release > 0);
        var window = source[release..(release + 1600)];
        var physicalRelease = window.IndexOf("owner.ReleaseForCenterMEnableAsync", StringComparison.Ordinal);
        var disarm = window.IndexOf("_winGSuppressionGuard.Disarm()", StringComparison.Ordinal);
        Assert.True(physicalRelease > 0, "physical release call not found in window");
        Assert.True(disarm > physicalRelease, "Disarm must follow the physical PID1901 restore");
        Assert.Contains("physicalReleaseResult.Succeeded", window); // guarded by success
    }

    [Fact]
    public void Center_m_enabled_startup_never_arms_full1902_suppression()
    {
        var body = Method(HostSource(), "private async Task TryStartDisabledModeControllerAsync(");
        // The whole method is gated on exactly-Disabled; the arm call lives inside it, so a non-Disabled
        // (Enabled/Partial/Unavailable) boot returns before EnsureAddonAuthorityWinGSuppression().
        Assert.Contains("startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled", body);
        var guardReturn = body.IndexOf("!= FrontendCenterMStartupState.Disabled", StringComparison.Ordinal);
        var ensure = body.IndexOf("EnsureAddonAuthorityWinGSuppression()", StringComparison.Ordinal);
        Assert.True(guardReturn < ensure);
    }

    // ---- 14.9: PR #470 legacy-routing removal is not regressed ----

    [Fact]
    public void Pr470_invariants_hold()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var composition = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs"));

        Assert.Contains("AddonRoutingRuntime? routingRuntime = null;", composition);
        Assert.DoesNotContain("AddonRoutingRuntime.Create(", composition);
        Assert.DoesNotContain("StartRoutingObservation(", composition);

        var front = File.ReadAllText(Path.Combine(dir.FullName, "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs"));
        Assert.DoesNotContain("WinGProtectionRoutingStage", front);
        Assert.DoesNotContain("CanonicalSteamDeckOutputStage", front);
    }
}
