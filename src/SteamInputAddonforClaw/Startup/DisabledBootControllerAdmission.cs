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
    /// <summary>Runtime prerequisites are not ready. Controller acquisition remains blocked, but
    /// the existing prerequisite setup path may assess whether a bounded repair is installable.</summary>
    PrerequisitesNotReady,
    /// <summary>At least one required fact could not be positively proven. No controller mutation
    /// runs; the mandatory Runtime stays alive so the user can inspect/repair or Enable and Restart.</summary>
    Blocked,
}

/// <summary>The one in-memory result PR4 carries forward for PR5 (work order PR4 section 6). It only
/// answers "may the next Full PID1902 ownership stage run?" -- the detailed facts keep their own
/// existing types. It is never persisted.</summary>
internal sealed record DisabledBootControllerAdmissionResult(
    DisabledBootAdmissionOutcome Outcome,
    string Reason,
    RuntimePrerequisiteAssessment? Prerequisites = null)
{
    internal bool IsReady => Outcome == DisabledBootAdmissionOutcome.Ready;

    internal bool IsEligibleForTransientUsbIpRetry => Outcome == DisabledBootAdmissionOutcome.PrerequisitesNotReady
        && Prerequisites is { } prerequisites
        && prerequisites.HidHide.Status == PrerequisiteStatus.Ready
        && prerequisites.Viiper.Status == PrerequisiteStatus.Ready
        && IsTransientUsbIpAssessment(prerequisites.UsbIpWin2);

    internal static DisabledBootControllerAdmissionResult NotApplicable { get; } =
        new(DisabledBootAdmissionOutcome.NotApplicable, "CenterMNotDisabled");

    internal static DisabledBootControllerAdmissionResult Ready { get; } =
        new(DisabledBootAdmissionOutcome.Ready, "AllAdmissionFactsVerified");

    internal static DisabledBootControllerAdmissionResult Blocked(string reason)
    {
        AppLog.Warn("ControllerAdmission", "Disabled-boot controller admission blocked.", null, ("Result", "Blocked"), ("Reason", reason));
        return new(DisabledBootAdmissionOutcome.Blocked, reason);
    }

    internal static DisabledBootControllerAdmissionResult PrerequisitesNotReady(RuntimePrerequisiteAssessment prerequisites)
    {
        var reason = DescribePrerequisites(prerequisites);
        return new(DisabledBootAdmissionOutcome.PrerequisitesNotReady, reason, prerequisites);
    }

    internal void LogInitialPrerequisiteFailure()
    {
        if (Outcome != DisabledBootAdmissionOutcome.PrerequisitesNotReady || Prerequisites is not { } prerequisites)
            return;

        AppLog.Warn("ControllerAdmission", "Disabled-boot controller admission requires prerequisite repair.", null,
            ("Result", "PrerequisitesNotReady"), ("Reason", Reason),
            ("HidHideStatus", prerequisites.HidHide.Status), ("HidHideReason", prerequisites.HidHide.Reason),
            ("UsbIpWin2Status", prerequisites.UsbIpWin2.Status), ("UsbIpWin2Reason", prerequisites.UsbIpWin2.Reason),
            ("UsbIpWin2Version", prerequisites.UsbIpWin2.Version),
            ("ViiperStatus", prerequisites.Viiper.Status), ("ViiperReason", prerequisites.Viiper.Reason));
    }

    private static string DescribePrerequisites(RuntimePrerequisiteAssessment prerequisites) =>
        $"HidHide={prerequisites.HidHide.Status}:{prerequisites.HidHide.Reason} " +
        $"UsbIpWin2={prerequisites.UsbIpWin2.Status}:{prerequisites.UsbIpWin2.Reason}" +
        (string.IsNullOrWhiteSpace(prerequisites.UsbIpWin2.Version) ? string.Empty : $"@{prerequisites.UsbIpWin2.Version}") +
        $" Viiper={prerequisites.Viiper.Status}:{prerequisites.Viiper.Reason}";

    private static bool IsTransientUsbIpAssessment(PrerequisiteAssessment assessment) => assessment.Reason switch
    {
        "UsbIpWin2InspectionFailed" => assessment.Status == PrerequisiteStatus.Indeterminate,
        "UsbIpWin2PackageInspectionFailed" => assessment.Status == PrerequisiteStatus.Indeterminate,
        "UsbIpWin2VersionUnavailable" => assessment.Status == PrerequisiteStatus.Indeterminate,
        "UsbIpWin2DeviceUnavailable" => assessment.Status == PrerequisiteStatus.Unusable,
        _ => false,
    };
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
/// current controller environment as <see cref="DisabledBootAdmissionOutcome.Ready"/>,
/// <see cref="DisabledBootAdmissionOutcome.PrerequisitesNotReady"/>, or
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
            AppLog.Warn("ControllerAdmission", "Runtime prerequisite inspection failed.", exception,
                ("ExceptionType", exception.GetType().Name), ("HResult", $"0x{exception.HResult:X8}"));
            return DisabledBootControllerAdmissionResult.Blocked("PrerequisiteInspectionUnavailable");
        }
        if (!prerequisites.IsRoutingReady)
            return DisabledBootControllerAdmissionResult.PrerequisitesNotReady(prerequisites);

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
