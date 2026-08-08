using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Windowing;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Foundation;
using WinRT.Interop;

namespace SteamInputAddonforClaw;

public sealed partial class MainWindow : Window
{
    private readonly StartupSettingsCoordinator _startupSettings;
    private readonly MsiClawInputSource _msiClawInputSource;

    public MainWindow(
        StartupSettingsCoordinator startupSettings,
        string startupRegistrationMessage)
    {
        _startupSettings = startupSettings ?? throw new ArgumentNullException(nameof(startupSettings));

        InitializeComponent();
        ApplyDefaultWindowSize();
        _msiClawInputSource = new MsiClawInputSource(new VorticeDirectInputDeviceEnumerator(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        _msiClawInputSource.StateChanged += OnMsiClawInputStateChanged;
        _msiClawInputSource.TestCompleted += OnMsiClawInputTestCompleted;
        Closed += OnWindowClosed;
        VersionText.Text = $"Version {GetDisplayVersion()}";
        LaunchAtWindowsStartupCheckBox.IsChecked = _startupSettings.Settings.LaunchAtWindowsStartup;
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

    private void LaunchAtWindowsStartupCheckBox_Click(object sender, RoutedEventArgs args)
    {
        var launchAtWindowsStartup = LaunchAtWindowsStartupCheckBox.IsChecked == true;
        var result = _startupSettings.ChangeLaunchAtWindowsStartup(launchAtWindowsStartup);
        StartupSettingsStatusText.Text = result.Message;
    }

    private void StartM1M2TestButton_Click(object sender, RoutedEventArgs args)
    {
        var result = _msiClawInputSource.Start();
        M1M2TestStatusText.Text = $"Status: {result.Message}";
        if (result.Started)
        {
            StartM1M2TestButton.IsEnabled = false;
            StopM1M2TestButton.IsEnabled = true;
        }
    }

    private async void StopM1M2TestButton_Click(object sender, RoutedEventArgs args)
    {
        await _msiClawInputSource.StopAsync();
    }

    private void OnMsiClawInputStateChanged(object? sender, ControllerState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (state.M1) M1TestStatusText.Text = "M1: OK";
            if (state.M2) M2TestStatusText.Text = "M2: OK";
            if (state.M1 != state.M2) IndependentTestStatusText.Text = "Independent: OK";
        });
    }

    private void OnMsiClawInputTestCompleted(object? sender, MsiClawInputTestSummary summary)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            M1M2TestStatusText.Text = $"Status: Completed ({summary.DurationMs} ms)";
            StartM1M2TestButton.IsEnabled = true;
            StopM1M2TestButton.IsEnabled = false;
        });
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _msiClawInputSource.StateChanged -= OnMsiClawInputStateChanged;
        _msiClawInputSource.TestCompleted -= OnMsiClawInputTestCompleted;
        _msiClawInputSource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selectedTag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        var page = args.IsSettingsSelected
            ? MainNavigationPage.Settings
            : selectedTag switch
            {
                "HowToUse" => MainNavigationPage.HowToUse,
                _ => MainNavigationPage.Status
            };

        StatusContent.Visibility = page == MainNavigationPage.Status ? Visibility.Visible : Visibility.Collapsed;
        HowToUseContent.Visibility = page == MainNavigationPage.HowToUse ? Visibility.Visible : Visibility.Collapsed;
        SettingsContent.Visibility = page == MainNavigationPage.Settings ? Visibility.Visible : Visibility.Collapsed;
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

    private enum MainNavigationPage
    {
        Status,
        HowToUse,
        Settings
    }
}
