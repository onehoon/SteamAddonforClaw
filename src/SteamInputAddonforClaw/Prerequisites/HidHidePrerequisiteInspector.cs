using SteamInputAddonforClaw.HidHide;

namespace SteamInputAddonforClaw.Prerequisites;

internal sealed class HidHidePrerequisiteInspector(IHidHideClient hidHideClient)
{
    public PrerequisiteAssessment Inspect()
    {
        try
        {
            var inspection = hidHideClient.Inspect();
            return inspection.Status switch
            {
                HidHideInspectionStatus.Available => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "HidHideAvailable"),
                HidHideInspectionStatus.NotInstalled => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Missing, "HidHideControlDeviceMissing"),
                HidHideInspectionStatus.Disabled => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Unusable, "HidHideDisabled"),
                HidHideInspectionStatus.InverseWhitelist => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Unusable, "HidHideInverseWhitelist"),
                HidHideInspectionStatus.ConfigurationUnavailable => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Indeterminate, "HidHideConfigurationUnavailable"),
                _ => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Indeterminate, "HidHideInspectionUnknown")
            };
        }
        catch
        {
            return new(PrerequisiteKind.HidHide, PrerequisiteStatus.Indeterminate, "HidHideInspectionFailed");
        }
    }
}
