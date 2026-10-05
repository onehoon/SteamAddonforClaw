using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Processes;

namespace SteamInputAddonforClaw.Frontend;

internal interface IFrontendPrerequisiteSetupExecutor
{
    FirstTimeSetupAssessment Evaluate(SystemStatusSnapshot snapshot);
    Task<ChildProcessResult?> RunAsync(FirstTimeSetupAssessment assessment, string executablePath, CancellationToken cancellationToken);
}

internal sealed class FrontendPrerequisiteSetupExecutor : IFrontendPrerequisiteSetupExecutor
{
    private readonly bool _allowPrerequisiteRepairWhileRecoveryUnsafe;
    private readonly IHidHideProvisioningReceiptStore _hidHideReceiptStore = new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath);
    private readonly IChildProcessRunner _setupRunner = new ChildProcessRunner();

    internal FrontendPrerequisiteSetupExecutor(bool allowPrerequisiteRepairWhileRecoveryUnsafe = false)
    {
        _allowPrerequisiteRepairWhileRecoveryUnsafe = allowPrerequisiteRepairWhileRecoveryUnsafe;
    }

    public FirstTimeSetupAssessment Evaluate(SystemStatusSnapshot snapshot)
    {
        var receipt = _hidHideReceiptStore.Load();
        var usbReceipt = new UsbIpWin2ProvisioningReceiptStore(VelopackAppPaths.UsbIpWin2ProvisioningReceiptPath).Load();
        var hidPackage = new WindowsHidHidePackageProbe().Inspect();
        var usbPackage = new WindowsUsbIpWin2PackageProbe().Inspect();
        var storage = ProvisioningStorageSecurity.Inspect(VelopackAppPaths.ProvisioningStateDirectory);
        var hidState = receipt.IsCorrupt || storage.Status is ProvisioningStorageStatus.Unsafe or ProvisioningStorageStatus.Indeterminate ? ComponentProvisioningState.Corrupt : receipt.Receipt is not null ? ToComponentProvisioningState(receipt.Receipt.State) : File.Exists(VelopackAppPaths.LegacyHidHideProvisioningReceiptPath) ? ComponentProvisioningState.Legacy : ComponentProvisioningState.None;
        var usbState = usbReceipt.IsCorrupt || storage.Status is ProvisioningStorageStatus.Unsafe or ProvisioningStorageStatus.Indeterminate ? ComponentProvisioningState.Corrupt : usbReceipt.Receipt is not null ? ToComponentProvisioningState(usbReceipt.Receipt.State) : ComponentProvisioningState.None;
        var hidBootChanged = receipt.Receipt is { State: HidHideProvisioningReceiptState.InstalledPendingReboot } hp && BootSession.HasChangedSince(hp.StartedAtUtc);
        var usbBootChanged = usbReceipt.Receipt is { State: UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot } up && BootSession.HasChangedSince(up.StartedAtUtc);
        var hidInstall = ComponentInstallationAssessmentPolicy.AssessHidHide(hidPackage, snapshot.Prerequisites.HidHide, HidHidePackageMetadata.BundledVersion.ToString());
        var usbInstall = ComponentInstallationAssessmentPolicy.AssessUsbIp(usbPackage, snapshot.Prerequisites.UsbIpWin2, UsbIpWin2PackageMetadata.BundledVersion.ToString());
        var allowPrerequisiteRepairWhileRecoveryUnsafe = AllowsPrerequisiteRepairWhileRecoveryUnsafe(
            _allowPrerequisiteRepairWhileRecoveryUnsafe,
            snapshot.Prerequisites);
        return FirstTimeSetupPolicy.Evaluate(new FirstTimeSetupInput(snapshot.HardwareCompatibility, snapshot.RecoverySafe, new SteamSessionState(snapshot.Steam.IsActive, snapshot.Steam.RunningAppId, snapshot.Steam.Source), snapshot.Prerequisites.HidHide, snapshot.Prerequisites.UsbIpWin2, hidInstall, usbInstall, new(hidState, usbState, hidBootChanged, usbBootChanged), allowPrerequisiteRepairWhileRecoveryUnsafe));
    }

    public Task<ChildProcessResult?> RunAsync(FirstTimeSetupAssessment assessment, string executablePath, CancellationToken cancellationToken) =>
        PrerequisiteSetupRunnerPolicy.RunIfInstallableAsync(assessment, _setupRunner, executablePath, PrerequisiteSetupWorker.Argument, cancellationToken);

    internal static bool AllowsPrerequisiteRepairWhileRecoveryUnsafe(
        bool startupRepairWindow,
        RuntimePrerequisiteAssessment prerequisites) =>
        startupRepairWindow
        && prerequisites.Viiper.Status == PrerequisiteStatus.Ready;

    private static ComponentProvisioningState ToComponentProvisioningState(HidHideProvisioningReceiptState state) => state switch { HidHideProvisioningReceiptState.Provisioned => ComponentProvisioningState.Provisioned, HidHideProvisioningReceiptState.InstallStarted => ComponentProvisioningState.InstallStarted, HidHideProvisioningReceiptState.InstalledPendingReboot => ComponentProvisioningState.PendingReboot, HidHideProvisioningReceiptState.AttemptFailed => ComponentProvisioningState.AttemptFailed, HidHideProvisioningReceiptState.AttemptCancelled => ComponentProvisioningState.AttemptCancelled, _ => ComponentProvisioningState.Indeterminate };
    private static ComponentProvisioningState ToComponentProvisioningState(UsbIpWin2ProvisioningReceiptState state) => state switch { UsbIpWin2ProvisioningReceiptState.Provisioned => ComponentProvisioningState.Provisioned, UsbIpWin2ProvisioningReceiptState.InstallStarted => ComponentProvisioningState.InstallStarted, UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot => ComponentProvisioningState.PendingReboot, UsbIpWin2ProvisioningReceiptState.AttemptFailed => ComponentProvisioningState.AttemptFailed, UsbIpWin2ProvisioningReceiptState.AttemptCancelled => ComponentProvisioningState.AttemptCancelled, _ => ComponentProvisioningState.Indeterminate };
}
