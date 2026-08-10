using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Windowing;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Foundation;
using WinRT.Interop;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Prerequisites;
using System.Collections.ObjectModel;
using System.Diagnostics;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;
using SteamInputAddonforClaw.Developer;

namespace SteamInputAddonforClaw;

public sealed partial class MainWindow : Window
{
    private readonly StartupSettingsCoordinator _startupSettings;
    private bool _isLoadingStartupSettings;
    private readonly MainNavigationState _navigationState = new();
    private readonly ISystemStatusProvider _systemStatusProvider;
    private readonly IEnvironmentDiscoveryReportGenerator _environmentDiscoveryReportGenerator;
    private readonly IHidHideProvisioningReceiptStore _hidHideReceiptStore;
    private readonly IElevatedProcessRunner _prerequisiteSetupRunner;
    private readonly DeveloperTestModeState? _developerTestModeState;
    private bool _isInitializingTestMode;
    private SystemStatusSnapshot? _latestSystemStatus;
    private readonly ObservableCollection<StatusCardViewModel> _softwareCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _componentCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _externalControllerCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _runtimeCards = [];
    private int _isRefreshingStatus;
    private int _isGeneratingEnvironmentDiscoveryReport;
    private string? _environmentDiscoveryDirectory;
    private bool _setupPromptActive;
    private bool _setupPromptDeclinedForCurrentProcess;
    private bool _windowActivatedForUser;
    private bool _setupPromptPendingActivation;
    private bool _prerequisiteSetupInProgress;

    public MainWindow(
        StartupSettingsCoordinator startupSettings,
        string startupRegistrationMessage)
        : this(startupSettings, startupRegistrationMessage, null)
    {
    }

    internal MainWindow(
        StartupSettingsCoordinator startupSettings,
        string startupRegistrationMessage,
        RecoveryManager? recoveryManager,
        ISystemStatusProvider? systemStatusProvider = null,
        IEnvironmentDiscoveryReportGenerator? environmentDiscoveryReportGenerator = null,
        IHidHideProvisioningReceiptStore? hidHideReceiptStore = null,
        DeveloperTestModeState? developerTestModeState = null,
        IElevatedProcessRunner? prerequisiteSetupRunner = null)
    {
        _startupSettings = startupSettings ?? throw new ArgumentNullException(nameof(startupSettings));
        _systemStatusProvider = systemStatusProvider ?? CreateDefaultSystemStatusProvider();
        _hidHideReceiptStore = hidHideReceiptStore ?? new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath);
        _prerequisiteSetupRunner = prerequisiteSetupRunner ?? new ElevatedProcessRunner();
        _developerTestModeState = developerTestModeState;
        _environmentDiscoveryReportGenerator = environmentDiscoveryReportGenerator ?? new EnvironmentDiscoveryReportGenerator(
            new WindowsEnvironmentDiscoverySnapshotSource(),
            new EnvironmentDiscoveryReportStore(AppLog.DirectoryPath),
            new EnvironmentDiscoveryReportWriter());

