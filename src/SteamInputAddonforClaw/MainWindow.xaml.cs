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
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
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
using SteamInputAddonforClaw.Startup;

namespace SteamInputAddonforClaw;

public sealed partial class MainWindow : Window
{
    private readonly StartupSettingsCoordinator _startupSettings;
    private readonly Func<IDirectInputDeviceEnumerator> _directInputEnumeratorFactory;
    private readonly RecoveryManager _recoveryManager;
    private MsiClawInputSource? _msiClawInputSource;
    private M1M2DiagnosticCoordinator? _m1M2DiagnosticCoordinator;
    private bool _isLoadingStartupSettings;
    private readonly MainNavigationState _navigationState = new();
    private readonly ISystemStatusProvider _systemStatusProvider;
    private readonly ObservableCollection<StatusCardViewModel> _softwareCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _componentCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _runtimeCards = [];
    private int _isRefreshingStatus;

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
        ISystemStatusProvider? systemStatusProvider = null)
    {
        _startupSettings = startupSettings ?? throw new ArgumentNullException(nameof(startupSettings));
        _recoveryManager = recoveryManager ?? new RecoveryManager(new RecoveryJournalStore(VelopackAppPaths.RecoveryJournalPath), hidHideClient: new HidHideDriverClient());
        _systemStatusProvider = systemStatusProvider ?? CreateDefaultSystemStatusProvider();

        InitializeComponent();
        Title = FormatWindowTitle(GetDisplayVersion());
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        ApplyDefaultWindowSize();
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _directInputEnumeratorFactory = () => new VorticeDirectInputDeviceEnumerator(windowHandle);
        Closed += OnWindowClosed;
        VersionText.Text = $"Version {GetDisplayVersion()}";
        _isLoadingStartupSettings = true;
        LaunchAtWindowsStartupToggleSwitch.IsOn = _startupSettings.Settings.LaunchAtWindowsStartup;
        _isLoadingStartupSettings = false;
        StartupSettingsStatusText.Text = startupRegistrationMessage;
        ControllerSoftwareList.ItemsSource = _softwareCards;
        RoutingComponentsList.ItemsSource = _componentCards;
        RuntimeStatusList.ItemsSource = _runtimeCards;
        MainNavigationView.SelectedItem = StatusNavigationItem;
        _ = RefreshSystemStatusAsync();
    }

    public void UpdateSteamSessionState(SteamSessionState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _ = RefreshSystemStatusAsync();
        });
    }

    public void UpdateExternalControllerAssessment(ExternalControllerAssessment assessment)
    {
        _ = RefreshSystemStatusAsync();
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

    private void DeveloperMenuButton_Click(object sender, RoutedEventArgs args)
    {
        var previousPage = _navigationState.CurrentPage;
        ShowPage(_navigationState.OpenDeveloperMenu());
        AppLog.Info("Window", "Developer menu opened.",
            ("PreviousPage", previousPage),
            ("CurrentPage", _navigationState.CurrentPage));
    }

    private void DeveloperMenuBackButton_Click(object sender, RoutedEventArgs args)
    {
        ReturnToSettings("BackButton");
    }

    private void StartM1M2TestButton_Click(object sender, RoutedEventArgs args)
    {
        _m1M2DiagnosticCoordinator ??= CreateM1M2DiagnosticCoordinator();
        var result = _m1M2DiagnosticCoordinator.Start();
        M1M2TestStatusText.Text = $"Status: {result.Message}";
        if (result.Started)
        {
            M1TestStatusText.Text = "M1: Waiting";
            M2TestStatusText.Text = "M2: Waiting";
            IndependentTestStatusText.Text = "Independent: Waiting";
            StartM1M2TestButton.IsEnabled = false;
            StopM1M2TestButton.IsEnabled = true;
        }
    }

    private async void StopM1M2TestButton_Click(object sender, RoutedEventArgs args)
    {
        if (_m1M2DiagnosticCoordinator is not null)
        {
            await _m1M2DiagnosticCoordinator.StopAsync();
        }
    }

    private void OnMsiClawInputStateChanged(object? sender, ControllerState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (MsiClawInputSource.IsM1Pressed(state)) M1TestStatusText.Text = "M1: OK";
            if (MsiClawInputSource.IsM2Pressed(state)) M2TestStatusText.Text = "M2: OK";
        });
    }

    private void OnMsiClawInputIndependentVerified(object? sender, EventArgs args)
    {
        DispatcherQueue.TryEnqueue(() => IndependentTestStatusText.Text = "Independent: OK");
    }

    private void OnMsiClawInputTestCompleted(object? sender, MsiClawInputTestSummary summary)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            M1M2TestStatusText.Text = $"Status: Completed ({summary.DurationMs} ms, {summary.StopReason})";
            if (summary.Independent) IndependentTestStatusText.Text = "Independent: OK";
            StartM1M2TestButton.IsEnabled = true;
            StopM1M2TestButton.IsEnabled = false;
        });
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_m1M2DiagnosticCoordinator is not null)
        {
            var source = _msiClawInputSource!;
            source.StateChanged -= OnMsiClawInputStateChanged;
            source.IndependentVerified -= OnMsiClawInputIndependentVerified;
            source.TestCompleted -= OnMsiClawInputTestCompleted;
            _m1M2DiagnosticCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private MsiClawInputSource CreateMsiClawInputSource()
    {
        var source = new MsiClawInputSource(_directInputEnumeratorFactory);
        source.StateChanged += OnMsiClawInputStateChanged;
        source.IndependentVerified += OnMsiClawInputIndependentVerified;
        source.TestCompleted += OnMsiClawInputTestCompleted;
        return source;
    }

    private M1M2DiagnosticCoordinator CreateM1M2DiagnosticCoordinator()
    {
        _msiClawInputSource ??= CreateMsiClawInputSource();
        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable.");
        return new M1M2DiagnosticCoordinator(_msiClawInputSource, new HidHideDriverClient(), _recoveryManager, executablePath, ResolveMsiDirectInputHidInstanceIds);
    }

    private static IReadOnlyList<string> ResolveMsiDirectInputHidInstanceIds() =>
        MsiClawHardware.ResolveDirectInputHidInstanceIds(new WindowsControllerDeviceEnumerator().EnumeratePresentDevices());

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
        if (Interlocked.Exchange(ref _isRefreshingStatus, 1) != 0) return;
        RefreshStatusButton.IsEnabled = false;
        try { RenderSystemStatus(await _systemStatusProvider.CaptureAsync()); }
        catch (Exception exception) { AppLog.Warn("Status", "System status refresh failed.", exception, ("Reason", "SnapshotCaptureFailed")); }
        finally { RefreshStatusButton.IsEnabled = true; Volatile.Write(ref _isRefreshingStatus, 0); }
    }

    private void RenderSystemStatus(SystemStatusSnapshot snapshot)
    {
        DeviceManufacturerText.Text = snapshot.Device.Manufacturer;
        DeviceModelText.Text = snapshot.Device.Model;
        DeviceGpuText.Text = $"GPU: {string.Join(Environment.NewLine, snapshot.Device.GpuModels)}";
        Replace(_softwareCards, snapshot.ControllerSoftware.Select(item => new StatusCardViewModel(item.DisplayName, FormatSoftwareStatus(item), item.Reason)));
        Replace(_componentCards,
        [
            new("HidHide", snapshot.Prerequisites.HidHide.Status.ToString(), snapshot.Prerequisites.HidHide.Reason),
            new("usbip-win2", snapshot.Prerequisites.UsbIpWin2.Status.ToString(), snapshot.Prerequisites.UsbIpWin2.Reason),
            new("VIIPER", snapshot.Prerequisites.Viiper.Status.ToString(), snapshot.Prerequisites.Viiper.Reason)
        ]);
        Replace(_runtimeCards,
        [
            new("Steam", snapshot.Steam.IsActive ? "Active" : "Inactive", $"RunningAppID: {snapshot.Steam.RunningAppId}"),
            new("External Controller", snapshot.ExternalController.Status == ExternalControllerAssessmentStatus.Clear ? "None" : snapshot.ExternalController.Status == ExternalControllerAssessmentStatus.ExternalPresent ? "External detected" : "Indeterminate", snapshot.ExternalController.Status == ExternalControllerAssessmentStatus.ExternalPresent ? $"{snapshot.ExternalController.DetectedExternalControllerCount} external physical controller(s) detected." : "Read-only PnP assessment."),
            new("Steam Input Addon", FormatAddonStatus(snapshot.Addon.Status), snapshot.Addon.Reason)
        ]);
    }

    private static void Replace(ObservableCollection<StatusCardViewModel> destination, IEnumerable<StatusCardViewModel> source) { destination.Clear(); foreach (var item in source) destination.Add(item); }
    private static string FormatSoftwareStatus(ControllerSoftwareStatus item) => item.Runtime == SoftwareRuntimeStatus.Running ? "Running" : item.Installation == SoftwareInstallationStatus.Installed ? "Installed / Not running" : item.Installation == SoftwareInstallationStatus.NotInstalled ? "Not installed" : "Indeterminate";
    private static string FormatAddonStatus(AddonOperationalStatus status) => status switch { AddonOperationalStatus.WaitingForSteam => "Waiting for Steam", AddonOperationalStatus.SetupRequired => "Setup required", AddonOperationalStatus.RecoveryRequired => "Recovery required", _ => status.ToString() };

    private static ISystemStatusProvider CreateDefaultSystemStatusProvider()
    {
        var devices = new WindowsControllerDeviceEnumerator();
        var classifier = new ControllerDeviceClassifier();
        return new SystemStatusProvider(new WindowsDeviceInformationProvider(devices),
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
