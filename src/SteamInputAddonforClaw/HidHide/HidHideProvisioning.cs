using System.Text.Json;
using Microsoft.Win32;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Prerequisites;

namespace SteamInputAddonforClaw.HidHide;

internal static class HidHidePackageMetadata
{
    public static readonly Version BundledVersion = new(1, 5, 230, 0);
    public const string InstallerFileName = "HidHide_1.5.230_x64.exe";
    public const string InstallerSha256 = "F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6";
    public static readonly Uri InstallerDownloadUri = new("https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe");
    internal static PrerequisiteInstallerDescriptor InstallerDescriptor => new("HidHide", BundledVersion, InstallerFileName, InstallerDownloadUri, InstallerSha256);
}
internal sealed record HidHidePackageState(bool Installed, string? Version, bool InspectionSucceeded);
internal interface IHidHidePackageProbe { HidHidePackageState Inspect(); }

internal sealed record HidHideUninstallCandidate(
    string DisplayName,
    string? DisplayVersion,
    string? Publisher,
    string? InstallLocation = null,
    string? SubKey = null,
    bool? WindowsInstaller = null,
    string? UninstallString = null,
    string? QuietUninstallString = null);
internal interface IHidHideUninstallRegistry
{
    IReadOnlyList<HidHideUninstallCandidate> Enumerate(RegistryView view);
}

internal interface IHidHideDependencyRegistry
{
    string? ReadVersion(RegistryView view);
}

internal sealed class WindowsHidHideUninstallRegistry : IHidHideUninstallRegistry
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public IReadOnlyList<HidHideUninstallCandidate> Enumerate(RegistryView view)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var uninstall = root.OpenSubKey(UninstallPath);
        if (uninstall is null) return [];
        var candidates = new List<HidHideUninstallCandidate>();
        foreach (var name in uninstall.GetSubKeyNames())
        {
            using var entry = uninstall.OpenSubKey(name);
            if (entry?.GetValue("DisplayName") is string displayName)
                candidates.Add(new(
                    displayName,
                    entry.GetValue("DisplayVersion") as string,
                    entry.GetValue("Publisher") as string,
                    entry.GetValue("InstallLocation") as string,
                    name,
                    entry.GetValue("WindowsInstaller") is int windowsInstaller ? windowsInstaller == 1 : null,
                    entry.GetValue("UninstallString") as string,
                    entry.GetValue("QuietUninstallString") as string));
        }
        return candidates;
    }
}

internal sealed class WindowsHidHideDependencyRegistry : IHidHideDependencyRegistry
{
    public string? ReadVersion(RegistryView view)
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view).OpenSubKey(@"Installer\Dependencies\NSS.Drivers.HidHide.x64");
        return key?.GetValue("Version") as string;
    }
}

