using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SteamInputAddonforClaw.Windowing;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.ControllerLed;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Foundation;
using WinRT.Interop;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.UI;
using SteamInputAddonforClaw.Views;
using Windows.UI.ViewManagement;

namespace SteamInputAddonforClaw;

public sealed partial class MainWindow : Window
{
    private static readonly MainNavigationPage[] DeferredWarmupPages =
    [
        MainNavigationPage.Controller,
        MainNavigationPage.Settings,
        MainNavigationPage.Overlay,
        MainNavigationPage.Shortcut,
        MainNavigationPage.Tweaks,
        MainNavigationPage.Profile,
        MainNavigationPage.Xbox,
    ];

    private readonly IAddonFrontendControl _frontend;
    private readonly FrontendBootstrapSnapshot _bootstrap;
    private readonly MainUiThemePreferenceStore? _mainUiThemePreferenceStore;
    private MainUiTheme _mainUiThemePreference = MainUiTheme.System;
    private ElementTheme _systemAppThemeFallback = ElementTheme.Dark;
    private UISettings? _systemUiSettings;
    private readonly MainNavigationState _navigationState = new();
    private readonly InfoBadge _updateReadyDotBadge = new();
    private FrontendStatusSnapshot? _latestSystemStatus;
    private bool _isUpdateReady;
    // Bootstrap is captured once at startup and never re-fetched; a mutation made through the
    // frontend (e.g. suppressing the developer menu warning) must update this live flag directly,
    // or the same process would keep re-showing a warning the user already dismissed.
    private bool _suppressDeveloperMenuWarning;
    private int _isRefreshingStatus;
    private int _statusRefreshPending;
    private bool _windowActivatedForUser;
    private bool _mainAppRootLoaded;
    private bool _warmupStartQueued;
    private bool _warmupStarted;
    private bool _windowClosed;
    private bool _restoringNavigationSelection;
    private int _nextWarmupPageIndex;
    private MainNavigationPage _displayedPage = MainNavigationPage.Device;
    private readonly long _windowCreatedTimestamp = Stopwatch.GetTimestamp();
    private bool _prerequisiteSetupPendingActivation;
    private bool _prerequisiteSetupInProgress;
    private bool _prerequisiteSetupAttemptedForCurrentProcess;
    private DeveloperPage? _developerMenuContent;
    private ClawSensorProbePage? _clawSensorProbeContent;
    private FanHardwareProbePage? _fanHardwareProbeContent;
    private ControllerPage? _controllerContent;
    private ProfilePage? _profileContent;
    private XboxPage? _xboxContent;
    private OverlayPage? _overlayContent;
    private ShortcutPage? _shortcutContent;
    private WindowsTweaksPage? _tweaksContent;
    private SettingsPage? _settingsContent;
    private HowToUsePage? _howToUseContent;

    // App UI PR-C: the single ordered mutation path for the WHOLE front-button mapping. The
    // cross-button same-domain uniqueness rule belongs to one whole mapping, so there is one save
    // chain, not one per button. _frontButtonUiMapping is the latest edit the Controller page has
    // requested (advanced synchronously, before persistence starts); _frontButtonPersistedMapping is
    // the last value this window knows is actually on disk, used to roll a failed save back to.
    private Contracts.FrontButtons.FrontButtonMappingSettings _frontButtonUiMapping = Contracts.FrontButtons.FrontButtonMappingSettings.Default;
    private Contracts.FrontButtons.FrontButtonMappingSettings _frontButtonPersistedMapping = Contracts.FrontButtons.FrontButtonMappingSettings.Default;
    private Task _frontButtonSaveChain = Task.CompletedTask;
    private long _frontButtonEditVersion;

    private BackButtonMappingSettings _backButtonUiMapping = BackButtonMappingSettings.Default;
    private BackButtonMappingSettings _backButtonPersistedMapping = BackButtonMappingSettings.Default;
    private Task _backButtonSaveChain = Task.CompletedTask;
    private long _backButtonEditVersion;

    private ControllerLedSettings _controllerLedUiSettings = ControllerLedSettings.Default;
    private ControllerLedSettings _controllerLedPersistedSettings = ControllerLedSettings.Default;
    private Task _controllerLedSaveChain = Task.CompletedTask;
    private long _controllerLedEditVersion;
    private DispatcherQueueTimer? _controllerLedMutationTimer;

