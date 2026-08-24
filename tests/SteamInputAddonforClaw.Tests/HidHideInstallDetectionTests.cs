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
    public void DependencyOnlyEvidence_IsAccepted()
    {
        var result = new WindowsHidHidePackageProbe(new FakeRegistry([], [], "1.5.230")).Inspect();
        Assert.True(result.Installed);
        Assert.Equal("1.5.230.0", result.Version);
    }

    [Fact]
    public void UninstallAndSameDependencyEvidence_IsAccepted()
    {
        var result = new WindowsHidHidePackageProbe(new FakeRegistry([new("HidHide", "1.5.230", "Nefarius Software Solutions e.U.")], [], "1.5.230.0")).Inspect();
        Assert.True(result.Installed);
        Assert.Equal("1.5.230.0", result.Version);
    }

    [Fact]
    public void UninstallAndConflictingDependencyEvidence_FailsClosed()
    {
        var result = new WindowsHidHidePackageProbe(new FakeRegistry([new("HidHide", "1.5.230", "Nefarius Software Solutions e.U.")], [], "1.5.231")).Inspect();
        Assert.False(result.Installed);
        Assert.False(result.InspectionSucceeded);
    }

    [Fact]
    public void InvalidDependencyVersion_FailsClosed()
    {
        var result = new WindowsHidHidePackageProbe(new FakeRegistry([], [], "not-a-version")).Inspect();
        Assert.False(result.Installed);
        Assert.False(result.InspectionSucceeded);
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
            new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "HidHideAvailableInactive"),
            "1.5.230.0");

        Assert.Equal(ComponentInstallationStatus.Installed, result.Status);
    }

    [Fact]
    public void ShortcutCleanup_IsNotEligibleForWrongPackageVersion()
    {
        Assert.False(HidHideDesktopShortcutCleanup.IsExactPackageEstablished(new(true, "1.5.231.0", true), "1.5.230.0"));
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

    [Fact]
    public void Official_client_path_resolver_returns_only_existing_exact_package_client()
    {
        const string installLocation = "C:\\Program Files\\Nefarius Software Solutions\\HidHide";
        const string officialPath = installLocation + "\\x64\\HidHideClient.exe";
        var resolver = new HidHideTrustedApplicationPathResolver(
            new FakeRegistry([new("HidHide", "1.5.230", "Nefarius Software Solutions e.U.", installLocation)], []),
            path => string.Equals(path, officialPath, StringComparison.OrdinalIgnoreCase));

        var result = resolver.Resolve();

        Assert.Equal([officialPath], result);
    }

    [Fact]
    public void Official_path_resolver_returns_client_and_cli_from_package_layouts()
    {
        const string installLocation = "C:\\Program Files\\Nefarius Software Solutions\\HidHide";
        const string clientPath = installLocation + "\\x64\\HidHideClient.exe";
        const string cliPath = installLocation + "\\HidHideCLI.exe";
        var resolver = new HidHideTrustedApplicationPathResolver(
            new FakeRegistry([new("HidHide", "1.5.230", "Nefarius Software Solutions e.U.", installLocation)], []),
            path => string.Equals(path, clientPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, cliPath, StringComparison.OrdinalIgnoreCase));

        var result = resolver.Resolve();

        Assert.Equal([clientPath, cliPath], result);
    }

    [Fact]
    public void Official_path_resolver_does_not_trust_same_named_files_outside_package_location()
    {
        const string installLocation = "C:\\Program Files\\Nefarius Software Solutions\\HidHide";
        var resolver = new HidHideTrustedApplicationPathResolver(
            new FakeRegistry([new("HidHide", "1.5.230", "Nefarius Software Solutions e.U.", installLocation)], []),
            _ => true);

        var result = resolver.Resolve();

        Assert.DoesNotContain(result, path => path.StartsWith("C:\\Temp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result, path => path.EndsWith("\\HidHideClient.exe", StringComparison.OrdinalIgnoreCase) &&
            path.Contains("Temp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Official_client_path_resolution_failure_returns_no_trusted_path()
    {
        var resolver = new HidHideTrustedApplicationPathResolver(
            new FakeRegistry([new("HidHide", "1.5.230", "Nefarius Software Solutions e.U.", "C:\\Program Files\\HidHide")], []),
            _ => false);

        Assert.Empty(resolver.Resolve());
    }

    private sealed class FakeRegistry(IReadOnlyList<HidHideUninstallCandidate> registry64, IReadOnlyList<HidHideUninstallCandidate> registry32, string? dependency64 = null, string? dependency32 = null) : IHidHideUninstallRegistry, IHidHideDependencyRegistry
    {
        public IReadOnlyList<HidHideUninstallCandidate> Enumerate(Microsoft.Win32.RegistryView view) => view == Microsoft.Win32.RegistryView.Registry64 ? registry64 : registry32;
        public string? ReadVersion(Microsoft.Win32.RegistryView view) => view == Microsoft.Win32.RegistryView.Registry64 ? dependency64 : dependency32;
    }

    private sealed class FakeFileSystem(IEnumerable<string> files, bool throwOnDelete = false) : IHidHideShortcutFileSystem
    {
        internal HashSet<string> Files { get; } = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        internal List<string> Deleted { get; } = [];
        public bool Exists(string path) => Files.Contains(path);
        public void Delete(string path) { if (throwOnDelete) throw new UnauthorizedAccessException(); Files.Remove(path); Deleted.Add(path); }
    }
}