internal sealed class WindowsHidHidePackageProbe : IHidHidePackageProbe
{
    private const string ProductName = "HidHide";
    private const string PublisherName = "Nefarius Software Solutions e.U.";
    private readonly IHidHideUninstallRegistry _uninstallRegistry;
    private readonly IHidHideDependencyRegistry _dependencyRegistry;
    internal WindowsHidHidePackageProbe(IHidHideUninstallRegistry? uninstallRegistry = null, IHidHideDependencyRegistry? dependencyRegistry = null)
    { _uninstallRegistry = uninstallRegistry ?? new WindowsHidHideUninstallRegistry(); _dependencyRegistry = dependencyRegistry ?? uninstallRegistry as IHidHideDependencyRegistry ?? new WindowsHidHideDependencyRegistry(); }
    public HidHidePackageState Inspect()
    {
        try
        {
            var candidates = Enum.GetValues<RegistryView>().Where(view => view is RegistryView.Registry64 or RegistryView.Registry32)
                .SelectMany(view => _uninstallRegistry.Enumerate(view).Where(IsExactCandidate).Select(candidate => (View: view, Candidate: candidate))).ToArray();
            if (candidates.Any(item => !TryNormalizeVersion(item.Candidate.DisplayVersion, out _)))
            {
                AppLog.Warn("HidHidePackageProbe", "HidHide uninstall evidence had an invalid version.", null, ("Source", "HKLMUninstall"), ("Reason", "InvalidPackageVersion"));
                return new(false, null, false);
            }
            var evidence = candidates.Select(item => NormalizeVersion(item.Candidate.DisplayVersion!)).ToList();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                var dependencyVersion = ReadDependencyVersion(view);
                if (dependencyVersion is not null) evidence.Add(dependencyVersion);
            }
            var versions = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (versions.Length > 1)
            {
                AppLog.Warn("HidHidePackageProbe", "Conflicting HidHide package evidence found.", null, ("Source", "HKLMUninstallAndDependency"), ("CandidateCount", candidates.Length), ("Reason", "ConflictingPackageEvidence"));
                return new(false, null, false);
            }
            if (versions.Length == 1)
            {
                AppLog.Debug("HidHidePackageProbe", "HidHide package evidence found.", ("Source", candidates.Length > 0 ? "HKLMUninstall" : "InstallerDependencyFallback"), ("CandidateCount", candidates.Length), ("NormalizedVersion", versions[0]), ("PublisherMatch", candidates.Length > 0), ("Installed", true));
                return new(true, versions[0], true);
            }

            return new(false, null, true);
        }
        catch { return new(false, null, false); }
    }

    private string? ReadDependencyVersion(RegistryView view)
    {
        var raw = _dependencyRegistry.ReadVersion(view);
        if (raw is null) return null;
        return TryNormalizeVersion(raw, out var normalized) ? normalized : throw new InvalidDataException("The HidHide dependency version is invalid.");
    }

    internal static bool IsExactCandidate(HidHideUninstallCandidate candidate) => string.Equals(candidate.DisplayName.Trim(), ProductName, StringComparison.OrdinalIgnoreCase) && string.Equals(candidate.Publisher?.Trim(), PublisherName, StringComparison.OrdinalIgnoreCase);
    internal static bool TryNormalizeVersion(string? value, out string normalized)
    {
        if (Version.TryParse(value, out var version)) { normalized = NormalizeVersion(version); return true; }
        normalized = string.Empty; return false;
    }
    internal static string NormalizeVersion(string value) => NormalizeVersion(Version.Parse(value));
    internal static string NormalizeVersion(Version version) => new Version(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0), Math.Max(version.Revision, 0)).ToString(4);
}

internal sealed class HidHideTrustedApplicationPathResolver(
    IHidHideUninstallRegistry? uninstallRegistry = null,
    Func<string, bool>? fileExists = null)
{
    private readonly IHidHideUninstallRegistry _uninstallRegistry = uninstallRegistry ?? new WindowsHidHideUninstallRegistry();
    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;

    internal IReadOnlyList<string> Resolve()
    {
        try
        {
            var candidates = Enum.GetValues<RegistryView>()
                .Where(view => view is RegistryView.Registry64 or RegistryView.Registry32)
                .SelectMany(view => _uninstallRegistry.Enumerate(view))
                .Where(WindowsHidHidePackageProbe.IsExactCandidate)
                .SelectMany(candidate => CandidatePaths(candidate.InstallLocation))
                .Select(Canonicalize)
                .Where(path => path is not null && _fileExists(path!))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return candidates;
        }
        catch (Exception exception)
        {
            AppLog.Warn("HidHide", "Official HidHide application path resolution failed.", exception,
                ("Action", "DoNotTrustWhitelistEntry"));
            return [];
        }
    }

    private static IEnumerable<string> CandidatePaths(string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation)) yield break;
        var root = installLocation.Trim().Trim('"');
        yield return Path.Combine(root, "HidHideClient.exe");
        yield return Path.Combine(root, "x64", "HidHideClient.exe");
        yield return Path.Combine(root, "HidHideCLI.exe");
        yield return Path.Combine(root, "x64", "HidHideCLI.exe");
    }

    private static string? Canonicalize(string path)
    {
        try { return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null; }
        catch { return null; }
    }
}

internal static class HidHidePackageVersionPolicy
{
    internal static bool AreEquivalent(string? left, string? right) => WindowsHidHidePackageProbe.TryNormalizeVersion(left, out var normalizedLeft)
        && WindowsHidHidePackageProbe.TryNormalizeVersion(right, out var normalizedRight)
        && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
}

internal interface IHidHideShortcutFileSystem
{
    bool Exists(string path);
    void Delete(string path);
}