    internal MainWindow(
        IAddonFrontendControl frontend,
        FrontendBootstrapSnapshot bootstrap)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        MainUiThemePreferenceStore? themePreferenceStore = null;
        var mainUiThemePreference = MainUiTheme.System;
        try
        {
            themePreferenceStore = new MainUiThemePreferenceStore(bootstrap.LogDirectoryPath);
            mainUiThemePreference = themePreferenceStore.Load(out var loadFailure);
            if (loadFailure is not null)
                AppLog.Warn("Theme", "Main UI theme preference could not be read; using System.", loadFailure);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Theme", "Main UI theme preference path could not be initialized; using System.", exception);
        }
        _mainUiThemePreferenceStore = themePreferenceStore;
        _mainUiThemePreference = mainUiThemePreference;
        _suppressDeveloperMenuWarning = bootstrap.Settings.SuppressDeveloperMenuWarning;
        _frontButtonUiMapping = bootstrap.Settings.FrontButtonMapping;
        _frontButtonPersistedMapping = bootstrap.Settings.FrontButtonMapping;
        _backButtonUiMapping = bootstrap.Settings.BackButtonMapping;
        _backButtonPersistedMapping = bootstrap.Settings.BackButtonMapping;
        _controllerLedUiSettings = bootstrap.Settings.ControllerLed;
        _controllerLedPersistedSettings = bootstrap.Settings.ControllerLed;

