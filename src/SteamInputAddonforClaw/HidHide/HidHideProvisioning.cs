using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw.HidHide;

internal static class HidHidePackageMetadata
{
    public static readonly Version BundledVersion = new(1, 5, 230, 0);
    public const string InstallerFileName = "HidHide_1.5.230_x64.exe";
    public const string InstallerSha256 = "F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6";
    public static string InstallerPath => Path.Combine(AppContext.BaseDirectory, "Dependencies", "HidHide", InstallerFileName);
}

internal sealed record HidHidePackageState(bool Installed, string? Version, bool InspectionSucceeded);
internal interface IHidHidePackageProbe { HidHidePackageState Inspect(); }

internal sealed record HidHideUninstallCandidate(string DisplayName, string? DisplayVersion, string? Publisher);
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
                candidates.Add(new(displayName, entry.GetValue("DisplayVersion") as string, entry.GetValue("Publisher") as string));
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
                AppLog.Info("HidHidePackageProbe", "HidHide package evidence found.", ("Source", candidates.Length > 0 ? "HKLMUninstall" : "InstallerDependencyFallback"), ("CandidateCount", candidates.Length), ("NormalizedVersion", versions[0]), ("PublisherMatch", candidates.Length > 0), ("Installed", true));
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

internal enum ElevatedProcessResultKind { Completed, CancelledBeforeStart, FailedToStart }
internal sealed record ElevatedProcessResult(ElevatedProcessResultKind Kind, int? ExitCode = null, string? Reason = null);
internal interface IElevatedProcessRunner { Task<ElevatedProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken); }

internal sealed class ElevatedProcessRunner : IElevatedProcessRunner
{
    public async Task<ElevatedProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process? process;
        try { process = Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = true, Verb = "runas" }); }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223) { return new(ElevatedProcessResultKind.CancelledBeforeStart); }
        catch (Exception exception) { return new(ElevatedProcessResultKind.FailedToStart, Reason: exception.Message); }
        if (process is null) return new(ElevatedProcessResultKind.FailedToStart, Reason: "ProcessStartReturnedNull");
        using (process)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            return new(ElevatedProcessResultKind.Completed, process.ExitCode);
        }
    }
}

internal enum HidHideProvisioningResultKind { AlreadyReady, Installed, RebootRequired, Cancelled, Failed, Blocked, AlreadyInProgress }
internal sealed record HidHideProvisioningResult(HidHideProvisioningResultKind Kind, string Reason);
internal sealed record HidHideProvisioningContext(ControllerEnvironmentCompatibilityAssessment Compatibility, ExternalControllerAssessment ExternalController, SteamSessionState Steam, PrerequisiteAssessment HidHide, bool SetupAllowed);
internal interface IHidHideProvisioner
{
    Task<HidHideProvisioningResult> ProvisionAsync(CancellationToken cancellationToken);
    void Reconcile();
    HidHideReceiptLoadResult GetReceiptStatus();
}

internal interface IHidHideProvisioningSafetyStateProvider
{
    Task<HidHideProvisioningContext> CaptureAsync(CancellationToken cancellationToken);
}

internal sealed class SystemStatusHidHideProvisioningSafetyStateProvider(ISystemStatusProvider systemStatusProvider) : IHidHideProvisioningSafetyStateProvider
{
    public async Task<HidHideProvisioningContext> CaptureAsync(CancellationToken cancellationToken)
    {
        var snapshot = await systemStatusProvider.CaptureAsync(cancellationToken).ConfigureAwait(false);
        return new(snapshot.Compatibility, snapshot.ExternalController, SteamSessionState.FromRunningAppId(snapshot.Steam.RunningAppId), snapshot.Prerequisites.HidHide, snapshot.Addon.Status == AddonOperationalStatus.SetupRequired);
    }
}