internal sealed class WindowsHidHideShortcutFileSystem : IHidHideShortcutFileSystem
{
    public bool Exists(string path) => File.Exists(path);
    public void Delete(string path) => File.Delete(path);
}

internal sealed class HidHideDesktopShortcutCleanup
{
    internal const string ShortcutFileName = "HidHide Configuration Client.lnk";
    private readonly IHidHideShortcutFileSystem _fileSystem;
    private readonly IReadOnlyList<string> _paths;

    internal HidHideDesktopShortcutCleanup(IHidHideShortcutFileSystem? fileSystem = null, IEnumerable<string>? paths = null)
    {
        _fileSystem = fileSystem ?? new WindowsHidHideShortcutFileSystem();
        _paths = (paths ?? [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutFileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ShortcutFileName)])
            .Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal IReadOnlySet<string> Snapshot()
        => _paths.Where(path => SafeExists(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal void RemoveInstallerCreated(IReadOnlySet<string> before)
    {
        foreach (var path in _paths.Where(path => !before.Contains(path) && SafeExists(path)))
        {
            try { _fileSystem.Delete(path); AppLog.Info("HidHideProvisioning", "HidHide desktop shortcut removed.", ("DesktopShortcutCreatedByInstall", true), ("DesktopShortcutRemoved", true)); }
            catch (Exception exception) { AppLog.Warn("HidHideProvisioning", "HidHide desktop shortcut cleanup failed.", exception, ("Reason", "DesktopShortcutCleanupFailed")); }
        }
    }

    internal static bool IsExactPackageEstablished(HidHidePackageState package, string expectedVersion)
        => package.InspectionSucceeded && package.Installed && HidHidePackageVersionPolicy.AreEquivalent(package.Version, expectedVersion);

    private bool SafeExists(string path) { try { return _fileSystem.Exists(path); } catch { return false; } }
}

internal enum HidHideProvisioningReceiptState { InstallStarted, Provisioned, InstalledPendingReboot, AttemptFailed, AttemptCancelled }
internal sealed record HidHideProvisioningReceipt(int SchemaVersion, HidHideProvisioningReceiptState State, Guid AttemptId, string InstallerVersion, string InstallerSha256, PrerequisiteStatus PreProvisioningStatus, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, string? ObservedInstalledVersion, string? FailureReason = null, int? InstallerExitCode = null)
{
    public const int CurrentSchemaVersion = 1;
    public bool IsValid => SchemaVersion == CurrentSchemaVersion && AttemptId != Guid.Empty && PreProvisioningStatus == PrerequisiteStatus.Missing
        && Version.TryParse(InstallerVersion, out _) && InstallerSha256.Length == 64 && InstallerSha256.All(Uri.IsHexDigit)
        && StartedAtUtc != default && Enum.IsDefined(State);
}

internal sealed record HidHideReceiptLoadResult(HidHideProvisioningReceipt? Receipt, bool IsCorrupt);
internal interface IHidHideProvisioningReceiptStore
{
    HidHideReceiptLoadResult Load();
    void Save(HidHideProvisioningReceipt receipt);
}

internal sealed class HidHideProvisioningReceiptStore(string path) : IHidHideProvisioningReceiptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public HidHideReceiptLoadResult Load()
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null) return new(null, true);
        var security = ProvisioningStorageSecurity.Inspect(directory);
        if (security.Status is ProvisioningStorageStatus.Unsafe or ProvisioningStorageStatus.Indeterminate) return new(null, true);
        if (!File.Exists(path)) return new(null, false);
        try
        {
            var receipt = JsonSerializer.Deserialize<HidHideProvisioningReceipt>(File.ReadAllText(path), JsonOptions);
            return receipt is { IsValid: true } ? new(receipt, false) : new(null, true);
        }
        catch { return new(null, true); }
    }

    public void Save(HidHideProvisioningReceipt receipt)
    {
        if (!receipt.IsValid) throw new InvalidDataException("The HidHide provisioning receipt is invalid.");
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The provisioning receipt directory is unavailable.");
        var security = ProvisioningStorageSecurity.Inspect(directory);
        if (security.Status != ProvisioningStorageStatus.Trusted) throw new InvalidOperationException("The provisioning receipt storage is not trusted.");
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, receipt, JsonOptions);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