        InitializeComponent();
        Title = FormatWindowTitle(GetDisplayVersion());
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        ApplyDefaultWindowSize();
        Closed += OnWindowClosed;
        Activated += OnWindowActivated;
        _isLoadingStartupSettings = true;
        LaunchAtWindowsStartupToggleSwitch.IsOn = _startupSettings.Settings.LaunchAtWindowsStartup;
        _isLoadingStartupSettings = false;
        _isInitializingTestMode = true;
        TestModeToggleSwitch.IsOn = _developerTestModeState?.IsEnabled == true;
        _isInitializingTestMode = false;
        StartupSettingsStatusText.Text = startupRegistrationMessage;
        ControllerSoftwareRepeater.ItemsSource = _softwareCards;
        RoutingComponentsRepeater.ItemsSource = _componentCards;
        ExternalControllersList.ItemsSource = _externalControllerCards;
        RuntimeStatusList.ItemsSource = _runtimeCards;
        MainNavigationView.SelectedItem = StatusNavigationItem;
        _ = RefreshSystemStatusAsync();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) return;
        if (_windowActivatedForUser)
        {
            if (_setupPromptPendingActivation) _ = RefreshSystemStatusAsync();
            return;
        }
        _windowActivatedForUser = true;
        _ = RefreshSystemStatusAsync();
    }

    public void UpdateSteamSessionState(SteamSessionState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _ = RefreshSystemStatusAsync();
        });
    }

    private void LaunchAtWindowsStartupToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoadingStartupSettings)
        {
            return;
        }

        var launchAtWindowsStartup = LaunchAtWindowsStartupToggleSwitch.IsOn;
        var result = _startupSettings.ChangeLaunchAtWindowsStartup(launchAtWindowsStartup);
        StartupSettingsStatusText.Text = result.Message;
    }

    private void TestModeToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_isInitializingTestMode && !_prerequisiteSetupInProgress)
            _developerTestModeState?.SetEnabled(TestModeToggleSwitch.IsOn);
    }


    private void DeveloperMenuButton_Click(object sender, RoutedEventArgs args)
    {
        var previousPage = _navigationState.CurrentPage;
        ShowPage(_navigationState.OpenDeveloperMenu());
        AppLog.Info("Window", "Developer menu opened.",
            ("PreviousPage", previousPage),
            ("CurrentPage", _navigationState.CurrentPage));
    }

    private async void GenerateEnvironmentDiscoveryReportButton_Click(object sender, RoutedEventArgs args)
    {
        if (_prerequisiteSetupInProgress) return;
        if (Interlocked.Exchange(ref _isGeneratingEnvironmentDiscoveryReport, 1) != 0) return;
        GenerateEnvironmentDiscoveryReportButton.IsEnabled = false;
        OpenEnvironmentDiscoveryFolderButton.IsEnabled = false;
        OpenEnvironmentDiscoveryFolderButton.Visibility = Visibility.Collapsed;
        EnvironmentDiscoveryReportStatusText.Text = "Generating...";
        try
        {
            var result = await _environmentDiscoveryReportGenerator.GenerateAsync();
            _environmentDiscoveryDirectory = result.DirectoryPath;
            EnvironmentDiscoveryReportStatusText.Text = $"Report generated successfully.{Environment.NewLine}{result.ReportFileName}";
            OpenEnvironmentDiscoveryFolderButton.Visibility = Visibility.Visible;
            OpenEnvironmentDiscoveryFolderButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("EnvironmentDiscovery", "Environment discovery report generation failed.", exception, ("Reason", exception.GetType().Name));
            EnvironmentDiscoveryReportStatusText.Text = "Report generation failed.\r\nSee the application log for details.";
        }
        finally
        {
            GenerateEnvironmentDiscoveryReportButton.IsEnabled = true;
            Volatile.Write(ref _isGeneratingEnvironmentDiscoveryReport, 0);
        }
    }

    private void OpenEnvironmentDiscoveryFolderButton_Click(object sender, RoutedEventArgs args)
    {
        if (_prerequisiteSetupInProgress) return;
        if (string.IsNullOrWhiteSpace(_environmentDiscoveryDirectory)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_environmentDiscoveryDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn("EnvironmentDiscovery", "Environment discovery folder could not be opened.", exception);
        }
    }

    private void DeveloperMenuBackButton_Click(object sender, RoutedEventArgs args)
    {
        ReturnToSettings("BackButton");
    }

    private void OnWindowClosed(object sender, WindowEventArgs args) { }

    private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selectedTag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        ShowPage(_navigationState.SelectNavigationItem(args.IsSettingsSelected, selectedTag));
    }

    private void ShowPage(MainNavigationPage page)
    {
        StatusContent.Visibility = page == MainNavigationPage.Status ? Visibility.Visible : Visibility.Collapsed;
        HowToUseContent.Visibility = page == MainNavigationPage.HowToUse ? Visibility.Visible : Visibility.Collapsed;
        SettingsContent.Visibility = page == MainNavigationPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        DeveloperMenuContent.Visibility = page == MainNavigationPage.DeveloperMenu ? Visibility.Visible : Visibility.Collapsed;
        if (page == MainNavigationPage.Status) _ = RefreshSystemStatusAsync();
    }

    private async void RefreshStatusButton_Click(object sender, RoutedEventArgs args) => await RefreshSystemStatusAsync();

    private async Task RefreshSystemStatusAsync()
    {
        if (_prerequisiteSetupInProgress) return;
        if (Interlocked.Exchange(ref _isRefreshingStatus, 1) != 0) return;
        RefreshStatusButton.IsEnabled = false;
        try { RenderSystemStatus(await _systemStatusProvider.CaptureAsync()); }
        catch (Exception exception) { AppLog.Warn("Status", "System status refresh failed.", exception, ("Reason", "SnapshotCaptureFailed")); }
        finally { RefreshStatusButton.IsEnabled = true; Volatile.Write(ref _isRefreshingStatus, 0); }
    }

    private void RenderSystemStatus(SystemStatusSnapshot snapshot)
    {
        _latestSystemStatus = snapshot;
        DeviceManufacturerText.Text = snapshot.Device.Manufacturer;
        DeviceModelText.Text = snapshot.Device.Model;
        DeviceSupportText.Text = snapshot.HardwareCompatibility.Status switch
        {
            HardwareCompatibilityStatus.Supported => "Supported",
            HardwareCompatibilityStatus.Unsupported => "Unsupported",
            _ => "Compatibility unknown"
        };
        DeviceBoardGpuText.Text = $"Board: {snapshot.Device.BaseBoardProduct}  GPU: {string.Join(", ", snapshot.Device.GpuModels)}";
        Replace(_softwareCards, snapshot.ControllerSoftware.Select(item => new StatusCardViewModel(item.DisplayName, FormatSoftwareStatus(item), item.Reason)));
        Replace(_componentCards,
        [
            new("HidHide", snapshot.Prerequisites.HidHide.Status.ToString(), snapshot.Prerequisites.HidHide.Reason),
            new("usbip-win2", snapshot.Prerequisites.UsbIpWin2.Status.ToString(), snapshot.Prerequisites.UsbIpWin2.Reason),
            new("VIIPER", snapshot.Prerequisites.Viiper.Status.ToString(), snapshot.Prerequisites.Viiper.Reason)
        ]);
        Replace(_externalControllerCards, ExternalControllerStatusCardFactory.Create(snapshot.ExternalController));
        var setup = EvaluateFirstTimeSetup(snapshot);
        var canInstall = setup.CanInstallRequiredComponents;
        var addonPresentation = FirstTimeSetupPresentation.GetAddonPresentation(setup, snapshot.Prerequisites, snapshot.Addon);
        Replace(_runtimeCards,
        [
            new("Steam", snapshot.Steam.IsActive ? "Active" : "Inactive", $"RunningAppID: {snapshot.Steam.RunningAppId}"),
            new("Steam Input Addon", addonPresentation.Status, addonPresentation.Reason)
        ]);
        if (PrerequisiteSetupPromptPolicy.IsInstallable(setup))
        {
            if (_windowActivatedForUser)
                _ = PromptForPrerequisiteSetupAsync();
            else
                RequestSetupPromptActivation();
        }
    }

    private void RequestSetupPromptActivation()
    {
        if (_setupPromptActive || _setupPromptDeclinedForCurrentProcess || _setupPromptPendingActivation) return;
        _setupPromptPendingActivation = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_windowActivatedForUser) return;
            AppWindow.Show();
            Activate();
        });
    }

    private async Task PromptForPrerequisiteSetupAsync()
    {
        if (_setupPromptActive || _setupPromptDeclinedForCurrentProcess || _prerequisiteSetupInProgress) return;
        if (Content.XamlRoot is null)
        {
            _setupPromptPendingActivation = true;
            return;
        }
        _setupPromptPendingActivation = false;
        _setupPromptActive = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Setup required",
                Content = "Steam Input Addon for Claw needs a few required components. Install them now?",
                PrimaryButtonText = "Install",
                CloseButtonText = "Not now",
                XamlRoot = Content.XamlRoot
            };
            AppLog.Info("PrerequisiteSetupPrompt", "Prerequisite setup prompt shown.", ("Action", "Shown"));
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                _setupPromptDeclinedForCurrentProcess = true;
                AppLog.Info("PrerequisiteSetupPrompt", "Prerequisite setup prompt declined.", ("Action", "Declined"));
                return;
            }
            AppLog.Info("PrerequisiteSetupPrompt", "Prerequisite setup prompt accepted.", ("Action", "Accepted"));
            await RunPrerequisiteSetupAsync();
        }
        catch (Exception exception) { AppLog.Warn("PrerequisiteSetup", "Prerequisite setup prompt failed.", exception); }
        finally { _setupPromptActive = false; }
    }

    private FirstTimeSetupAssessment EvaluateFirstTimeSetup(SystemStatusSnapshot snapshot)
    {
        var receipt = _hidHideReceiptStore.Load();
        var usbReceipt = new UsbIpWin2ProvisioningReceiptStore(VelopackAppPaths.UsbIpWin2ProvisioningReceiptPath).Load();
        var hidPackage = new WindowsHidHidePackageProbe().Inspect();
        var usbPackage = new WindowsUsbIpWin2PackageProbe().Inspect();
        var storage = ProvisioningStorageSecurity.Inspect(VelopackAppPaths.ProvisioningStateDirectory);
        var hidHideState = receipt.IsCorrupt || storage.Status is ProvisioningStorageStatus.Unsafe or ProvisioningStorageStatus.Indeterminate
            ? ComponentProvisioningState.Corrupt
            : receipt.Receipt is not null ? ToComponentProvisioningState(receipt.Receipt.State)
            : File.Exists(VelopackAppPaths.LegacyHidHideProvisioningReceiptPath) ? ComponentProvisioningState.Legacy
            : ComponentProvisioningState.None;
        var usbIpState = usbReceipt.IsCorrupt || storage.Status is ProvisioningStorageStatus.Unsafe or ProvisioningStorageStatus.Indeterminate
            ? ComponentProvisioningState.Corrupt
            : usbReceipt.Receipt is not null ? ToComponentProvisioningState(usbReceipt.Receipt.State)
            : ComponentProvisioningState.None;
        var hidInstallation = ComponentInstallationAssessmentPolicy.AssessHidHide(hidPackage, snapshot.Prerequisites.HidHide, HidHidePackageMetadata.BundledVersion.ToString());
        var usbInstallation = ComponentInstallationAssessmentPolicy.AssessUsbIp(usbPackage, snapshot.Prerequisites.UsbIpWin2, UsbIpWin2PackageMetadata.BundledVersion.ToString());
        return FirstTimeSetupPolicy.Evaluate(new FirstTimeSetupInput(
            snapshot.HardwareCompatibility, snapshot.Compatibility, snapshot.RecoverySafe, snapshot.ExternalController,
            SteamSessionState.FromRunningAppId(snapshot.Steam.IsActive ? snapshot.Steam.RunningAppId : 0),
            snapshot.Prerequisites.HidHide, snapshot.Prerequisites.UsbIpWin2, hidInstallation, usbInstallation, new(hidHideState, usbIpState)));
    }

    private async Task RunPrerequisiteSetupAsync()
    {
        if (_prerequisiteSetupInProgress) return;
        _prerequisiteSetupInProgress = true;
        UpdatePrerequisiteSetupBusyUi();
        try
        {
            var current = await _systemStatusProvider.CaptureAsync();
            var currentSetup = EvaluateFirstTimeSetup(current);
            AppLog.Info("PrerequisiteSetup", "Prerequisite setup requested.",
                ("HidHideStatus", current.Prerequisites.HidHide.Status),
                ("UsbIpWin2Status", current.Prerequisites.UsbIpWin2.Status),
                ("CompatibilityStatus", current.Compatibility.Status),
                ("CompatibilityReason", current.Compatibility.Reason),
                ("ExternalControllerStatus", current.ExternalController.Status),
                ("SteamActive", current.Steam.IsActive),
                ("RecoverySafe", current.RecoverySafe));
            if (!PrerequisiteSetupPromptPolicy.IsInstallable(currentSetup))
            {
                RenderSystemStatus(current);
                return;
            }
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable.");
            var result = await PrerequisiteSetupRunnerPolicy.RunIfInstallableAsync(
                currentSetup, _prerequisiteSetupRunner, executable, ElevatedPrerequisiteSetup.Argument, CancellationToken.None);
            if (result is null) return;
            var resultKind = ElevatedPrerequisiteSetup.TranslateExitCode(result);
            AppLog.Info("PrerequisiteSetup", "Elevated prerequisite setup finished.", ("Result", resultKind));
            if (resultKind == ElevatedPrerequisiteSetup.ResultKind.RebootRequired)
            {
                var restartDialog = new ContentDialog
                {
                    Title = "Restart required",
                    Content = "Windows needs to restart to finish setting up Steam Input Addon for Claw.",
                    PrimaryButtonText = "Restart now",
                    CloseButtonText = "Later",
                    XamlRoot = Content.XamlRoot
                };
                if (await restartDialog.ShowAsync() == ContentDialogResult.Primary)
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false });
            }
            else if (resultKind is ElevatedPrerequisiteSetup.ResultKind.Blocked or ElevatedPrerequisiteSetup.ResultKind.AlreadyInProgress ||
                     resultKind is ElevatedPrerequisiteSetup.ResultKind.Failed)
            {
                var message = resultKind == ElevatedPrerequisiteSetup.ResultKind.AlreadyInProgress
                    ? "Another setup operation is already in progress."
                    : "Setup couldn't be completed. Check Status or the application log for details.";
                await new ContentDialog
                {
                    Title = "Setup unavailable",
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                }.ShowAsync();
            }
        }
        finally
        {
            _prerequisiteSetupInProgress = false;
            UpdatePrerequisiteSetupBusyUi();
            await RefreshSystemStatusAsync();
        }
    }

    private void UpdatePrerequisiteSetupBusyUi()
    {
        PrerequisiteSetupBusyOverlay.Visibility = _prerequisiteSetupInProgress ? Visibility.Visible : Visibility.Collapsed;
        MainNavigationView.IsHitTestVisible = !_prerequisiteSetupInProgress;
        RefreshStatusButton.IsEnabled = !_prerequisiteSetupInProgress;
    }

    private void ShowStatusButton_Click(object sender, RoutedEventArgs args)
    {
        MainNavigationView.SelectedItem = StatusNavigationItem;
        ShowPage(_navigationState.SelectNavigationItem(false, "Status"));
    }

    private static void Replace(ObservableCollection<StatusCardViewModel> destination, IEnumerable<StatusCardViewModel> source) { destination.Clear(); foreach (var item in source) destination.Add(item); }
    internal static string FormatSoftwareStatus(ControllerSoftwareStatus item) => item.Runtime switch
    {
        SoftwareRuntimeStatus.Running => "Running",
        SoftwareRuntimeStatus.Starting => "Starting",
        SoftwareRuntimeStatus.Indeterminate => "Indeterminate",
        _ when item.Installation == SoftwareInstallationStatus.Installed => "Installed / Not running",
        _ when item.Installation == SoftwareInstallationStatus.NotInstalled => "Not installed",
        _ => "Indeterminate"
    };
    private static string FormatAddonStatus(AddonOperationalStatus status) => status switch { AddonOperationalStatus.WaitingForSteam => "Waiting for Steam", AddonOperationalStatus.SetupRequired => "Setup required", AddonOperationalStatus.RecoveryRequired => "Recovery required", AddonOperationalStatus.Unsupported => "Unsupported", _ => status.ToString() };
    private static ComponentProvisioningState ToComponentProvisioningState(HidHideProvisioningReceiptState state) => state switch
    {
        HidHideProvisioningReceiptState.Provisioned => ComponentProvisioningState.Provisioned,
        HidHideProvisioningReceiptState.InstallStarted => ComponentProvisioningState.InstallStarted,
        HidHideProvisioningReceiptState.InstalledPendingReboot => ComponentProvisioningState.PendingReboot,
        HidHideProvisioningReceiptState.AttemptFailed => ComponentProvisioningState.AttemptFailed,
        HidHideProvisioningReceiptState.AttemptCancelled => ComponentProvisioningState.AttemptCancelled,
        _ => ComponentProvisioningState.Indeterminate
    };
    private static ComponentProvisioningState ToComponentProvisioningState(UsbIpWin2ProvisioningReceiptState state) => state switch
    {
        UsbIpWin2ProvisioningReceiptState.Provisioned => ComponentProvisioningState.Provisioned,
        UsbIpWin2ProvisioningReceiptState.InstallStarted => ComponentProvisioningState.InstallStarted,
        UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot => ComponentProvisioningState.PendingReboot,
        UsbIpWin2ProvisioningReceiptState.AttemptFailed => ComponentProvisioningState.AttemptFailed,
        UsbIpWin2ProvisioningReceiptState.AttemptCancelled => ComponentProvisioningState.AttemptCancelled,
        _ => ComponentProvisioningState.Indeterminate
    };
    private static ISystemStatusProvider CreateDefaultSystemStatusProvider()
    {
        var devices = new WindowsControllerDeviceEnumerator();
        var adapter = new MsiClawDeviceAdapter(devices);
        var classifier = new ControllerDeviceClassifier(adapter.InternalControllerMatcher);
        return new SystemStatusProvider(new WindowsDeviceInformationProvider(),
        new WindowsDeviceProbeContextFactory(new WindowsDeviceIdentitySource(), devices),
        new HardwareCompatibilityEvaluator(new HandheldDeviceRegistry([adapter])),
        [new MsiCenterMSoftwareStatusProvider(), new ClawTweaksSoftwareStatusProvider(new ClawTweaksInstallationProbe(), new ClawTweaksRuntimeDetector()), new HandheldCompanionSoftwareStatusProvider(new HandheldCompanionRuntimeDetector())],
        new RuntimePrerequisiteInspector(new HidHidePrerequisiteInspector(new HidHideDriverClient()), new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(devices)), new ViiperRuntimeInspector()),
        () => SteamSessionState.FromRunningAppId(0), () => new ExternalControllerDetector(devices, classifier).Detect(), () => true);
    }

    private void MainNavigationView_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_navigationState.CurrentPage != MainNavigationPage.DeveloperMenu ||
            !args.GetCurrentPoint(MainNavigationView).Properties.IsXButton1Pressed)
        {
            return;
        }

        ReturnToSettings("MouseBackButton");
        args.Handled = true;
    }

    private void ReturnToSettings(string reason)
    {
        var previousPage = _navigationState.CurrentPage;
        ShowPage(_navigationState.ReturnToSettings());
        AppLog.Info("Window", "Developer menu closed.",
            ("PreviousPage", previousPage),
            ("CurrentPage", _navigationState.CurrentPage),
            ("Reason", reason));
    }

    private void MainNavigationView_Loaded(object sender, RoutedEventArgs args)
    {
        SetEnglishSettingsItemContent();
        if (_setupPromptPendingActivation && _windowActivatedForUser)
            _ = PromptForPrerequisiteSetupAsync();

        var navigationItems = MainNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .Append(MainNavigationView.SettingsItem)
            .OfType<NavigationViewItem>();
        var openPaneLength = navigationItems
            .Select(MeasureDesiredWidth)
            .DefaultIfEmpty(MainNavigationView.OpenPaneLength)
            .Max();

        MainNavigationView.OpenPaneLength = openPaneLength;
    }

    private void SetEnglishSettingsItemContent()
    {
        if (MainNavigationView.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.Content = "Settings";
        }
    }

    private static double MeasureDesiredWidth(NavigationViewItem item)
    {
        item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return item.DesiredSize.Width;
    }

    private void ApplyDefaultWindowSize()
    {
        var windowHandle = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(windowHandle);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        var workArea = displayArea?.WorkArea;
        var size = DpiAwareWindowSize.Calculate(dpi, workArea?.Width ?? 0, workArea?.Height ?? 0);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(size.Width, size.Height));

        if (workArea is not null)
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(
                workArea.Value.X + (workArea.Value.Width - size.Width) / 2,
                workArea.Value.Y + (workArea.Value.Height - size.Height) / 2));
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    private static string GetDisplayVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";
    }

    internal static string FormatWindowTitle(string version) => $"Steam Input Addon for Claw v{version}";

}
