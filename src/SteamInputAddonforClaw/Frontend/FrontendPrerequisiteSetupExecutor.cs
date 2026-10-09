using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Processes;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using System.Globalization;

namespace SteamInputAddonforClaw.Frontend;

internal interface IFrontendPrerequisiteSetupExecutor
{
    FirstTimeSetupAssessment Evaluate(SystemStatusSnapshot snapshot);
    Task<ChildProcessResult?> RunAsync(FirstTimeSetupAssessment assessment, string executablePath, CancellationToken cancellationToken);
}

internal sealed class FrontendPrerequisiteSetupExecutor : IFrontendPrerequisiteSetupExecutor
{
    private readonly bool _allowPrerequisiteRepairWhileRecoveryUnsafe;
    private readonly FrontendCenterMStartupState _centerMStartupState;
    private readonly bool _disabledBootPrerequisiteRepairWindow;
    private readonly bool _stockTopologyUnreadyBeforeBaseline;
    private readonly object _decisionLogSync = new();
    private string? _lastDecisionLogSignature;
    private readonly IHidHideProvisioningReceiptStore _hidHideReceiptStore = new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath);
    private readonly IChildProcessRunner _setupRunner = new ChildProcessRunner();

    internal FrontendPrerequisiteSetupExecutor(
        bool allowPrerequisiteRepairWhileRecoveryUnsafe = false,
        bool disabledBootPrerequisiteRepairWindow = false,
        bool stockTopologyUnreadyBeforeBaseline = false,
        FrontendCenterMStartupState centerMStartupState = FrontendCenterMStartupState.Unavailable)
    {
        _disabledBootPrerequisiteRepairWindow = disabledBootPrerequisiteRepairWindow;
        _stockTopologyUnreadyBeforeBaseline = stockTopologyUnreadyBeforeBaseline;
        _centerMStartupState = centerMStartupState;
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
        var assessment = FirstTimeSetupPolicy.Evaluate(new FirstTimeSetupInput(snapshot.HardwareCompatibility, snapshot.RecoverySafe, new SteamSessionState(snapshot.Steam.IsActive, snapshot.Steam.RunningAppId, snapshot.Steam.Source), snapshot.Prerequisites.HidHide, snapshot.Prerequisites.UsbIpWin2, hidInstall, usbInstall, new(hidState, usbState, hidBootChanged, usbBootChanged), allowPrerequisiteRepairWhileRecoveryUnsafe));
        LogSetupDecisionIfChanged(
            ("HardwareStatus", snapshot.HardwareCompatibility.Status),
            ("HardwareReason", snapshot.HardwareCompatibility.Reason),
            ("CenterMStartupState", _centerMStartupState),
            ("DisabledBootPrerequisiteRepairWindow", _disabledBootPrerequisiteRepairWindow),
            ("StockTopologyUnreadyBeforeBaseline", _stockTopologyUnreadyBeforeBaseline),
            ("PrerequisiteRepairWindow", _allowPrerequisiteRepairWhileRecoveryUnsafe),
            ("PrerequisiteRepairPermission", allowPrerequisiteRepairWhileRecoveryUnsafe),
            ("RecoverySafe", snapshot.RecoverySafe),
            ("ViiperPrerequisiteStatus", snapshot.Prerequisites.Viiper.Status),
            ("ViiperPrerequisiteReason", snapshot.Prerequisites.Viiper.Reason),
            ("HidHidePrerequisiteStatus", snapshot.Prerequisites.HidHide.Status),
            ("HidHidePrerequisiteReason", snapshot.Prerequisites.HidHide.Reason),
            ("HidHideInstallationStatus", hidInstall.Status),
            ("HidHideInstallationReason", hidInstall.Reason),
            ("HidHideInstalledVersion", hidInstall.Version),
            ("UsbIpPrerequisiteStatus", snapshot.Prerequisites.UsbIpWin2.Status),
            ("UsbIpPrerequisiteReason", snapshot.Prerequisites.UsbIpWin2.Reason),
            ("UsbIpInstallationStatus", usbInstall.Status),
            ("UsbIpInstallationReason", usbInstall.Reason),
            ("UsbIpInstalledVersion", usbInstall.Version),
            ("HidHideReceiptState", receipt.Receipt?.State),
            ("HidHideReceiptFailureReason", receipt.Receipt?.FailureReason),
            ("HidHideReceiptCorrupt", receipt.IsCorrupt),
            ("HidHideProvisioningState", hidState),
            ("HidHideBootSessionChanged", hidBootChanged),
            ("UsbIpReceiptState", usbReceipt.Receipt?.State),
            ("UsbIpReceiptFailureReason", usbReceipt.Receipt?.FailureReason),
            ("UsbIpReceiptCorrupt", usbReceipt.IsCorrupt),
            ("UsbIpProvisioningState", usbState),
            ("UsbIpBootSessionChanged", usbBootChanged),
            ("ProvisioningStorageStatus", storage.Status),
            ("ProvisioningStorageReason", storage.Reason),
            ("SteamActive", snapshot.Steam.IsActive),
            ("FirstTimeSetupStatus", assessment.Status),
            ("FirstTimeSetupReason", assessment.Reason),
            ("CanInstallRequiredComponents", assessment.CanInstallRequiredComponents));
        return assessment;
    }

    internal bool LogSetupDecisionIfChanged(params (string Key, object? Value)[] fields)
    {
        if (!AppLog.IsEnabled(AppLogLevel.Info)) return false;
        var signature = string.Join('\u001f', fields.Select(field =>
            $"{field.Key}={Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? "<null>"}"));
        lock (_decisionLogSync)
        {
            if (string.Equals(_lastDecisionLogSignature, signature, StringComparison.Ordinal)) return false;
            _lastDecisionLogSignature = signature;
            AppLog.Info("FirstTimeSetup", "Prerequisite setup decision evaluated.", fields);
            return true;
        }
    }

    public Task<ChildProcessResult?> RunAsync(FirstTimeSetupAssessment assessment, string executablePath, CancellationToken cancellationToken) =>
        PrerequisiteSetupRunnerPolicy.RunIfInstallableAsync(assessment, _setupRunner, executablePath, PrerequisiteSetupWorker.Argument, cancellationToken);

    internal static bool AllowsPrerequisiteRepairWhileRecoveryUnsafe(
        bool startupRepairWindow,
        RuntimePrerequisiteAssessment prerequisites) =>
        startupRepairWindow
        && prerequisites.Viiper.Status == PrerequisiteStatus.Ready;

    internal static bool IsStartupPrerequisiteRepairWindow(
        FrontendCenterMStartupState centerMStartupState,
        bool disabledBootPrerequisiteRepairWindow,
        bool stockTopologyUnreadyBeforeBaseline) =>
        (centerMStartupState == FrontendCenterMStartupState.Disabled && disabledBootPrerequisiteRepairWindow)
        || (centerMStartupState == FrontendCenterMStartupState.Enabled && stockTopologyUnreadyBeforeBaseline);

    private static ComponentProvisioningState ToComponentProvisioningState(HidHideProvisioningReceiptState state) => state switch { HidHideProvisioningReceiptState.Provisioned => ComponentProvisioningState.Provisioned, HidHideProvisioningReceiptState.InstallStarted => ComponentProvisioningState.InstallStarted, HidHideProvisioningReceiptState.InstalledPendingReboot => ComponentProvisioningState.PendingReboot, HidHideProvisioningReceiptState.AttemptFailed => ComponentProvisioningState.AttemptFailed, HidHideProvisioningReceiptState.AttemptCancelled => ComponentProvisioningState.AttemptCancelled, _ => ComponentProvisioningState.Indeterminate };
    private static ComponentProvisioningState ToComponentProvisioningState(UsbIpWin2ProvisioningReceiptState state) => state switch { UsbIpWin2ProvisioningReceiptState.Provisioned => ComponentProvisioningState.Provisioned, UsbIpWin2ProvisioningReceiptState.InstallStarted => ComponentProvisioningState.InstallStarted, UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot => ComponentProvisioningState.PendingReboot, UsbIpWin2ProvisioningReceiptState.AttemptFailed => ComponentProvisioningState.AttemptFailed, UsbIpWin2ProvisioningReceiptState.AttemptCancelled => ComponentProvisioningState.AttemptCancelled, _ => ComponentProvisioningState.Indeterminate };
}
