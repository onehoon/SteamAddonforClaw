using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MachineRecoverySafetyInspectorTests
{
    [Fact]
    public void CurrentProfileWithoutJournal_IsSafe()
    {
        var result = Inspect([ExistingProfilePath()], []);

        Assert.Equal(RecoverySafetyStatus.Safe, result.Status);
    }

    [Fact]
    public void AnotherProfileJournal_IsUnsafeRegardlessOfElevatedIdentity()
    {
        var standardUserProfile = ExistingProfilePath();
        var administratorProfile = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var journal = Path.Combine(standardUserProfile, "AppData", "Local", "SteamInputAddonforClaw", "recovery.json");

        var result = Inspect([administratorProfile, standardUserProfile], [journal]);

        Assert.Equal(RecoverySafetyStatus.Unsafe, result.Status);
        Assert.Equal("RecoveryJournalPresent", result.Reason);
        Assert.False(ElevatedPrerequisiteSetup.AllowsRecoverySafeProvisioning(result));
    }

    [Fact]
    public void DuplicateCanonicalProfilePaths_AreInspectedOnce()
    {
        var profile = ExistingProfilePath();
        var probe = new FakeJournalProbe([]);
        var inspector = new MachineRecoverySafetyInspector(new FakeProfileSource([profile, profile + Path.DirectorySeparatorChar]), probe, new FixedProfileDirectoryProbe(ProfileDirectoryStatus.Present));

        var result = inspector.Inspect();

        Assert.Equal(RecoverySafetyStatus.Safe, result.Status);
        Assert.Single(probe.InspectedPaths);
    }

    [Fact]
    public void ProfileEnumerationFailure_IsIndeterminateAndBlocksProvisioning()
    {
        var result = new MachineRecoverySafetyInspector(new ThrowingProfileSource(), new FakeJournalProbe([])).Inspect();

        Assert.Equal(RecoverySafetyStatus.Indeterminate, result.Status);
        Assert.False(ElevatedPrerequisiteSetup.AllowsRecoverySafeProvisioning(result));
    }

    [Fact]
    public void InvalidProfilePath_IsIndeterminateAndBlocksProvisioning()
    {
        var result = Inspect(["relative-profile-path"], []);

        Assert.Equal(RecoverySafetyStatus.Indeterminate, result.Status);
        Assert.False(ElevatedPrerequisiteSetup.AllowsRecoverySafeProvisioning(result));
    }

    [Fact]
    public void JournalInspectionFailure_IsIndeterminateAndBlocksProvisioning()
    {
        var result = new MachineRecoverySafetyInspector(new FakeProfileSource([ExistingProfilePath()]), new ThrowingJournalProbe(), new FixedProfileDirectoryProbe(ProfileDirectoryStatus.Present)).Inspect();

        Assert.Equal(RecoverySafetyStatus.Indeterminate, result.Status);
        Assert.False(ElevatedPrerequisiteSetup.AllowsRecoverySafeProvisioning(result));
    }

    [Fact]
    public void ProfileDirectoryAccessDenied_IsIndeterminateAndBlocksProvisioning()
    {
        var result = new MachineRecoverySafetyInspector(new FakeProfileSource([ExistingProfilePath()]), new FakeJournalProbe([]), new FixedProfileDirectoryProbe(ProfileDirectoryStatus.Indeterminate)).Inspect();

        Assert.Equal(RecoverySafetyStatus.Indeterminate, result.Status);
        Assert.False(ElevatedPrerequisiteSetup.AllowsRecoverySafeProvisioning(result));
    }

    [Fact]
    public void GenuinelyAbsentProfile_IsSkipped()
    {
        var result = new MachineRecoverySafetyInspector(new FakeProfileSource([ExistingProfilePath()]), new FakeJournalProbe([]), new FixedProfileDirectoryProbe(ProfileDirectoryStatus.Missing)).Inspect();

        Assert.Equal(RecoverySafetyStatus.Safe, result.Status);
    }

    private static RecoverySafetyAssessment Inspect(IReadOnlyList<string> profiles, IReadOnlyList<string> journals) =>
        new MachineRecoverySafetyInspector(new FakeProfileSource(profiles), new FakeJournalProbe(journals), new FixedProfileDirectoryProbe(ProfileDirectoryStatus.Present)).Inspect();

    private static string ExistingProfilePath() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private sealed class FakeProfileSource(IReadOnlyList<string> paths) : IWindowsProfilePathSource
    {
        public IReadOnlyList<string> EnumerateProfilePaths() => paths;
    }

    private sealed class ThrowingProfileSource : IWindowsProfilePathSource
    {
        public IReadOnlyList<string> EnumerateProfilePaths() => throw new InvalidOperationException();
    }

    private sealed class FakeJournalProbe(IReadOnlyList<string> journals) : IRecoveryJournalPresenceProbe
    {
        private readonly HashSet<string> _journals = new(journals, StringComparer.OrdinalIgnoreCase);
        public List<string> InspectedPaths { get; } = [];
        public bool Exists(string journalPath)
        {
            InspectedPaths.Add(journalPath);
            return _journals.Contains(journalPath);
        }
    }

    private sealed class ThrowingJournalProbe : IRecoveryJournalPresenceProbe
    {
        public bool Exists(string journalPath) => throw new UnauthorizedAccessException();
    }

    private sealed class FixedProfileDirectoryProbe(ProfileDirectoryStatus status) : IProfileDirectoryProbe
    {
        public ProfileDirectoryStatus Inspect(string profilePath) => status;
    }
}
