using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Windowing;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Foundation;
using WinRT.Interop;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw;

public sealed partial class MainWindow : Window
{
    private readonly IAddonFrontendControl _frontend;
    private readonly FrontendBootstrapSnapshot _bootstrap;
    private readonly MainNavigationState _navigationState = new();
    private FrontendStatusSnapshot? _latestSystemStatus;
    // Bootstrap is captured once at startup and never re-fetched; a mutation made through the
    // frontend (e.g. suppressing the developer menu warning) must update this live flag directly,
    // or the same process would keep re-showing a warning the user already dismissed.
    private bool _suppressDeveloperMenuWarning;
    private int _isRefreshingStatus;
    private int _statusRefreshPending;
    private bool _setupPromptActive;
    private bool _setupPromptDeclinedForCurrentProcess;
    private bool _windowActivatedForUser;
    private bool _setupPromptPendingActivation;
    private bool _prerequisiteSetupInProgress;

    internal MainWindow(
        IAddonFrontendControl frontend,
        FrontendBootstrapSnapshot bootstrap)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _suppressDeveloperMenuWarning = bootstrap.Settings.SuppressDeveloperMenuWarning;

        InitializeComponent();
        Title = FormatWindowTitle(GetDisplayVersion());
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        ApplyDefaultWindowSize();
        Closed += OnWindowClosed;
        Activated += OnWindowActivated;
        SettingsContent.Initialize(_frontend, _bootstrap);
        ControllerContent.Initialize(_frontend, _bootstrap);
        SettingsContent.DeveloperMenuRequested += OnDeveloperMenuRequested;
        DeveloperMenuContent.Initialize(_frontend, _bootstrap, () => _prerequisiteSetupInProgress);
        DeveloperMenuContent.BackRequested += (_, _) => ReturnToSettings("BackButton");
        DeveloperMenuContent.ClawSensorProbeRequested += (_, _) => OpenClawSensorProbe();
        ClawSensorProbeContent.Initialize(() => _latestSystemStatus);
        _frontend.StateInvalidated += OnFrontendStateInvalidated;
        ClawSensorProbeContent.ReturnToDeveloperMenuRequested += (_, _) => ShowPage(_navigationState.ReturnToDeveloperMenu());
        StatusContent.RefreshRequested += (_, _) => _ = RefreshSystemStatusAsync();
        MainNavigationView.SelectedItem = StatusNavigationItem;
        _ = RefreshSystemStatusAsync();
    }

    private void OnFrontendStateInvalidated(object? sender, EventArgs args) => RequestStatusRefresh();

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

    internal void RequestStatusRefresh()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _ = RefreshSystemStatusAsync();
        });
    }

    private void OpenDeveloperMenu()
    {
        var previousPage = _navigationState.CurrentPage;
        ShowPage(_navigationState.OpenDeveloperMenu());
        AppLog.Info("Window", "Developer menu opened.",
            ("PreviousPage", previousPage),
            ("CurrentPage", _navigationState.CurrentPage));
    }

    private async void OnDeveloperMenuRequested(object? sender, EventArgs args)
    {
        if (_suppressDeveloperMenuWarning)
        {
            OpenDeveloperMenu();
            return;
        }

        if (Content.XamlRoot is null)
        {
            AppLog.Warn("DeveloperMenu", "Developer menu warning could not be shown because the window has no XamlRoot.");
            return;
        }

        var suppressWarningCheckBox = new CheckBox { Content = "Don't show this warning again" };
        var dialog = new ContentDialog
        {
            Title = "Developer Menu",
            Content = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Developer features are intended for testing and diagnostics. Changing these settings may affect normal application behavior.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    suppressWarningCheckBox
                }
            },
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (suppressWarningCheckBox.IsChecked == true)
        {
            var settings = await _frontend.SuppressDeveloperMenuWarningAsync();
            _suppressDeveloperMenuWarning = settings.SuppressDeveloperMenuWarning;
        }

        OpenDeveloperMenu();
    }

    private async void OpenClawSensorProbe()
    {
        await ClawSensorProbeContent.PrepareForShowAsync();
        ShowPage(_navigationState.OpenClawSensorProbe());
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        await ClawSensorProbeContent.ShutdownAsync();
    }

    private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selectedTag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        ShowPage(_navigationState.SelectNavigationItem(args.IsSettingsSelected, selectedTag));
    }

    private void ShowPage(MainNavigationPage page)
    {
        StatusContent.Visibility = page == MainNavigationPage.Status ? Visibility.Visible : Visibility.Collapsed;
        ControllerContent.Visibility = page == MainNavigationPage.Controller ? Visibility.Visible : Visibility.Collapsed;
        HowToUseContent.Visibility = page == MainNavigationPage.HowToUse ? Visibility.Visible : Visibility.Collapsed;
        SettingsContent.Visibility = page == MainNavigationPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        DeveloperMenuContent.Visibility = page == MainNavigationPage.DeveloperMenu ? Visibility.Visible : Visibility.Collapsed;
        ClawSensorProbeContent.Visibility = page == MainNavigationPage.ClawSensorProbe ? Visibility.Visible : Visibility.Collapsed;
        if (page == MainNavigationPage.Status) _ = RefreshSystemStatusAsync();
    }

    private async Task RefreshSystemStatusAsync()
    {
        if (_prerequisiteSetupInProgress) return;
        if (Interlocked.Exchange(ref _isRefreshingStatus, 1) != 0)
        {
            Volatile.Write(ref _statusRefreshPending, 1);
            return;
        }
        StatusContent.SetRefreshing(true);
        try { RenderSystemStatus(await _frontend.CaptureStatusAsync()); }
        catch (Exception exception) { AppLog.Warn("Status", "System status refresh failed.", exception, ("Reason", "SnapshotCaptureFailed")); }
        finally
        {
            StatusContent.SetRefreshing(false);
            Volatile.Write(ref _isRefreshingStatus, 0);
            if (Interlocked.Exchange(ref _statusRefreshPending, 0) != 0)
                DispatcherQueue.TryEnqueue(() => _ = RefreshSystemStatusAsync());
        }
    }

    private void RenderSystemStatus(FrontendStatusSnapshot snapshot)
    {
        _latestSystemStatus = snapshot;
        StatusContent.Render(snapshot);
        if (snapshot.CanInstallRequiredComponents)
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

    private async Task RunPrerequisiteSetupAsync()
    {
        if (_prerequisiteSetupInProgress) return;
        _prerequisiteSetupInProgress = true;
        UpdatePrerequisiteSetupBusyUi();
        try
        {
            var result = await _frontend.RunPrerequisiteSetupAsync();
            AppLog.Info("PrerequisiteSetup", "Elevated prerequisite setup finished.", ("Result", result.Result));
            if (result.Status is not null)
                RenderSystemStatus(result.Status);
            await ShowPrerequisiteSetupResultDialogAsync(result.Result);
        }
        finally
        {
            _prerequisiteSetupInProgress = false;
            UpdatePrerequisiteSetupBusyUi();
            RequestStatusRefresh();
        }
    }

    private async Task ShowPrerequisiteSetupResultDialogAsync(FrontendPrerequisiteSetupResultKind resultKind)
    {
        if (Content.XamlRoot is null) return;

        if (resultKind == FrontendPrerequisiteSetupResultKind.NotInstallable)
            return;

        if (resultKind == FrontendPrerequisiteSetupResultKind.RebootRequired)
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
        else if (resultKind is FrontendPrerequisiteSetupResultKind.Blocked or FrontendPrerequisiteSetupResultKind.AlreadyInProgress or FrontendPrerequisiteSetupResultKind.Failed)
        {
            var message = resultKind == FrontendPrerequisiteSetupResultKind.AlreadyInProgress
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
        // Ready/Installed/Cancelled/NotInstallable need no dialog: Ready/Installed complete silently (Status
        // already reflects the new state), and Cancelled mirrors the prompt's own "Not now" path.
    }

    private void UpdatePrerequisiteSetupBusyUi()
    {
        PrerequisiteSetupBusyOverlay.Visibility = _prerequisiteSetupInProgress ? Visibility.Visible : Visibility.Collapsed;
        MainNavigationView.IsHitTestVisible = !_prerequisiteSetupInProgress;
        StatusContent.SetRefreshing(_prerequisiteSetupInProgress);
    }

    private async void MainNavigationView_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(MainNavigationView).Properties.IsXButton1Pressed ||
            _navigationState.GetMouseBackDestination() is not { } destination)
        {
            return;
        }

        args.Handled = true;
        switch (destination)
        {
            case MainNavigationPage.Settings:
                ReturnToSettings("MouseBackButton");
                break;
            case MainNavigationPage.DeveloperMenu:
                await ClawSensorProbeContent.ReturnToDeveloperMenuAsync();
                break;
        }
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
