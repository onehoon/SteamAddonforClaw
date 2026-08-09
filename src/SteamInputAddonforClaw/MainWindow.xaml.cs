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
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Windowing;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Foundation;
using WinRT.Interop;

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

    public MainWindow(
        StartupSettingsCoordinator startupSettings,
        string startupRegistrationMessage)
        : this(startupSettings, startupRegistrationMessage, null)
    {
    }

    internal MainWindow(
        StartupSettingsCoordinator startupSettings,
        string startupRegistrationMessage,
        RecoveryManager? recoveryManager)
    {
        _startupSettings = startupSettings ?? throw new ArgumentNullException(nameof(startupSettings));
        _recoveryManager = recoveryManager ?? new RecoveryManager(new RecoveryJournalStore(VelopackAppPaths.RecoveryJournalPath), new HidHideDriverClient());

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
        MainNavigationView.SelectedItem = StatusNavigationItem;
    }

    public void UpdateSteamSessionState(SteamSessionState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = state.IsActive ? "Steam session active" : "Steam inactive";
            RunningAppIdText.Text = $"RunningAppID: {state.RunningAppId}";
        });
    }

    public void UpdateExternalControllerAssessment(ExternalControllerAssessment assessment)
    {
        ExternalControllerText.Text = assessment.Status switch
        {
            ExternalControllerAssessmentStatus.Clear => "External controller: None",
            ExternalControllerAssessmentStatus.ExternalPresent => "External controller: Detected",
            _ => "External controller: Indeterminate"
        };
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

    private static IReadOnlyList<string> ResolveMsiDirectInputHidInstanceIds() => new WindowsControllerDeviceEnumerator().EnumeratePresentDevices()
        .Where(device => device.Present && device.VendorId == 0x0DB0 && device.ProductId == 0x1902 &&
            device.InstanceId.StartsWith("HID\\VID_0DB0&PID_1902&MI_00&COL01\\", StringComparison.OrdinalIgnoreCase))
        .Select(device => device.InstanceId)
        .ToArray();

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