internal sealed class HidHideProvisioner(
    HidHidePrerequisiteInspector prerequisiteInspector,
    IHidHidePackageProbe packageProbe,
    IHidHideProvisioningReceiptStore receiptStore,
    IElevatedProcessRunner processRunner,
    Func<string>? installerPathProvider,
    Func<string, bool>? installerIntegrityValidator,
    IHidHideProvisioningSafetyStateProvider safetyStateProvider) : IHidHideProvisioner
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<string> _installerPathProvider = installerPathProvider ?? (() => HidHidePackageMetadata.InstallerPath);
    private readonly Func<string, bool> _installerIntegrityValidator = installerIntegrityValidator ?? VerifyInstaller;
    private readonly IHidHideProvisioningSafetyStateProvider _safetyStateProvider = safetyStateProvider ?? throw new ArgumentNullException(nameof(safetyStateProvider));

    public async Task<HidHideProvisioningResult> ProvisionAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false)) return new(HidHideProvisioningResultKind.AlreadyInProgress, "ProvisioningAlreadyInProgress");
        try
        {
            var context = await _safetyStateProvider.CaptureAsync(cancellationToken).ConfigureAwait(false);
            var existing = receiptStore.Load();
            if (existing.IsCorrupt) return new(HidHideProvisioningResultKind.Blocked, "ProvisioningReceiptCorrupt");
            if (context.HidHide.Status == PrerequisiteStatus.Ready) return new(HidHideProvisioningResultKind.AlreadyReady, "HidHideAlreadyReady");
            if (!AllowsInstall(context)) return new(HidHideProvisioningResultKind.Blocked, "ProvisioningSafetyGateBlocked");
            if (existing.Receipt is not null and not { State: HidHideProvisioningReceiptState.AttemptCancelled })
                return new(HidHideProvisioningResultKind.Blocked, "ProvisioningReceiptRequiresReconciliation");
            cancellationToken.ThrowIfCancellationRequested();
            var installer = _installerPathProvider();
            if (!_installerIntegrityValidator(installer)) return new(HidHideProvisioningResultKind.Failed, "InstallerIntegrityValidationFailed");
            var packageBeforeInstall = packageProbe.Inspect();
            if (!packageBeforeInstall.InspectionSucceeded || packageBeforeInstall.Installed)
                return new(HidHideProvisioningResultKind.Blocked, "ExistingInstallationNotReady");
            var started = NewReceipt(HidHideProvisioningReceiptState.InstallStarted);
            try { receiptStore.Save(started); }
            catch { return new(HidHideProvisioningResultKind.Failed, "ProvisioningReceiptSaveFailed"); }
            AppLog.Info("HidHideProvisioning", "HidHide provisioning attempt started.", ("Action", "Started"), ("AttemptId", started.AttemptId), ("Version", started.InstallerVersion), ("HashVerified", true));
            if (cancellationToken.IsCancellationRequested)
            {
                SaveTransition(started, HidHideProvisioningReceiptState.AttemptCancelled, null);
                return new(HidHideProvisioningResultKind.Cancelled, "ProvisioningCancelledBeforeStart");
            }
            try { context = await _safetyStateProvider.CaptureAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                SaveTransition(started, HidHideProvisioningReceiptState.AttemptCancelled, null);
                return new(HidHideProvisioningResultKind.Cancelled, "ProvisioningCancelledBeforeStart");
            }
            if (!AllowsInstall(context))
            {
                SaveTransition(started, HidHideProvisioningReceiptState.AttemptCancelled, null);
                return new(HidHideProvisioningResultKind.Blocked, "ProvisioningSafetyGateBlockedBeforeStart");
            }
            ElevatedProcessResult execution;
            try { execution = await processRunner.RunAsync(installer, "/exenoui /qn /norestart", cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                SaveTransition(started, HidHideProvisioningReceiptState.AttemptCancelled, null);
                return new(HidHideProvisioningResultKind.Cancelled, "ProvisioningCancelledBeforeStart");
            }
            AppLog.Info("HidHideProvisioning", "HidHide installer invocation completed.", ("Action", "InstallerCompleted"), ("AttemptId", started.AttemptId), ("Result", execution.Kind), ("ExitCode", execution.ExitCode));
            if (execution.Kind == ElevatedProcessResultKind.CancelledBeforeStart)
            {
                SaveTransition(started, HidHideProvisioningReceiptState.AttemptCancelled, null);
                return new(HidHideProvisioningResultKind.Cancelled, "UacCancelled");
            }
            if (execution.Kind != ElevatedProcessResultKind.Completed)
            {
                SaveTransition(started, HidHideProvisioningReceiptState.AttemptFailed, null);
                return new(HidHideProvisioningResultKind.Failed, "InstallerFailedToStart");
            }
            if (execution.ExitCode == 3010)
            {
                SaveTransition(started, HidHideProvisioningReceiptState.InstalledPendingReboot, packageProbe.Inspect().Version);
                return new(HidHideProvisioningResultKind.RebootRequired, "HidHideRebootRequired");
            }
            if (execution.ExitCode != 0)
            {
                SaveTransition(started, HidHideProvisioningReceiptState.AttemptFailed, null);
                return new(HidHideProvisioningResultKind.Failed, "InstallerExitCode" + execution.ExitCode);
            }
            return ValidateSuccessfulInstall(started);
        }
        catch (OperationCanceledException) { return new(HidHideProvisioningResultKind.Cancelled, "ProvisioningCancelledBeforeStart"); }
        finally { _gate.Release(); }
    }

    public void Reconcile()
    {
        var loaded = receiptStore.Load();
        if (loaded.IsCorrupt || loaded.Receipt is null) return;
        var receipt = loaded.Receipt;
        if (receipt.State == HidHideProvisioningReceiptState.AttemptCancelled) return;
        var prerequisite = prerequisiteInspector.Inspect();
        var package = packageProbe.Inspect();
        AppLog.Info("HidHideProvisioning", "HidHide installer validation completed.", ("Action", "ValidationCompleted"), ("PrerequisiteStatus", prerequisite.Status), ("ObservedVersion", package.Version));
        if (package.InspectionSucceeded && package.Installed && HidHidePackageVersionPolicy.AreEquivalent(package.Version, receipt.InstallerVersion)
            && receipt.State is HidHideProvisioningReceiptState.InstallStarted or HidHideProvisioningReceiptState.AttemptFailed or HidHideProvisioningReceiptState.InstalledPendingReboot
            && (prerequisite.Status == PrerequisiteStatus.Ready || receipt.State == HidHideProvisioningReceiptState.AttemptFailed))
            SaveTransition(receipt, HidHideProvisioningReceiptState.Provisioned, package.Version);
        else if (package.InspectionSucceeded && receipt.State == HidHideProvisioningReceiptState.InstallStarted && package.Installed
            && HidHidePackageVersionPolicy.AreEquivalent(package.Version, receipt.InstallerVersion)
            && prerequisite.Status != PrerequisiteStatus.Ready)
            SaveTransition(receipt, HidHideProvisioningReceiptState.InstalledPendingReboot, package.Version);
        AppLog.Info("HidHideProvisioning", "HidHide provisioning receipt reconciled.", ("Action", "ReceiptReconciled"), ("PreviousState", receipt.State));
    }

    public HidHideReceiptLoadResult GetReceiptStatus() => receiptStore.Load();

    private HidHideProvisioningResult ValidateSuccessfulInstall(HidHideProvisioningReceipt receipt)
    {
        var prerequisite = prerequisiteInspector.Inspect();
        var package = packageProbe.Inspect();
        if (prerequisite.Status == PrerequisiteStatus.Ready && package.Installed && HidHidePackageVersionPolicy.AreEquivalent(package.Version, receipt.InstallerVersion))
        {
            SaveTransition(receipt, HidHideProvisioningReceiptState.Provisioned, package.Version);
            return new(HidHideProvisioningResultKind.Installed, "HidHideProvisioned");
        }
        SaveTransition(receipt, HidHideProvisioningReceiptState.InstalledPendingReboot, package.Version);
        return new(HidHideProvisioningResultKind.RebootRequired, "HidHideValidationPending");
    }

    private static bool AllowsInstall(HidHideProvisioningContext context) => context.SetupAllowed
        && context.Compatibility.AllowsMutation
        && context.ExternalController.Status == ExternalControllerAssessmentStatus.Clear
        && !context.Steam.IsActive
        && context.HidHide.Status == PrerequisiteStatus.Missing;
    private static bool VerifyInstaller(string path) => File.Exists(path)
        && string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), HidHidePackageMetadata.InstallerSha256, StringComparison.OrdinalIgnoreCase);
    private static HidHideProvisioningReceipt NewReceipt(HidHideProvisioningReceiptState state) => new(HidHideProvisioningReceipt.CurrentSchemaVersion, state, Guid.NewGuid(), HidHidePackageMetadata.BundledVersion.ToString(), HidHidePackageMetadata.InstallerSha256, PrerequisiteStatus.Missing, DateTimeOffset.UtcNow, null, null);
    private void SaveTransition(HidHideProvisioningReceipt receipt, HidHideProvisioningReceiptState state, string? observedVersion) => receiptStore.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = observedVersion, FailureReason = state == HidHideProvisioningReceiptState.Provisioned ? null : receipt.FailureReason });
}
