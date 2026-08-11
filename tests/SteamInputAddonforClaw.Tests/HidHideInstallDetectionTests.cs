using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HidHideInstallDetectionTests
{
    [Fact]
    public void UninstallRegistration_RecognizesRealHidHideAndNormalizesVersion()
    {
        var probe = new WindowsHidHidePackageProbe(new FakeRegistry(
            [new("HidHide", "1.5.230", "Nefarius Software Solutions e.U.")], []));

        var result = probe.Inspect();

        Assert.True(result.Installed);
        Assert.True(result.InspectionSucceeded);
        Assert.Equal("1.5.230.0", result.Version);
    }

    [Fact]
    public void UninstallRegistration_DoesNotAcceptWrongPublisherOrUnrelatedApplication()
    {
        var probe = new WindowsHidHidePackageProbe(new FakeRegistry(
            [new("HidHide", "1.5.230", "Other Publisher"), new("Other App", "1.5.230", "Nefarius Software Solutions e.U.")], []));

        Assert.False(probe.Inspect().Installed);
    }

    [Fact]
    public void ConflictingUninstallVersions_FailsClosed()
    {
        var probe = new WindowsHidHidePackageProbe(new FakeRegistry(
            [new("HidHide", "1.5.230", "Nefarius Software Solutions e.U.")],
            [new("HidHide", "1.5.231", "Nefarius Software Solutions e.U.")]));

        var result = probe.Inspect();

        Assert.False(result.Installed);
        Assert.False(result.InspectionSucceeded);
        Assert.Null(result.Version);
    }

    [Fact]
    public void MatchingIdentityWithoutUsableVersion_FailsClosed()
    {
        var probe = new WindowsHidHidePackageProbe(new FakeRegistry(
            [new("HidHide", null, "Nefarius Software Solutions e.U.")], []));

        var result = probe.Inspect();

        Assert.False(result.Installed);
        Assert.False(result.InspectionSucceeded);
    }

    [Fact]
    public void ConflictingEvidence_CannotBecomeInstallableMissingState()
    {
        var package = new HidHidePackageState(false, null, false);
        var assessment = ComponentInstallationAssessmentPolicy.AssessHidHide(
            package,
            new(PrerequisiteKind.HidHide, PrerequisiteStatus.Missing, "Missing"),
            "1.5.230.0");

        Assert.Equal(ComponentInstallationStatus.Indeterminate, assessment.Status);
    }

    [Fact]
    public void VersionPolicy_TreatsMissingRevisionAsZero()
    {
        Assert.True(HidHidePackageVersionPolicy.AreEquivalent("1.5.230", "1.5.230.0"));
        Assert.False(HidHidePackageVersionPolicy.AreEquivalent("1.5.230", "1.5.231.0"));
    }

    [Fact]
    public void ComponentAssessment_TreatsRealWorldVersionAsExpectedPackage()
    {
        var result = ComponentInstallationAssessmentPolicy.AssessHidHide(
            new(true, "1.5.230", true),
            new(PrerequisiteKind.HidHide, PrerequisiteStatus.Unusable, "HidHideDisabled"),
            "1.5.230.0");

        Assert.Equal(ComponentInstallationStatus.Installed, result.Status);
    }

    [Fact]
    public void ShortcutCleanup_PreservesPreExistingAndRemovesFreshShortcut()
    {
        var fileSystem = new FakeFileSystem(["C:\\Desktop\\HidHide Configuration Client.lnk"]);
        var cleanup = new HidHideDesktopShortcutCleanup(fileSystem, ["C:\\Desktop\\HidHide Configuration Client.lnk", "C:\\Common\\HidHide Configuration Client.lnk"]);
        var before = cleanup.Snapshot();
        fileSystem.Files.Add("C:\\Common\\HidHide Configuration Client.lnk");

        cleanup.RemoveInstallerCreated(before);

        Assert.Contains("C:\\Desktop\\HidHide Configuration Client.lnk", fileSystem.Files);
        Assert.DoesNotContain("C:\\Common\\HidHide Configuration Client.lnk", fileSystem.Files);
        Assert.Equal(["C:\\Common\\HidHide Configuration Client.lnk"], fileSystem.Deleted);
    }

    [Fact]
    public void ShortcutCleanup_DeleteFailureDoesNotThrow()
    {
        var fileSystem = new FakeFileSystem([], throwOnDelete: true);
        var cleanup = new HidHideDesktopShortcutCleanup(fileSystem, ["C:\\Desktop\\HidHide Configuration Client.lnk"]);
        var before = cleanup.Snapshot();
        fileSystem.Files.Add("C:\\Desktop\\HidHide Configuration Client.lnk");

        var exception = Record.Exception(() => cleanup.RemoveInstallerCreated(before));

        Assert.Null(exception);
        Assert.Empty(fileSystem.Deleted);
    }

    private sealed class FakeRegistry(IReadOnlyList<HidHideUninstallCandidate> registry64, IReadOnlyList<HidHideUninstallCandidate> registry32) : IHidHideUninstallRegistry
    { public IReadOnlyList<HidHideUninstallCandidate> Enumerate(Microsoft.Win32.RegistryView view) => view == Microsoft.Win32.RegistryView.Registry64 ? registry64 : registry32; }

    private sealed class FakeFileSystem(IEnumerable<string> files, bool throwOnDelete = false) : IHidHideShortcutFileSystem
    {
        internal HashSet<string> Files { get; } = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        internal List<string> Deleted { get; } = [];
        public bool Exists(string path) => Files.Contains(path);
        public void Delete(string path) { if (throwOnDelete) throw new UnauthorizedAccessException(); Files.Remove(path); Deleted.Add(path); }
    }
}