        var initializeComponentTimer = Stopwatch.StartNew();
        InitializeComponent();
        initializeComponentTimer.Stop();
        AppLog.Info("Startup", "Main UI XAML initialized.",
            ("DurationMs", initializeComponentTimer.Elapsed.TotalMilliseconds));
        if (Application.Current.Resources.TryGetValue("InformationalDotInfoBadgeStyle", out var infoBadgeStyle)
            && infoBadgeStyle is Style style)
        {
            _updateReadyDotBadge.Style = style;
        }
        _systemAppThemeFallback = MainAppRoot.ActualTheme is ElementTheme.Light or ElementTheme.Dark
            ? MainAppRoot.ActualTheme
            : ElementTheme.Dark;
        ApplyMainUiTheme();
        InitializeSystemThemeTracking();
        Title = FormatWindowTitle(GetDisplayVersion());
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        ApplyDefaultWindowSize();
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
        DeviceContent.Initialize(_frontend);
        _controllerLedMutationTimer = DispatcherQueue.CreateTimer();
        _controllerLedMutationTimer.Interval = TimeSpan.FromMilliseconds(200);
        _controllerLedMutationTimer.IsRepeating = false;
        _controllerLedMutationTimer.Tick += ControllerLedMutationTimer_Tick;
        _frontend.StateInvalidated += OnFrontendStateInvalidated;
        MainNavigationView.SelectedItem = DeviceNavigationItem;
        _ = RefreshSystemStatusAsync();
        AppLog.Info("Startup", "MainWindow constructor completed.",
            ("DurationMs", Stopwatch.GetElapsedTime(_windowCreatedTimestamp).TotalMilliseconds));
    }

    private void OnFrontendStateInvalidated(object? sender, EventArgs args)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            RefreshInvalidatedFrontendStateOnUiThread();
            return;
        }

        if (!DispatcherQueue.TryEnqueue(RefreshInvalidatedFrontendStateOnUiThread))
        {
            AppLog.Info("Frontend", "Frontend state invalidation ignored because the UI dispatcher is unavailable.");
        }
    }

    private void RefreshInvalidatedFrontendStateOnUiThread()
    {
        if (_windowClosed) return;
        _ = RefreshSystemStatusAsync();
        _settingsContent?.RequestAppUpdateRefresh();
        _settingsContent?.RequestGamingHomeRefresh();
        _overlayContent?.RequestClawHudRefresh();
        _shortcutContent?.RequestRefresh();
        if (_navigationState.CurrentPage == MainNavigationPage.Tweaks)
            _tweaksContent?.RequestRefresh();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _windowClosed = true;
        CompositionTarget.Rendering -= OnInitialMainWindowRendering;
        if (!_warmupStarted || _nextWarmupPageIndex < DeferredWarmupPages.Length)
            AppLog.Info("Startup", "MainWindow closed before deferred page warm-up completed.",
                ("RemainingPages", Math.Max(0, DeferredWarmupPages.Length - _nextWarmupPageIndex)));
        if (_settingsContent is not null)
        {
            _settingsContent.AppThemeSelectionRequested -= OnAppThemeSelectionRequested;
            _settingsContent.AppUpdateSnapshotRendered -= OnAppUpdateSnapshotRendered;
            _settingsContent.DeveloperMenuRequested -= OnDeveloperMenuRequested;
        }
        if (_systemUiSettings is not null)
            _systemUiSettings.ColorValuesChanged -= SystemUiSettings_ColorValuesChanged;
        _frontend.StateInvalidated -= OnFrontendStateInvalidated;
        _controllerContent?.Deactivate();
        if (_controllerContent is not null)
        {
            _controllerContent.MappingEditRequested -= OnControllerMappingEditRequested;
            _controllerContent.BackButtonMappingEditRequested -= OnControllerBackButtonMappingEditRequested;
            _controllerContent.ControllerLedEditRequested -= OnControllerLedEditRequested;
        }
        if (_profileContent is not null)
            _profileContent.SteamActivityRefreshRequested -= OnSteamActivityRefreshRequested;
    }

    private SettingsPage GetOrCreateSettingsPage()
    {
        if (_settingsContent is not null) return _settingsContent;

        var page = new SettingsPage();
        page.AppThemeSelectionRequested += OnAppThemeSelectionRequested;
        page.AppUpdateSnapshotRendered += OnAppUpdateSnapshotRendered;
        page.DeveloperMenuRequested += OnDeveloperMenuRequested;
        page.Initialize(_bootstrap, _frontend, _mainUiThemePreference);
        if (_latestSystemStatus is { } status)
            page.RenderRequiredComponents(status);
        SettingsPageHost.Content = page;
        return _settingsContent = page;
    }

    private ControllerPage GetOrCreateControllerPage()
    {
        if (_controllerContent is not null) return _controllerContent;

        var page = new ControllerPage();
        page.Initialize(_bootstrap, _frontend, () => WindowNative.GetWindowHandle(this));
        page.ApplyFrontButtonMapping(_frontButtonUiMapping);
        page.ApplyBackButtonMapping(_backButtonUiMapping);
        page.ApplyControllerLedSettings(_controllerLedUiSettings);
        page.ApplyControllerForSteamSettings(_bootstrap.Settings);
        page.MappingEditRequested += OnControllerMappingEditRequested;
        page.BackButtonMappingEditRequested += OnControllerBackButtonMappingEditRequested;
        page.ControllerLedEditRequested += OnControllerLedEditRequested;
        ControllerPageHost.Content = page;
        return _controllerContent = page;
    }

    private ProfilePage GetOrCreateProfilePage()
    {
        if (_profileContent is not null) return _profileContent;

        var page = new ProfilePage();
        page.Initialize(_frontend);
        page.SetSteamActivity(_latestSystemStatus?.Steam.Active);
        page.SteamActivityRefreshRequested += OnSteamActivityRefreshRequested;
        ProfilePageHost.Content = page;
        return _profileContent = page;
    }

    private XboxPage GetOrCreateXboxPage()
    {
        if (_xboxContent is not null) return _xboxContent;

        var page = new XboxPage();
        page.Initialize(_frontend);
        XboxPageHost.Content = page;
        return _xboxContent = page;
    }

    private OverlayPage GetOrCreateOverlayPage()
    {
        if (_overlayContent is not null) return _overlayContent;

        var page = new OverlayPage();
        page.Initialize(_frontend);
        OverlayPageHost.Content = page;
        return _overlayContent = page;
    }

    private ShortcutPage GetOrCreateShortcutPage()
    {
        if (_shortcutContent is not null) return _shortcutContent;

        var page = new ShortcutPage();
        page.Initialize(_frontend, () => WindowNative.GetWindowHandle(this));
        ShortcutPageHost.Content = page;
        return _shortcutContent = page;
    }

    private WindowsTweaksPage GetOrCreateTweaksPage()
    {
        if (_tweaksContent is not null) return _tweaksContent;

        var page = new WindowsTweaksPage();
        page.Initialize(_frontend);
        TweaksPageHost.Content = page;
        return _tweaksContent = page;
    }

    private HowToUsePage GetOrCreateHowToUsePage()
    {
        if (_howToUseContent is not null) return _howToUseContent;

        var page = new HowToUsePage();
        HowToUsePageHost.Content = page;
        return _howToUseContent = page;
    }

    private void OnControllerMappingEditRequested(object? sender, Contracts.FrontButtons.FrontButtonMappingSettings mapping)
        => QueueFrontButtonMutation(mapping);

    private void OnControllerBackButtonMappingEditRequested(object? sender, BackButtonMappingSettings mapping)
        => QueueBackButtonMutation(mapping);

    private void OnControllerLedEditRequested(object? sender, ControllerLedSettings settings)
        => QueueControllerLedMutation(settings);

    private void OnAppThemeSelectionRequested(MainUiTheme theme)
    {
        if (theme == _mainUiThemePreference)
        {
            _settingsContent?.SetAppThemeSelection(_mainUiThemePreference);
            return;
        }

        Exception? saveFailure = null;
        if (_mainUiThemePreferenceStore is null
            || !_mainUiThemePreferenceStore.TrySave(theme, out saveFailure))
        {
            AppLog.Warn("Theme", "Main UI theme preference could not be saved; retaining the previous selection.",
                saveFailure ?? new IOException("The theme preference store is unavailable."));
            _settingsContent?.SetAppThemeSelection(
                _mainUiThemePreference,
                "Theme could not be saved. The previous selection was kept.");
            return;
        }

        _mainUiThemePreference = theme;
        ApplyMainUiTheme();
        _settingsContent?.SetAppThemeSelection(_mainUiThemePreference);
        AppLog.Info("Theme", "Main UI theme changed.", ("Theme", _mainUiThemePreference));
    }

    private void OnAppUpdateSnapshotRendered(FrontendUpdateSnapshot snapshot)
    {
        _isUpdateReady = snapshot.CanInstall;
        Title = FormatWindowTitle(GetDisplayVersion(), _isUpdateReady);
        ApplyUpdateReadySettingsBadge();
    }

    private void ApplyUpdateReadySettingsBadge()
    {
        if (MainNavigationView.SettingsItem is NavigationViewItem settingsItem)
            settingsItem.InfoBadge = _isUpdateReady ? _updateReadyDotBadge : null;
    }

    private void ApplyMainUiTheme()
    {
        var theme = _mainUiThemePreference switch
        {
            MainUiTheme.Light => ElementTheme.Light,
            MainUiTheme.Dark => ElementTheme.Dark,
            _ => ReadWindowsAppTheme(),
        };

        MainAppRoot.RequestedTheme = theme;
        AppWindow.TitleBar.PreferredTheme = theme == ElementTheme.Dark
            ? TitleBarTheme.Dark
            : TitleBarTheme.Light;
    }

    private ElementTheme ReadWindowsAppTheme()
    {
        try
        {
            using var personalization = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            var appsUseLightTheme = personalization?.GetValue("AppsUseLightTheme") as int?;
            _systemAppThemeFallback = MainUiThemePreferenceStore.ResolveSystemTheme(
                appsUseLightTheme,
                _systemAppThemeFallback);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Warn("Theme", "Windows app appearance could not be read; retaining the last known system theme.", exception);
        }

        return _systemAppThemeFallback;
    }

    private void InitializeSystemThemeTracking()
    {
        try
        {
            _systemUiSettings = new UISettings();
            _systemUiSettings.ColorValuesChanged += SystemUiSettings_ColorValuesChanged;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Theme", "Windows appearance change notifications are unavailable.", exception);
        }
    }

    private void SystemUiSettings_ColorValuesChanged(UISettings sender, object args)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (_mainUiThemePreference == MainUiTheme.System)
                    ApplyMainUiTheme();
            }))
        {
            AppLog.Debug("Theme", "Windows appearance change ignored because the UI dispatcher is unavailable.");
        }
    }

    private void OnSteamActivityRefreshRequested(object? sender, EventArgs args)
    {
        _profileContent?.SetSteamActivity(null);
        _ = RefreshSystemStatusAsync();
    }

    internal void DeactivateFanHardwareProbeForUiShutdown() => _fanHardwareProbeContent?.Deactivate();

    internal async Task CloseClawSensorProbeForUiShutdownAsync()
    {
        if (_clawSensorProbeContent is not null)
            await _clawSensorProbeContent.DeactivateAsync().ConfigureAwait(true);
        await _frontend.CloseClawSensorProbeAsync().ConfigureAwait(true);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) return;
        if (_windowActivatedForUser)
        {
            if (_prerequisiteSetupPendingActivation) _ = RefreshSystemStatusAsync();
            return;
        }
        _windowActivatedForUser = true;
        AppLog.Info("Startup", "MainWindow activated.",
            ("ElapsedMs", Stopwatch.GetElapsedTime(_windowCreatedTimestamp).TotalMilliseconds));
        TryScheduleWarmupAfterInitialDisplay();
        _ = RefreshSystemStatusAsync();
    }

    private void MainAppRoot_Loaded(object sender, RoutedEventArgs args)
    {
        _mainAppRootLoaded = true;
        TryScheduleWarmupAfterInitialDisplay();
    }

    private void TryScheduleWarmupAfterInitialDisplay()
    {
        if (_windowClosed || !_windowActivatedForUser || !_mainAppRootLoaded || _warmupStartQueued)
            return;

        _warmupStartQueued = true;
        CompositionTarget.Rendering += OnInitialMainWindowRendering;
    }

    private void OnInitialMainWindowRendering(object? sender, object args)
    {
        CompositionTarget.Rendering -= OnInitialMainWindowRendering;
        if (_windowClosed) return;

        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, StartDeferredPageWarmup))
            AppLog.Debug("Startup", "Deferred page warm-up was not queued because the UI dispatcher is unavailable.");
    }

    private void StartDeferredPageWarmup()
    {
        if (_windowClosed || _warmupStarted) return;

        _warmupStarted = true;
        AppLog.Debug("Startup", "Deferred Main UI page warm-up started after the initial render opportunity.",
            ("ElapsedMs", Stopwatch.GetElapsedTime(_windowCreatedTimestamp).TotalMilliseconds));
        WarmNextPage();
    }

    private void WarmNextPage()
    {
        if (_windowClosed) return;

        while (_nextWarmupPageIndex < DeferredWarmupPages.Length
            && IsMainPageMaterialized(DeferredWarmupPages[_nextWarmupPageIndex]))
        {
            _nextWarmupPageIndex++;
        }

        if (_nextWarmupPageIndex >= DeferredWarmupPages.Length)
        {
            AppLog.Debug("Startup", "Deferred Main UI page warm-up completed.",
                ("ElapsedMs", Stopwatch.GetElapsedTime(_windowCreatedTimestamp).TotalMilliseconds));
            return;
        }

        var page = DeferredWarmupPages[_nextWarmupPageIndex++];
        var pageTimer = Stopwatch.StartNew();
        var created = TryMaterializeMainPage(page);
        pageTimer.Stop();
        AppLog.Debug("Startup", "Deferred Main UI page warm-up step completed.",
            ("Page", page),
            ("Created", created),
            ("DurationMs", pageTimer.Elapsed.TotalMilliseconds));

        if (_windowClosed) return;
        if (_nextWarmupPageIndex >= DeferredWarmupPages.Length)
        {
            AppLog.Debug("Startup", "Deferred Main UI page warm-up completed.",
                ("ElapsedMs", Stopwatch.GetElapsedTime(_windowCreatedTimestamp).TotalMilliseconds));
            return;
        }

        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, WarmNextPage))
            AppLog.Debug("Startup", "Remaining deferred pages will be created on demand because the UI dispatcher is unavailable.");
    }

    private bool IsMainPageMaterialized(MainNavigationPage page) => page switch
    {
        MainNavigationPage.Controller => _controllerContent is not null,
        MainNavigationPage.Settings => _settingsContent is not null,
        MainNavigationPage.Overlay => _overlayContent is not null,
        MainNavigationPage.Shortcut => _shortcutContent is not null,
        MainNavigationPage.Tweaks => _tweaksContent is not null,
        MainNavigationPage.Profile => _profileContent is not null,
        MainNavigationPage.Xbox => _xboxContent is not null,
        _ => true,
    };

    private bool TryMaterializeMainPage(MainNavigationPage page)
    {
        try
        {
            switch (page)
            {
                case MainNavigationPage.Controller: _ = GetOrCreateControllerPage(); break;
                case MainNavigationPage.Settings: _ = GetOrCreateSettingsPage(); break;
                case MainNavigationPage.Overlay: _ = GetOrCreateOverlayPage(); break;
                case MainNavigationPage.Shortcut: _ = GetOrCreateShortcutPage(); break;
                case MainNavigationPage.Tweaks: _ = GetOrCreateTweaksPage(); break;
                case MainNavigationPage.Profile: _ = GetOrCreateProfilePage(); break;
                case MainNavigationPage.Xbox: _ = GetOrCreateXboxPage(); break;
                case MainNavigationPage.HowToUse: _ = GetOrCreateHowToUsePage(); break;
            }

            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Window", "Main UI page could not be materialized.", exception, ("Page", page));
            return false;
        }
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

    private DeveloperPage GetOrCreateDeveloperMenu()
    {
        if (_developerMenuContent is not null) return _developerMenuContent;

        var page = new DeveloperPage();
        page.Initialize(_frontend, _bootstrap, () => _prerequisiteSetupInProgress);
        page.BackRequested += (_, _) => ReturnToSettings("BackButton");
        page.SensorProbeRequested += (_, _) => ShowPage(_navigationState.OpenClawSensorProbe());
        page.FanHardwareProbeRequested += (_, _) => ShowPage(_navigationState.OpenFanHardwareProbe());
        return _developerMenuContent = page;
    }

    private ClawSensorProbePage GetOrCreateClawSensorProbePage()
    {
        if (_clawSensorProbeContent is not null) return _clawSensorProbeContent;

        var page = new ClawSensorProbePage();
        page.Initialize(_frontend);
        page.BackRequested += (_, _) => ShowPage(_navigationState.ReturnToDeveloperMenu());
        return _clawSensorProbeContent = page;
    }

    private FanHardwareProbePage GetOrCreateFanHardwareProbePage()
    {
        if (_fanHardwareProbeContent is not null) return _fanHardwareProbeContent;

        var page = new FanHardwareProbePage();
        page.Initialize(_frontend);
        page.BackRequested += (_, _) => ShowPage(_navigationState.ReturnToDeveloperMenu());
        return _fanHardwareProbeContent = page;
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

    private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_restoringNavigationSelection) return;
        var selectedTag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        ShowPage(_navigationState.SelectNavigationItem(args.IsSettingsSelected, selectedTag));
    }

    private void ShowPage(MainNavigationPage page)
    {
        if (_windowClosed) return;
        if (!TryMaterializeMainPage(page))
        {
            _navigationState.RestoreCurrentPage(_displayedPage);
            RestoreNavigationSelection(_displayedPage);
            return;
        }

        var wasController = ControllerPageHost.Visibility == Visibility.Visible;
        var wasDevice = DeviceContent.Visibility == Visibility.Visible;
        var wasProfile = ProfilePageHost.Visibility == Visibility.Visible;
        var wasXbox = XboxPageHost.Visibility == Visibility.Visible;
        var wasShortcut = ShortcutPageHost.Visibility == Visibility.Visible;
        var wasClawSensorProbe = _clawSensorProbeContent is not null &&
            ReferenceEquals(DeveloperContentHost.Content, _clawSensorProbeContent);
        var wasFanHardwareProbe = _fanHardwareProbeContent is not null &&
            ReferenceEquals(DeveloperContentHost.Content, _fanHardwareProbeContent);
        DeveloperContentHost.Content = page switch
        {
            MainNavigationPage.DeveloperMenu => GetOrCreateDeveloperMenu(),
            MainNavigationPage.ClawSensorProbe => GetOrCreateClawSensorProbePage(),
            MainNavigationPage.FanHardwareProbe => GetOrCreateFanHardwareProbePage(),
            _ => null
        };
        DeveloperContentHost.Visibility = DeveloperContentHost.Content is null ? Visibility.Collapsed : Visibility.Visible;
        DeviceContent.Visibility = page == MainNavigationPage.Device ? Visibility.Visible : Visibility.Collapsed;
        ProfilePageHost.Visibility = page == MainNavigationPage.Profile ? Visibility.Visible : Visibility.Collapsed;
        XboxPageHost.Visibility = page == MainNavigationPage.Xbox ? Visibility.Visible : Visibility.Collapsed;
        ControllerPageHost.Visibility = page == MainNavigationPage.Controller ? Visibility.Visible : Visibility.Collapsed;
        OverlayPageHost.Visibility = page == MainNavigationPage.Overlay ? Visibility.Visible : Visibility.Collapsed;
        ShortcutPageHost.Visibility = page == MainNavigationPage.Shortcut ? Visibility.Visible : Visibility.Collapsed;
        TweaksPageHost.Visibility = page == MainNavigationPage.Tweaks ? Visibility.Visible : Visibility.Collapsed;
        HowToUsePageHost.Visibility = page == MainNavigationPage.HowToUse ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageHost.Visibility = page == MainNavigationPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        if (page == MainNavigationPage.HowToUse) _howToUseContent!.Activate();
        if (page == MainNavigationPage.Controller)
        {
            _controllerContent!.Activate();
            _ = RefreshSystemStatusAsync();
        }
        else if (wasController) _controllerContent?.Deactivate();
        if (page == MainNavigationPage.Device) DeviceContent.Activate();
        else if (wasDevice) DeviceContent.Deactivate();
        if (page == MainNavigationPage.Profile)
        {
            _profileContent!.SetSteamActivity(null);
            _profileContent.Activate();
            _ = RefreshSystemStatusAsync();
        }
        else if (wasProfile) _profileContent?.Deactivate();
        if (page == MainNavigationPage.Xbox) _xboxContent!.Activate();
        else if (wasXbox) _xboxContent?.Deactivate();
        if (page == MainNavigationPage.Overlay) _overlayContent!.Activate();
        if (page == MainNavigationPage.Shortcut) _shortcutContent!.Activate();
        else if (wasShortcut) _shortcutContent?.Deactivate();
        if (page == MainNavigationPage.Tweaks) _tweaksContent!.Activate();
        if (page == MainNavigationPage.ClawSensorProbe) _clawSensorProbeContent!.Activate();
        else if (wasClawSensorProbe) _clawSensorProbeContent?.Deactivate();
        if (page == MainNavigationPage.FanHardwareProbe) _fanHardwareProbeContent!.Activate();
        else if (wasFanHardwareProbe) _fanHardwareProbeContent?.Deactivate();
        _displayedPage = page;
    }

    private void RestoreNavigationSelection(MainNavigationPage page)
    {
        var selection = page switch
        {
            MainNavigationPage.Settings or MainNavigationPage.DeveloperMenu or MainNavigationPage.ClawSensorProbe
                or MainNavigationPage.FanHardwareProbe or MainNavigationPage.CenterMButton => MainNavigationView.SettingsItem,
            _ => MainNavigationView.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, NavigationTagFor(page), StringComparison.Ordinal)),
        };

        if (selection is null) return;
        _restoringNavigationSelection = true;
        try { MainNavigationView.SelectedItem = selection; }
        finally { _restoringNavigationSelection = false; }
    }

    private static string? NavigationTagFor(MainNavigationPage page) => page switch
    {
        MainNavigationPage.Device => "Device",
        MainNavigationPage.Controller => "Controller",
        MainNavigationPage.Profile => "Profile",
        MainNavigationPage.Xbox => "Xbox",
        MainNavigationPage.Overlay => "Overlay",
        MainNavigationPage.Shortcut => "Shortcut",
        MainNavigationPage.Tweaks => "Tweaks",
        MainNavigationPage.HowToUse => "HowToUse",
        _ => null,
    };

    private async Task RefreshSystemStatusAsync()
    {
        if (_prerequisiteSetupInProgress)
        {
            _profileContent?.SetSteamActivity(null);
            return;
        }
        if (Interlocked.Exchange(ref _isRefreshingStatus, 1) != 0)
        {
            Volatile.Write(ref _statusRefreshPending, 1);
            return;
        }
        try { RenderSystemStatus(await _frontend.CaptureStatusAsync()); }
        catch (Exception exception)
        {
            _profileContent?.SetSteamActivity(null);
            if (exception is FrontendTransportException
                && exception is not FrontendProtocolException
                && exception is not FrontendRemoteException)
            {
                if (_latestSystemStatus is { } lastStatus)
                {
                    var unavailableStatus = DeviceSummaryPresentation.WithUnavailableControllerBadge(lastStatus);
                    _latestSystemStatus = unavailableStatus;
                    DeviceContent.RenderDeviceSummary(unavailableStatus);
                }
                AppLog.Debug("Status", "System status refresh skipped because Runtime transport is unavailable.",
                    ("Reason", exception.Message));
            }
            else
            {
                AppLog.Warn("Status", "System status refresh failed.", exception, ("Reason", "SnapshotCaptureFailed"));
            }
        }
        finally
        {
            Volatile.Write(ref _isRefreshingStatus, 0);
            if (Interlocked.Exchange(ref _statusRefreshPending, 0) != 0)
                DispatcherQueue.TryEnqueue(() => _ = RefreshSystemStatusAsync());
        }
    }

    private void RenderSystemStatus(FrontendStatusSnapshot snapshot)
    {
        _latestSystemStatus = snapshot;
        _profileContent?.SetSteamActivity(snapshot.Steam.Active);
        DeviceContent.RenderDeviceSummary(snapshot);
        _settingsContent?.RenderRequiredComponents(snapshot);
        if (snapshot.CanInstallRequiredComponents)
        {
            if (_windowActivatedForUser)
                _ = RunPrerequisiteSetupAsync();
            else
                RequestPrerequisiteSetupActivation();
        }
    }

    private void RequestPrerequisiteSetupActivation()
    {
        if (_prerequisiteSetupAttemptedForCurrentProcess || _prerequisiteSetupPendingActivation) return;
        _prerequisiteSetupPendingActivation = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_windowActivatedForUser) return;
            AppWindow.Show();
            Activate();
        });
    }

    private async Task RunPrerequisiteSetupAsync()
    {
        if (_prerequisiteSetupAttemptedForCurrentProcess || _prerequisiteSetupInProgress) return;
        if (Content.XamlRoot is null)
        {
            _prerequisiteSetupPendingActivation = true;
            return;
        }
        _prerequisiteSetupPendingActivation = false;
        _prerequisiteSetupAttemptedForCurrentProcess = true;
        _prerequisiteSetupInProgress = true;
        UpdatePrerequisiteSetupBusyUi();
        try
        {
            var result = await _frontend.RunPrerequisiteSetupAsync();
            AppLog.Info("PrerequisiteSetup", "Prerequisite setup worker finished.", ("Result", result.Result));
            if (result.Status is not null)
                RenderSystemStatus(result.Status);
            await HandlePrerequisiteSetupResultAsync(result.Result);
        }
        catch (Exception exception) { AppLog.Warn("PrerequisiteSetup", "Automatic prerequisite setup failed.", exception); }
        finally
        {
            _prerequisiteSetupInProgress = false;
            UpdatePrerequisiteSetupBusyUi();
            RequestStatusRefresh();
        }
    }

    private async Task HandlePrerequisiteSetupResultAsync(FrontendPrerequisiteSetupResultKind resultKind)
    {
        if (resultKind is FrontendPrerequisiteSetupResultKind.Ready
            or FrontendPrerequisiteSetupResultKind.Installed
            or FrontendPrerequisiteSetupResultKind.RebootRequired)
        {
            await DeviceContent.ConfirmCenterMDisableAfterPrerequisiteSetupAsync();
            return;
        }

        if (Content.XamlRoot is null || resultKind is not (FrontendPrerequisiteSetupResultKind.Blocked
            or FrontendPrerequisiteSetupResultKind.AlreadyInProgress
            or FrontendPrerequisiteSetupResultKind.Failed))
            return;

        var message = resultKind == FrontendPrerequisiteSetupResultKind.AlreadyInProgress
            ? "Another setup operation is already in progress."
            : "Setup couldn't be completed. Check Settings > Required Components or the application log for details.";
        await new ContentDialog
        {
            Title = "Setup unavailable",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot
        }.ShowAsync();
    }

    private void UpdatePrerequisiteSetupBusyUi()
    {
        PrerequisiteSetupBusyOverlay.Visibility = _prerequisiteSetupInProgress ? Visibility.Visible : Visibility.Collapsed;
        MainNavigationView.IsHitTestVisible = !_prerequisiteSetupInProgress;
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
                ShowPage(_navigationState.ReturnToDeveloperMenu());
                break;
            case MainNavigationPage.Controller:
                break;
        }
    }

    /// <summary>
    /// The single ordered mutation path for the WHOLE front-button mapping (App UI PR-C §19).
    /// Advances the current edit synchronously and pushes it back into the Controller page
    /// immediately -- before persistence even starts. The actual save is chained behind whatever is
    /// already in flight, and only the save that is still the newest edit when it completes is
    /// allowed to touch the controls again. One whole-record chain, never one per button, so the
    /// cross-button uniqueness rule cannot produce a lost update.
    /// </summary>
    private void QueueFrontButtonMutation(Contracts.FrontButtons.FrontButtonMappingSettings next)
    {
        _frontButtonUiMapping = next;
        var version = ++_frontButtonEditVersion;

        _controllerContent?.ApplyFrontButtonMapping(next);

        _frontButtonSaveChain = SaveFrontButtonAfterAsync(_frontButtonSaveChain, next, version);
    }

    private async Task SaveFrontButtonAfterAsync(Task previous, Contracts.FrontButtons.FrontButtonMappingSettings next, long version)
    {
        try { await previous; }
        catch { /* observed where it happened */ }

        try
        {
            var result = await _frontend.SetFrontButtonMappingAsync(next);
            _frontButtonPersistedMapping = result.FrontButtonMapping;

            if (version != _frontButtonEditVersion) return;

            _frontButtonUiMapping = result.FrontButtonMapping;
            _controllerContent?.ApplyFrontButtonMapping(result.FrontButtonMapping);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Window", "Front-button mapping save failed.", exception);
            if (version != _frontButtonEditVersion) return;

            _frontButtonUiMapping = _frontButtonPersistedMapping;
            _controllerContent?.ApplyFrontButtonMapping(_frontButtonPersistedMapping);
        }
    }

    /// <summary>The single ordered mutation path for the complete Xbox360 M1/M2 mapping record.</summary>
    private void QueueBackButtonMutation(BackButtonMappingSettings next)
    {
        _backButtonUiMapping = next;
        var version = ++_backButtonEditVersion;

        _controllerContent?.ApplyBackButtonMapping(next);

        _backButtonSaveChain = SaveBackButtonAfterAsync(_backButtonSaveChain, next, version);
    }

    private async Task SaveBackButtonAfterAsync(Task previous, BackButtonMappingSettings next, long version)
    {
        try { await previous; }
        catch { /* observed where it happened */ }

        try
        {
            var result = await _frontend.SetBackButtonMappingAsync(next);
            _backButtonPersistedMapping = result.BackButtonMapping;

            if (version != _backButtonEditVersion) return;

            _backButtonUiMapping = result.BackButtonMapping;
            _controllerContent?.ApplyBackButtonMapping(result.BackButtonMapping);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Window", "Back-button mapping save failed.", exception);
            if (version != _backButtonEditVersion) return;

            _backButtonUiMapping = _backButtonPersistedMapping;
            _controllerContent?.ApplyBackButtonMapping(_backButtonPersistedMapping);
        }
    }

    private void QueueControllerLedMutation(ControllerLedSettings next)
    {
        _controllerLedUiSettings = next;
        _controllerLedEditVersion++;
        _controllerContent?.ApplyControllerLedSettings(next);
        _controllerLedMutationTimer?.Stop();
        _controllerLedMutationTimer?.Start();
    }

    private void ControllerLedMutationTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        var version = _controllerLedEditVersion;
        _controllerLedSaveChain = SaveControllerLedAfterAsync(_controllerLedSaveChain, _controllerLedUiSettings, version);
    }

    private async Task SaveControllerLedAfterAsync(Task previous, ControllerLedSettings next, long version)
    {
        try { await previous; }
        catch { /* observed where it happened */ }

        try
        {
            var result = await _frontend.SetControllerLedSettingsAsync(next);
            _controllerLedPersistedSettings = result.ControllerLed;
            if (version != _controllerLedEditVersion) return;
            _controllerLedUiSettings = result.ControllerLed;
            _controllerContent?.ApplyControllerLedSettings(result.ControllerLed);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Window", "Controller LED settings save/apply request failed; restoring the last persisted preference.", exception);
            if (version != _controllerLedEditVersion) return;
            _controllerLedUiSettings = _controllerLedPersistedSettings;
            _controllerContent?.ApplyControllerLedSettings(_controllerLedPersistedSettings);
        }
    }

    /// <summary>Waits for queued controller settings and flushes the shared LED debounce timer.</summary>
    internal Task DrainPendingControllerMappingSavesAsync()
    {
        if (_controllerLedMutationTimer?.IsRunning == true)
        {
            _controllerLedMutationTimer.Stop();
            var version = _controllerLedEditVersion;
            _controllerLedSaveChain = SaveControllerLedAfterAsync(_controllerLedSaveChain, _controllerLedUiSettings, version);
        }
        var front = _frontButtonSaveChain;
        var back = _backButtonSaveChain;
        var led = _controllerLedSaveChain;
        return Task.WhenAll(front, back, led);
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
        SetLocalizedSettingsItemContent();
        ApplyUpdateReadySettingsBadge();
        if (_prerequisiteSetupPendingActivation && _windowActivatedForUser)
            _ = RefreshSystemStatusAsync();

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

    private void SetLocalizedSettingsItemContent()
    {
        if (MainNavigationView.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.Content = MainUiStrings.Get("MainNav_Settings", "Settings");
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

    internal static string FormatWindowTitle(string version, bool updateReady = false)
    {
        var title = $"Steam Addon for Claw v{version}";
        return updateReady ? title + " — Update Available" : title;
    }

}
