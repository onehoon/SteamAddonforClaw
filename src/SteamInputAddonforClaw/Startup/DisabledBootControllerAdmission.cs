using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Prerequisites;

namespace SteamInputAddonforClaw.Startup;

internal enum DisabledBootAdmissionOutcome
{
    /// <summary>Center M startup roots are not exactly Disabled -- the stock path owns this boot.</summary>
    NotApplicable,
    /// <summary>Every read-only admission fact was positively verified. PR5 may later attempt the
    /// first physical Full PID1902 ownership operation.</summary>
    Ready,
    /// <summary>At least one required fact could not be positively proven. No controller mutation
    /// runs; the mandatory Runtime stays alive so the user can inspect/repair or Enable and Restart.</summary>
    Blocked,
}

/// <summary>The one in-memory result PR4 carries forward for PR5 (work order PR4 section 6). It only
/// answers "may the next Full PID1902 ownership stage run?" -- the detailed facts keep their own
/// existing types. It is never persisted.</summary>
internal sealed record DisabledBootControllerAdmissionResult(DisabledBootAdmissionOutcome Outcome, string Reason)
{
    internal bool IsReady => Outcome == DisabledBootAdmissionOutcome.Ready;

    internal static DisabledBootControllerAdmissionResult NotApplicable { get; } =
        new(DisabledBootAdmissionOutcome.NotApplicable, "CenterMNotDisabled");

    internal static DisabledBootControllerAdmissionResult Ready { get; } =
        new(DisabledBootAdmissionOutcome.Ready, "AllAdmissionFactsVerified");

    internal static DisabledBootControllerAdmissionResult Blocked(string reason)
    {
        AppLog.Warn("ControllerAdmission", "Disabled-boot controller admission blocked.", null, ("Result", "Blocked"), ("Reason", reason));
        return new(DisabledBootAdmissionOutcome.Blocked, reason);
    }
}

internal interface IDisabledBootControllerAdmission
{
    /// <summary>Performs no physical mode command or VIIPER attach. It DOES normalize + read-back
    /// verify the persistent Addon HidHide baseline: every Disabled boot proves the Addon isolation
    /// baseline can be established NOW rather than blocking on stale third-party HidHide
    /// configuration.</summary>
    DisabledBootControllerAdmissionResult Evaluate();
}

/// <summary>Read-only Disabled-boot admission for the Full PID1902 path. After the startup
/// coordinator has proven supported hardware and a stable MSI Claw topology, this classifies the
/// current controller environment as <see cref="DisabledBootAdmissionOutcome.Ready"/> or
/// <see cref="DisabledBootAdmissionOutcome.Blocked"/> from current facts only -- the Runtime
/// prerequisite inspector and the deterministic zero-target HidHide baseline normalization -- and
/// performs no physical mode command.</summary>
internal sealed class DisabledBootControllerAdmission(
    IRuntimePrerequisiteInspector prerequisiteInspector,
    Func<AddonHidHideBaselineResult> normalizeHidHideBaseline) : IDisabledBootControllerAdmission
{
    public DisabledBootControllerAdmissionResult Evaluate()
    {
        // 1. Runtime prerequisites (HidHide + USBIP2 + VIIPER) must all be Ready. A known
        //    missing/unusable/incompatible/indeterminate virtual-controller backend blocks before
        //    PR5 is allowed to own the physical controller.
        RuntimePrerequisiteAssessment prerequisites;
        try
        {
            prerequisites = prerequisiteInspector.Inspect();
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerAdmission", "Runtime prerequisite inspection failed.", exception);
            return DisabledBootControllerAdmissionResult.Blocked("PrerequisiteInspectionUnavailable");
        }
        if (!prerequisites.IsRoutingReady)
            return DisabledBootControllerAdmissionResult.Blocked(
                $"Prerequisites HidHide={prerequisites.HidHide.Status} UsbIpWin2={prerequisites.UsbIpWin2.Status} Viiper={prerequisites.Viiper.Status}");

        // 2. Normalize + read-back verify the persistent Addon HidHide baseline on THIS boot. A user
        //    or another program may have changed HidHide while the Addon was not running, so a stale
        //    "was compliant last shutdown" assumption is not trusted. Only a proven-compliant
        //    (Success / AlreadyCompliant) baseline admits Full1902.
        AddonHidHideBaselineResult baseline;
        try
        {
            baseline = normalizeHidHideBaseline();
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerAdmission", "HidHide baseline normalization failed.", exception);
            return DisabledBootControllerAdmissionResult.Blocked("HidHideBaselineNormalizationUnavailable");
        }
        if (!baseline.IsCompliant)
            return DisabledBootControllerAdmissionResult.Blocked($"HidHideBaseline={baseline.Outcome}:{baseline.Reason}");

        AppLog.Info("ControllerAdmission", "Disabled-boot controller admission ready.",
            ("Result", "Ready"), ("PrerequisitesReady", true), ("HidHideBaseline", baseline.Outcome));
        return DisabledBootControllerAdmissionResult.Ready;
    }
}
