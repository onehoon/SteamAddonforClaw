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

internal sealed class WindowsHidHidePackageProbe : IHidHidePackageProbe
{
    private const string VersionKey = @"Installer\Dependencies\NSS.Drivers.HidHide.x64";
    public HidHidePackageState Inspect()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(VersionKey);
            var version = key?.GetValue("Version") as string;
            return new(!string.IsNullOrWhiteSpace(version), version, true);
        }
        catch { return new(false, null, false); }
    }
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
        if (prerequisite.Status == PrerequisiteStatus.Ready && package.Installed && string.Equals(package.Version, receipt.InstallerVersion, StringComparison.OrdinalIgnoreCase)
            && receipt.State is HidHideProvisioningReceiptState.InstallStarted or HidHideProvisioningReceiptState.InstalledPendingReboot)
            SaveTransition(receipt, HidHideProvisioningReceiptState.Provisioned, package.Version);
        else if (receipt.State == HidHideProvisioningReceiptState.InstallStarted && package.Installed
            && string.Equals(package.Version, receipt.InstallerVersion, StringComparison.OrdinalIgnoreCase)
            && prerequisite.Status != PrerequisiteStatus.Ready)
            SaveTransition(receipt, HidHideProvisioningReceiptState.InstalledPendingReboot, package.Version);
        AppLog.Info("HidHideProvisioning", "HidHide provisioning receipt reconciled.", ("Action", "ReceiptReconciled"), ("PreviousState", receipt.State));
    }

    public HidHideReceiptLoadResult GetReceiptStatus() => receiptStore.Load();

    private HidHideProvisioningResult ValidateSuccessfulInstall(HidHideProvisioningReceipt receipt)
    {
        var prerequisite = prerequisiteInspector.Inspect();
        var package = packageProbe.Inspect();
        if (prerequisite.Status == PrerequisiteStatus.Ready && package.Installed && string.Equals(package.Version, receipt.InstallerVersion, StringComparison.OrdinalIgnoreCase))
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
    private void SaveTransition(HidHideProvisioningReceipt receipt, HidHideProvisioningReceiptState state, string? observedVersion) => receiptStore.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = observedVersion });
}
