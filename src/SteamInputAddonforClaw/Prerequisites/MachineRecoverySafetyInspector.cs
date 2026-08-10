using Microsoft.Win32;

namespace SteamInputAddonforClaw.Prerequisites;

internal enum RecoverySafetyStatus { Safe, Unsafe, Indeterminate }

internal sealed record RecoverySafetyAssessment(RecoverySafetyStatus Status, string Reason);

internal interface IMachineRecoverySafetyInspector
{
    RecoverySafetyAssessment Inspect();
}

internal interface IWindowsProfilePathSource
{
    IReadOnlyList<string> EnumerateProfilePaths();
}

internal interface IRecoveryJournalPresenceProbe
{
    bool Exists(string journalPath);
}

internal enum ProfileDirectoryStatus { Present, Missing, Indeterminate }

internal interface IProfileDirectoryProbe
{
    ProfileDirectoryStatus Inspect(string profilePath);
}

internal sealed class WindowsProfileListPathSource : IWindowsProfilePathSource
{
    private const string ProfileListKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    public IReadOnlyList<string> EnumerateProfilePaths()
    {
        using var profileList = Registry.LocalMachine.OpenSubKey(ProfileListKeyPath)
            ?? throw new InvalidOperationException("Windows ProfileList is unavailable.");
        var paths = new List<string>();
        foreach (var sid in profileList.GetSubKeyNames())
        {
            using var profile = profileList.OpenSubKey(sid)
                ?? throw new InvalidOperationException("Windows profile metadata is unavailable.");
            if (profile.GetValue("ProfileImagePath") is not string profileImagePath || string.IsNullOrWhiteSpace(profileImagePath))
                throw new InvalidDataException("Windows profile path metadata is unavailable.");
            paths.Add(profileImagePath);
        }

        return paths;
    }
}

internal sealed class FileRecoveryJournalPresenceProbe : IRecoveryJournalPresenceProbe
{
    public bool Exists(string journalPath)
    {
        try
        {
            using var stream = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}

internal sealed class FileSystemProfileDirectoryProbe : IProfileDirectoryProbe
{
    public ProfileDirectoryStatus Inspect(string profilePath)
    {
        try
        {
            var attributes = File.GetAttributes(profilePath);
            return (attributes & FileAttributes.Directory) != 0
                ? ProfileDirectoryStatus.Present
                : ProfileDirectoryStatus.Indeterminate;
        }
        catch (FileNotFoundException) { return ProfileDirectoryStatus.Missing; }
        catch (DirectoryNotFoundException) { return ProfileDirectoryStatus.Missing; }
        catch { return ProfileDirectoryStatus.Indeterminate; }
    }
}

internal sealed class MachineRecoverySafetyInspector(
    IWindowsProfilePathSource? profilePathSource = null,
    IRecoveryJournalPresenceProbe? journalPresenceProbe = null,
    IProfileDirectoryProbe? profileDirectoryProbe = null) : IMachineRecoverySafetyInspector
{
    private const string RecoveryJournalRelativePath = "AppData\\Local\\SteamInputAddonforClaw\\recovery.json";
    private readonly IWindowsProfilePathSource _profilePathSource = profilePathSource ?? new WindowsProfileListPathSource();
    private readonly IRecoveryJournalPresenceProbe _journalPresenceProbe = journalPresenceProbe ?? new FileRecoveryJournalPresenceProbe();
    private readonly IProfileDirectoryProbe _profileDirectoryProbe = profileDirectoryProbe ?? new FileSystemProfileDirectoryProbe();

    public RecoverySafetyAssessment Inspect()
    {
        IReadOnlyList<string> profilePaths;
        try { profilePaths = _profilePathSource.EnumerateProfilePaths(); }
        catch { return new(RecoverySafetyStatus.Indeterminate, "ProfileEnumerationFailed"); }

        var inspectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profilePath in profilePaths)
        {
            string canonicalProfilePath;
            try { canonicalProfilePath = CanonicalizeLocalProfilePath(profilePath); }
            catch { return new(RecoverySafetyStatus.Indeterminate, "ProfilePathInspectionFailed"); }

            ProfileDirectoryStatus profileDirectoryStatus;
            try { profileDirectoryStatus = _profileDirectoryProbe.Inspect(canonicalProfilePath); }
            catch { return new(RecoverySafetyStatus.Indeterminate, "ProfilePathInspectionFailed"); }
            if (profileDirectoryStatus == ProfileDirectoryStatus.Missing) continue;
            if (profileDirectoryStatus != ProfileDirectoryStatus.Present)
                return new(RecoverySafetyStatus.Indeterminate, "ProfilePathInspectionFailed");

            if (!inspectedPaths.Add(canonicalProfilePath)) continue;

            try
            {
                var journalPath = Path.Combine(canonicalProfilePath, RecoveryJournalRelativePath);
                if (_journalPresenceProbe.Exists(journalPath)) return new(RecoverySafetyStatus.Unsafe, "RecoveryJournalPresent");
            }
            catch { return new(RecoverySafetyStatus.Indeterminate, "RecoveryJournalInspectionFailed"); }
        }

        return new(RecoverySafetyStatus.Safe, "RecoveryJournalAbsent");
    }

    private static string CanonicalizeLocalProfilePath(string profilePath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(profilePath);
        if (string.IsNullOrWhiteSpace(expanded) || !Path.IsPathFullyQualified(expanded) || expanded.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Windows profile path is not a local absolute path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
    }
}
