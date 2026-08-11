namespace SteamInputAddonforClaw.Prerequisites;

internal enum PrerequisiteKind { HidHide, UsbIpWin2, Viiper }

internal enum ComponentInstallationStatus { Missing, Installed, ExistingUnverified, Incompatible, Indeterminate }

internal sealed record ComponentInstallationAssessment(
    PrerequisiteKind Kind,
    ComponentInstallationStatus Status,
    string Reason,
    string? Version = null);

internal enum PrerequisiteStatus { Ready, Missing, Present, Unusable, Incompatible, Indeterminate }

internal sealed record PrerequisiteAssessment(
    PrerequisiteKind Kind,
    PrerequisiteStatus Status,
    string Reason,
    string? Version = null);

internal sealed record RuntimePrerequisiteAssessment(
    PrerequisiteAssessment HidHide,
    PrerequisiteAssessment UsbIpWin2,
    PrerequisiteAssessment Viiper)
{
    public bool IsRoutingReady => HidHide.Status == PrerequisiteStatus.Ready
        && UsbIpWin2.Status == PrerequisiteStatus.Ready
        && Viiper.Status == PrerequisiteStatus.Ready;
}

internal interface IRuntimePrerequisiteInspector
{
    RuntimePrerequisiteAssessment Inspect();
}
