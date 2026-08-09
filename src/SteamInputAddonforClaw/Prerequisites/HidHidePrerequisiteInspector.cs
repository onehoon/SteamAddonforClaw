using SteamInputAddonforClaw.HidHide;

namespace SteamInputAddonforClaw.Prerequisites;

internal sealed class HidHidePrerequisiteInspector(IHidHideClient hidHideClient, IHidHidePackageProbe? packageProbe = null)
{
    public PrerequisiteAssessment Inspect()
    {
        try
        {
            var inspection = hidHideClient.Inspect();
            var package = (packageProbe ?? new WindowsHidHidePackageProbe()).Inspect();
            var version = package.InspectionSucceeded ? package.Version : null;
            return inspection.Status switch
            {
                HidHideInspectionStatus.Available => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "HidHideAvailable", version),
                HidHideInspectionStatus.NotInstalled => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Missing, "HidHideControlDeviceMissing", version),
                HidHideInspectionStatus.Disabled => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Unusable, "HidHideDisabled", version),
                HidHideInspectionStatus.InverseWhitelist => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Unusable, "HidHideInverseWhitelist", version),
                HidHideInspectionStatus.ConfigurationUnavailable => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Indeterminate, "HidHideConfigurationUnavailable", version),
                _ => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Indeterminate, "HidHideInspectionUnknown", version)
            };
        }
        catch
        {
            return new(PrerequisiteKind.HidHide, PrerequisiteStatus.Indeterminate, "HidHideInspectionFailed");
        }
    }
}
