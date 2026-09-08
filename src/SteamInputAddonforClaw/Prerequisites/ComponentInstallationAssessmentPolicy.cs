using SteamInputAddonforClaw.HidHide;

namespace SteamInputAddonforClaw.Prerequisites;

internal static class ComponentInstallationAssessmentPolicy
{
    internal static ComponentInstallationAssessment AssessHidHide(HidHidePackageState package, PrerequisiteAssessment runtime, string expectedVersion)
        => Assess(PrerequisiteKind.HidHide, package.InspectionSucceeded, package.Installed, package.Version, runtime, expectedVersion);

    internal static ComponentInstallationAssessment AssessUsbIp(UsbIpWin2PackageState package, PrerequisiteAssessment runtime, string expectedVersion)
    {
        if (!package.InspectionSucceeded)
            return new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.Indeterminate, "PackageInspectionFailed", package.Version);
        if (package.Installed)
        {
            if (!Version.TryParse(package.Version, out var installedVersion)
                || !Version.TryParse(expectedVersion, out var bundledVersion))
                return new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.Incompatible, "UnexpectedPackageVersion", package.Version);

            var comparison = installedVersion.CompareTo(bundledVersion);
            if (comparison == 0)
                return new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.Installed, "ExpectedPackagePresent", package.Version);
            if (comparison < 0)
                return new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.UpdateRequired, "OlderPackageVersion", package.Version);
            return new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.Incompatible, "UnexpectedPackageVersion", package.Version);
        }

        return runtime.Status == PrerequisiteStatus.Missing
            ? new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.Missing, "PackageAndRuntimeMissing")
            : new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.ExistingUnverified, "RuntimeEvidenceWithoutPackage", package.Version);
    }

    private static ComponentInstallationAssessment Assess(PrerequisiteKind kind, bool inspectionSucceeded, bool installed, string? version, PrerequisiteAssessment runtime, string expectedVersion)
    {
        if (!inspectionSucceeded)
            return new(kind, ComponentInstallationStatus.Indeterminate, "PackageInspectionFailed", version);
        if (installed)
            return (kind == PrerequisiteKind.HidHide
                ? HidHidePackageVersionPolicy.AreEquivalent(version, expectedVersion)
                : string.Equals(version, expectedVersion, StringComparison.OrdinalIgnoreCase))
                ? new(kind, ComponentInstallationStatus.Installed, "ExpectedPackagePresent", version)
                : new(kind, ComponentInstallationStatus.Incompatible, "UnexpectedPackageVersion", version);
        if (runtime.Status == PrerequisiteStatus.Missing)
            return new(kind, ComponentInstallationStatus.Missing, "PackageAndRuntimeMissing");
        return new(kind, ComponentInstallationStatus.ExistingUnverified, "RuntimeEvidenceWithoutPackage", version);
    }
}
