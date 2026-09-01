using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Status;

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
    /// <summary>Read-only. Performs no physical mode command, HidHide mutation, VIIPER attach, or
    /// recovery-journal retirement (work order PR4 sections 9-14).</summary>
    DisabledBootControllerAdmissionResult Evaluate();
}

/// <summary>Read-only Disabled-boot admission for the Full PID1902 path (work order PR4). After the
/// startup coordinator has proven supported hardware and a stable MSI Claw topology, this classifies
/// the current controller environment as <see cref="DisabledBootAdmissionOutcome.Ready"/> or
/// <see cref="DisabledBootAdmissionOutcome.Blocked"/>. It reuses existing facts only -- the
/// controller-manager assessment, the Runtime prerequisite inspector, the read side of the recovery
/// journal, and the PR2 zero-target HidHide baseline inspection -- and mutates nothing.</summary>
internal sealed class DisabledBootControllerAdmission(
    IControllerEnvironmentAssessmentProvider environmentAssessmentProvider,
    IRuntimePrerequisiteInspector prerequisiteInspector,
    Func<RecoveryResult> loadRecoveryJournal,
    Func<AddonHidHideBaselineResult> inspectHidHideBaseline) : IDisabledBootControllerAdmission
{
    public DisabledBootControllerAdmissionResult Evaluate()
    {
        // 1. Fresh controller-manager assessment. Only a positively-proven "no third-party manager"
        //    admits exclusive Addon controller authority; Indeterminate / throwing fails closed
        //    (work order PR4 section 11). The stock compatibility result is deliberately NOT used --
        //    MsiCenterMNotOperational is expected while Center M is Disabled.
        ControllerManagerKind manager;
        try
        {
            manager = environmentAssessmentProvider.Capture().Manager.Kind;
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerAdmission", "Controller-manager assessment could not be read.", exception);
            return DisabledBootControllerAdmissionResult.Blocked("ControllerManagerAssessmentUnavailable");
        }
        if (manager != ControllerManagerKind.None)
            return DisabledBootControllerAdmissionResult.Blocked($"ControllerManager={manager}");

        // 2. Runtime prerequisites (HidHide + USBIP2 + VIIPER) must all be Ready. A known
        //    missing/unusable/incompatible/indeterminate virtual-controller backend blocks before
        //    PR5 is allowed to own the physical controller (work order PR4 section 12).
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

        // 3. Stale recovery journal is READ-ONLY evidence here. A surviving old route-scoped journal
        //    means the process cannot prove the new persistent PR2 baseline is the only HidHide
        //    ownership evidence -- block, and never clean/delete it in Disabled mode (section 13).
        RecoveryResult recovery;
        try
        {
            recovery = loadRecoveryJournal();
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerAdmission", "Recovery journal read failed.", exception);
            return DisabledBootControllerAdmissionResult.Blocked("RecoveryJournalUnavailable");
        }
        if (recovery.Status != RecoveryStatus.NoRecoveryNeeded)
            return DisabledBootControllerAdmissionResult.Blocked($"RecoveryJournal={recovery.Status}");

        // 4. The PR3 zero-target persistent HidHide baseline must already be proven compliant.
        //    "Applicable" (could be applied) is NOT compliant, and PR4 must not apply it (section 14).
        AddonHidHideBaselineResult baseline;
        try
        {
            baseline = inspectHidHideBaseline();
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerAdmission", "HidHide baseline inspection failed.", exception);
            return DisabledBootControllerAdmissionResult.Blocked("HidHideBaselineInspectionUnavailable");
        }
        if (baseline.Outcome != AddonHidHideBaselineOutcome.AlreadyCompliant)
            return DisabledBootControllerAdmissionResult.Blocked($"HidHideBaseline={baseline.Outcome}");

        AppLog.Info("ControllerAdmission", "Disabled-boot controller admission ready.",
            ("Result", "Ready"), ("Manager", manager), ("PrerequisitesReady", true),
            ("RecoveryJournal", "Clear"), ("HidHideBaseline", baseline.Outcome));
        return DisabledBootControllerAdmissionResult.Ready;
    }
}
