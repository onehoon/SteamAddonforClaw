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
    private MsiClawInputSource? _msiClawInputSource;
    private bool _isLoadingStartupSettings;
    private readonly MainNavigationState _navigationState = new();

    public MainWindow(
        StartupSettingsCoordinator startupSettings,
        string startupRegistrationMessage)
    {
        _startupSettings = startupSettings ?? throw new ArgumentNullException(nameof(startupSettings));

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
        _msiClawInputSource ??= CreateMsiClawInputSource();
        var result = _msiClawInputSource.Start();
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
        if (_msiClawInputSource is not null)
        {
            await _msiClawInputSource.StopAsync();
        }
    }

    private void OnMsiClawInputStateChanged(object? sender, ControllerState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (state.M1) M1TestStatusText.Text = "M1: OK";
            if (state.M2) M2TestStatusText.Text = "M2: OK";
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
        if (_msiClawInputSource is not null)
        {
            _msiClawInputSource.StateChanged -= OnMsiClawInputStateChanged;
            _msiClawInputSource.IndependentVerified -= OnMsiClawInputIndependentVerified;
            _msiClawInputSource.TestCompleted -= OnMsiClawInputTestCompleted;
            _msiClawInputSource.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
