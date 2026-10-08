using System.Xml.Linq;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Windows.Foundation;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UiArchitectureTests
{
    [Fact]
    public void Project_references_preserve_the_headless_dependency_direction()
    {
        var ui = References("src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj");
        var runtime = References("src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj");
        var transport = References("src/SteamInputAddonforClaw.FrontendTransport/SteamInputAddonforClaw.FrontendTransport.csproj");

        Assert.Contains("SteamInputAddonforClaw.Contracts.csproj", ui);
        Assert.Contains("SteamInputAddonforClaw.FrontendTransport.csproj", ui);
        Assert.Contains("SteamInputAddonforClaw.FrontendTransport.csproj", runtime);
        Assert.Contains("SteamInputAddonforClaw.Contracts.csproj", transport);
        Assert.DoesNotContain("SteamInputAddonforClaw.csproj", ui);
        Assert.DoesNotContain("SteamInputAddonforClaw.UI.csproj", runtime);
        Assert.DoesNotContain("SteamInputAddonforClaw.csproj", transport);
    }

    [Fact]
    public void Frontend_sources_have_one_physical_owner()
    {
        var root = FindRepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml")));
        Assert.True(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/MainWindow.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/MainWindow.xaml.cs")));
        // The Claw Sensor Probe UI was restored (work order "restore-claw-sensor-probe-diagnostic")
        // as a proper frontend-boundary page: the WinUI page lives ONLY in the UI project, and the
        // Runtime-owned coordinator it talks to over IAddonFrontendControl lives ONLY in Runtime --
        // never the old pre-PR213 shape where the page held the coordinator directly in-process.
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/Views/ClawSensorProbePage.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/Views/ClawSensorProbePage.xaml.cs")));
        Assert.True(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml")));
        Assert.True(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml.cs")));
        Assert.True(Directory.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe")));
    }

    [Fact]
    public void Developer_surfaces_are_materialized_only_when_their_navigation_page_is_first_opened()
    {
        var root = FindRepositoryRoot();
        var mainWindowXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));
        var constructor = ExtractMethod(mainWindow, "internal MainWindow(");
        var showPage = ExtractMethod(mainWindow, "private void ShowPage(");
        XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        var developerContentHost = XDocument.Parse(mainWindowXaml).Descendants()
            .Single(element => (string?)element.Attribute(xamlNamespace + "Name") == "DeveloperContentHost");
        var developerPageTypes = new[]
        {
            "DeveloperPage",
            "VibrationTestPage",
            "ClawSensorProbePage",
            "FanHardwareProbePage",
            "BatteryChargeLimitTestPage",
            "GameInputSystemButtonProbePage"
        };

        Assert.Contains("x:Name=\"DeveloperContentHost\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Equal("Stretch", (string?)developerContentHost.Attribute("HorizontalContentAlignment"));
        Assert.Equal("Stretch", (string?)developerContentHost.Attribute("VerticalContentAlignment"));
        foreach (var pageType in developerPageTypes)
        {
            Assert.DoesNotContain($"<views:{pageType}", mainWindowXaml, StringComparison.Ordinal);
            Assert.DoesNotContain($"new {pageType}(", constructor, StringComparison.Ordinal);
            Assert.Equal(1, mainWindow.Split($"new {pageType}(", StringSplitOptions.None).Length - 1);
        }

        Assert.Contains("DeveloperContentHost.Content = page switch", showPage, StringComparison.Ordinal);
        foreach (var page in new[]
        {
            (Navigation: "DeveloperMenu", Type: "DeveloperPage"),
            (Navigation: "VibrationTest", Type: "VibrationTestPage"),
            (Navigation: "ClawSensorProbe", Type: "ClawSensorProbePage"),
            (Navigation: "FanHardwareProbe", Type: "FanHardwareProbePage"),
            (Navigation: "BatteryChargeLimitTest", Type: "BatteryChargeLimitTestPage"),
            (Navigation: "GameInputSystemButtonProbe", Type: "GameInputSystemButtonProbePage")
        })
        {
            Assert.Contains($"MainNavigationPage.{page.Navigation} => GetOrCreate", showPage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Developer_page_invalidation_shutdown_and_navigation_guards_do_not_materialize_pages()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));
        var invalidation = ExtractMethod(mainWindow, "private void RefreshInvalidatedFrontendStateOnUiThread()");
        var vibrationShutdown = ExtractMethod(mainWindow, "internal async Task CloseVibrationTestForUiShutdownAsync()");
        var gameInputShutdown = ExtractMethod(mainWindow, "internal async Task CloseGameInputSystemButtonProbeForUiShutdownAsync()");
        var sensorShutdown = ExtractMethod(mainWindow, "internal async Task CloseClawSensorProbeForUiShutdownAsync()");
        var batteryGuard = ExtractMethod(mainWindow, "private bool IsBatteryValidationBlockingNavigation()");

        Assert.DoesNotContain("GetOrCreate", invalidation, StringComparison.Ordinal);
        Assert.DoesNotContain("DeveloperContentHost", invalidation, StringComparison.Ordinal);
        Assert.Contains("if (_vibrationTestContent is not null)", vibrationShutdown, StringComparison.Ordinal);
        Assert.Contains("_fanHardwareProbeContent?.Deactivate()", vibrationShutdown, StringComparison.Ordinal);
        Assert.Contains("if (_gameInputSystemButtonProbeContent is not null)", gameInputShutdown, StringComparison.Ordinal);
        Assert.Contains("if (_clawSensorProbeContent is not null)", sensorShutdown, StringComparison.Ordinal);
        Assert.Contains("_batteryChargeLimitTestContent is { IsValidationRunning: true }", batteryGuard, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(DeveloperContentHost.Content, _batteryChargeLimitTestContent)", batteryGuard, StringComparison.Ordinal);
    }

    [Fact]
    public void Developer_diagnostic_leave_paths_preserve_stop_and_sensor_session_cleanup()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));
        var sensorPage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml.cs"));
        var vibrationPage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs"));
        var gameInputPage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/GameInputSystemButtonProbePage.xaml.cs"));
        var showPage = ExtractMethod(mainWindow, "private void ShowPage(");
        var sensorActivate = ExtractMethod(sensorPage, "internal void Activate()");
        var sensorDeactivate = ExtractMethod(sensorPage, "internal async Task DeactivateAsync()");
        var sensorTimer = ExtractMethod(sensorPage, "private void StartPollTimer()");

        Assert.Contains("else if (wasVibrationTest) _vibrationTestContent?.Deactivate()", showPage, StringComparison.Ordinal);
        Assert.Contains("else if (wasGameInputSystemButtonProbe) _gameInputSystemButtonProbeContent?.Deactivate()", showPage, StringComparison.Ordinal);
        Assert.Contains("StopIfRunningAsync()", ExtractMethod(vibrationPage, "internal void Deactivate()"), StringComparison.Ordinal);
        Assert.Contains("StopIfRunningAsync(forceRequest: true)", ExtractMethod(gameInputPage, "internal void Deactivate()"), StringComparison.Ordinal);
        Assert.Contains("StartPollTimer();", sensorActivate, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(200)", sensorTimer, StringComparison.Ordinal);
        Assert.Contains("_pollTimer?.Stop()", sensorDeactivate, StringComparison.Ordinal);
        Assert.Contains("_pageCancellation?.Cancel()", sensorDeactivate, StringComparison.Ordinal);
        Assert.Contains("CloseClawSensorProbeAsync()", sensorDeactivate, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_gameinput_probe_is_created_on_first_use_and_shutdown_only_if_created()
    {
        var root = FindRepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
        var gameInputCapture = ExtractMethod(host, "private Task<FrontendGameInputSystemButtonProbeSnapshot> CaptureGameInputSystemButtonProbeAsync(");
        var gameInputStart = ExtractMethod(host, "private Task<FrontendGameInputSystemButtonProbeSnapshot> StartGameInputSystemButtonProbeAsync(");
        var gameInputStop = ExtractMethod(host, "private Task<FrontendGameInputSystemButtonProbeSnapshot> StopGameInputSystemButtonProbeAsync(");
        var hostShutdown = ExtractMethod(host, "public async ValueTask DisposeAsync()");

        Assert.Contains("private GameInputSystemButtonProbe? _gameInputSystemButtonProbe;", host, StringComparison.Ordinal);
        Assert.Contains("_gameInputSystemButtonProbe ??= new GameInputSystemButtonProbe()", gameInputCapture, StringComparison.Ordinal);
        Assert.Contains("_gameInputSystemButtonProbe ??= new GameInputSystemButtonProbe()", gameInputStart, StringComparison.Ordinal);
        Assert.Contains("_gameInputSystemButtonProbe?.Stop()", gameInputStop, StringComparison.Ordinal);
        Assert.DoesNotContain("new GameInputSystemButtonProbe()", gameInputStop, StringComparison.Ordinal);
        Assert.Contains("if (_gameInputSystemButtonProbe is not null)", hostShutdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_is_true_headless_and_ui_keeps_winui_ownership()
    {
        var root = FindRepositoryRoot();
        var runtimeProject = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj"));
        var uiProject = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj"));
        Assert.DoesNotContain("<UseWinUI>true</UseWinUI>", runtimeProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", runtimeProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CommunityToolkit.WinUI", runtimeProject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<UseWinUI>true</UseWinUI>", uiProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<Page Include=\"MainWindow.xaml\"", runtimeProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<PackageReference Include=\"Microsoft.WindowsAppSDK\"", runtimeProject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UI_keeps_meta_package_and_overlay_uses_framework_dependent_component_contract()
    {
        var root = FindRepositoryRoot();
        var uiProject = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj"));
        var overlayProject = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj"));

        foreach (var project in new[] { uiProject, overlayProject })
        {
            Assert.Contains("<WindowsPackageType>None</WindowsPackageType>", project, StringComparison.Ordinal);
            Assert.Contains("<UseWinUI>true</UseWinUI>", project, StringComparison.Ordinal);
            Assert.Contains("<SelfContained>false</SelfContained>", project, StringComparison.Ordinal);
            Assert.Contains("<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>", project, StringComparison.Ordinal);
            Assert.Contains("<RuntimeIdentifier>win-x64</RuntimeIdentifier>", project, StringComparison.Ordinal);
            Assert.DoesNotContain("Bootstrap.Initialize", project, StringComparison.Ordinal);
        }

        Assert.Contains("<PackageReference Include=\"Microsoft.WindowsAppSDK\" Version=\"2.5.1\"", uiProject, StringComparison.Ordinal);
        Assert.DoesNotContain("<PackageReference Include=\"Microsoft.WindowsAppSDK\" ", overlayProject, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Microsoft.WindowsAppSDK.Runtime\" Version=\"2.5.1\"", overlayProject, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Microsoft.WindowsAppSDK.WinUI\" Version=\"2.3.9\"", overlayProject, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Microsoft.WindowsAppSDK.InteractiveExperiences\" Version=\"2.1.9\"", overlayProject, StringComparison.Ordinal);
    }

    [Fact]
    public void Surface_prerequisite_is_checked_before_any_visible_surface_mutation()
    {
        var root = FindRepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

        var warmup = ExtractMethod(host, "private async Task StartOverlayWarmupAsync");
        Assert.Contains("_windowsAppRuntimePrerequisite.Probe()", warmup, StringComparison.Ordinal);
        Assert.Contains("(\"Action\", \"NoSetupNoUac\")", warmup, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureAvailableAsync", warmup, StringComparison.Ordinal);

        var mainOpen = ExtractMethod(host, "private async Task CoordinateFrontendOpenAsync");
        Assert.True(mainOpen.IndexOf("RequestOpen", StringComparison.Ordinal)
            < mainOpen.IndexOf("EnsureWindowsAppRuntimeForSurfaceAsync", StringComparison.Ordinal));
        Assert.True(mainOpen.IndexOf("EnsureWindowsAppRuntimeForSurfaceAsync", StringComparison.Ordinal)
            < mainOpen.IndexOf("_frontendLauncher.Launch", StringComparison.Ordinal));

        var overlayOpen = ExtractMethod(host, "private async Task CoordinateOverlayToggleAsync");
        Assert.True(overlayOpen.IndexOf("EnsureWindowsAppRuntimeForSurfaceAsync(\"Overlay\")", StringComparison.Ordinal)
            < overlayOpen.IndexOf("RequestClientCloseAsync", StringComparison.Ordinal));
        Assert.True(overlayOpen.IndexOf("RequestClientCloseAsync", StringComparison.Ordinal)
            < overlayOpen.IndexOf("_overlayController.ShowAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void External_ui_app_registers_required_winui_control_resources()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/App.xaml"));

        Assert.Contains("<Application.Resources>", appXaml, StringComparison.Ordinal);
        Assert.Contains("XamlControlsResources", appXaml, StringComparison.Ordinal);
    }

    [Fact] // Full1902 terminal STOP work order: this page is a focused diagnostic client over the
           // typed frontend contract, not the deleted legacy vibration-session surface.
    public void Vibration_test_page_uses_only_the_full1902_xbox360_loop_diagnostic_contract()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs"));
        var pageXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml"));
        var mainWindowXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));

        Assert.DoesNotContain("<views:VibrationTestPage", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Xbox360 Terminal STOP Loop", pageXaml, StringComparison.Ordinal);
        Assert.Contains("StartXbox360RumbleLoopDiagnosticAsync", page, StringComparison.Ordinal);
        Assert.Contains("CaptureXbox360RumbleLoopDiagnosticAsync", page, StringComparison.Ordinal);
        Assert.Contains("StopXbox360RumbleLoopDiagnosticAsync", page, StringComparison.Ordinal);
        Assert.Contains("else if (_snapshot.State == FrontendXbox360RumbleLoopState.Running)", page, StringComparison.Ordinal);
        Assert.Contains("await StopIfRunningAsync();", page, StringComparison.Ordinal);
        Assert.Contains("DeactivateAsync", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("unavailable in this build", page, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "RunVibrationTestAsync", "OpenVibrationTestSessionAsync", "CloseVibrationTestSessionAsync" })
        {
            Assert.DoesNotContain(forbidden, page, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, mainWindow, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("Haptic Test", pageXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GameInput_system_button_probe_is_a_developer_frontend_page_without_ui_native_interop()
    {
        var root = FindRepositoryRoot();
        var developerXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml"));
        var developerCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs"));
        var pageXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/GameInputSystemButtonProbePage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/GameInputSystemButtonProbePage.xaml.cs"));
        var mainWindowXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml"));
        var overlayRoot = Path.Combine(root, "src/SteamInputAddonforClaw.Overlay");

        Assert.Contains("GameInput System Button Probe", developerXaml, StringComparison.Ordinal);
        Assert.Contains("GameInputSystemButtonProbeRequested", developerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("<views:GameInputSystemButtonProbePage", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GameInputSystemButtonProbePage", File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("IAddonFrontendControl", page, StringComparison.Ordinal);
        Assert.Contains("CaptureGameInputSystemButtonProbeAsync", page, StringComparison.Ordinal);
        Assert.Contains("StartGameInputSystemButtonProbeAsync", page, StringComparison.Ordinal);
        Assert.Contains("StopGameInputSystemButtonProbeAsync", page, StringComparison.Ordinal);
        Assert.Contains("GameInput System Button Probe", pageXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DllImport", page, StringComparison.Ordinal);
        Assert.DoesNotContain("IGameInput", page, StringComparison.Ordinal);
        Assert.DoesNotContain("GameInput.dll", page, StringComparison.Ordinal);

        foreach (var file in Directory.EnumerateFiles(overlayRoot, "*", SearchOption.AllDirectories)
                     .Where(path => Path.GetExtension(path) is ".cs" or ".xaml"))
            Assert.DoesNotContain("GameInputSystemButtonProbe", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void Developer_surface_omits_retired_gpu_frequency_controls()
    {
        var root = FindRepositoryRoot();
        var developerXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml"));
        var developerCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs"));

        Assert.DoesNotContain("Intel GPU Frequency / IGCL Probe", developerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Set Max / Max + Readback", developerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Set Test PL1 + Readback", developerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Frequency" + "Probe", developerCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Tdp_toggle_disables_editors_while_the_first_enable_is_in_flight()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));

        Assert.Contains("SetTdpMutationBusy(true)", page, StringComparison.Ordinal);
        Assert.Contains("finally { SetTdpMutationBusy(false); }", page, StringComparison.Ordinal);
        Assert.Contains("if (_tdpSnapshot.Configuration?.Enabled == true) ScheduleTdpEdit(isAc);", page, StringComparison.Ordinal);
    }

    [Fact] // Shared Frontend V2, SF-V2-01: the normal Device refresh path performs one aggregate
           // read instead of three separate Device captures, while Center M's own separate refresh
           // (reboot-bound, work order section 10.1) is untouched.
    public void Device_refresh_uses_one_shared_aggregate_capture_and_fails_closed_on_whole_transport_failure()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));

        var refreshStart = page.IndexOf("internal async Task RefreshAsync()", StringComparison.Ordinal);
        Assert.True(refreshStart >= 0);
        var refresh = page[refreshStart..page.IndexOf("private static readonly PowerModeItem[] PowerModes", refreshStart, StringComparison.Ordinal)];

        Assert.Contains("await _frontend.CaptureDeviceQuickSettingsAsync()", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureCpuBoostAsync()", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureTdpAsync()", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("CapturePowerModeAsync()", refresh, StringComparison.Ordinal);
        Assert.Contains("Render(snapshot.CpuBoost);", refresh, StringComparison.Ordinal);
        Assert.Contains("RenderTdp(snapshot.Tdp);", refresh, StringComparison.Ordinal);
        Assert.Contains("RenderPowerMode(snapshot.PowerMode);", refresh, StringComparison.Ordinal);
        Assert.Contains("RenderBatteryChargeLimit(snapshot.BatteryChargeLimit);", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureBatteryChargeLimitAsync()", refresh, StringComparison.Ordinal);
        // Whole-transport failure fails closed: all three children render Unavailable and the TDP
        // dirty draft is not preserved as if it were still authoritative/editable (section 10.4).
        Assert.Contains("Render(FrontendCpuBoostSnapshot.Unavailable);", refresh, StringComparison.Ordinal);
        Assert.Contains("RenderTdp(FrontendTdpSnapshot.Unavailable, preserveDirtyDraft: false);", refresh, StringComparison.Ordinal);
        Assert.Contains("RenderPowerMode(FrontendPowerModeSnapshot.Unavailable);", refresh, StringComparison.Ordinal);
        Assert.Contains("RenderBatteryChargeLimit(FrontendBatteryChargeLimitSnapshot.Unavailable, preserveDirtyDraft: false);", refresh, StringComparison.Ordinal);

        // Center M keeps its own separate, reboot-bound page-entry refresh.
        Assert.Contains("_ = RefreshCenterMStartupAsync();", page, StringComparison.Ordinal);
        Assert.Contains("private async Task RefreshCenterMStartupAsync()", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Power_mode_ui_failures_clear_stale_state()
    {
        var root = FindRepositoryRoot();
        var devicePage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        var profilePage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));

        // Shared Frontend V2 SF-V2-01: RefreshAsync no longer catches each Device feature capture
        // separately -- a whole-transport failure renders all three children Unavailable and
        // RenderPowerMode itself is responsible for the concise per-feature failure message.
        Assert.Contains("RenderPowerMode(FrontendPowerModeSnapshot.Unavailable)", devicePage, StringComparison.Ordinal);
        Assert.Contains("\"Windows Power Mode could not be initialized.\"", devicePage, StringComparison.Ordinal);
        Assert.Contains("PowerModeAcComboBox.SelectedItem = null", profilePage, StringComparison.Ordinal);
        Assert.Contains("PowerModeDcComboBox.SelectedItem = null", profilePage, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_feature_expanders_start_collapsed_without_snapshot_driven_expansion()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        var normalizedXaml = xaml.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("IsExpanded=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Expander.IsExpanded =", codeBehind, StringComparison.Ordinal);
        Assert.Contains("<ctcontrols:SettingsExpander.HeaderIcon>\n                    <FontIcon Glyph=\"&#xE83F;\" />\n                </ctcontrols:SettingsExpander.HeaderIcon>", normalizedXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"TDP Control\"", xaml, StringComparison.Ordinal);
        Assert.True(xaml.IndexOf("Header=\"TDP Control\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"CPU Boost\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Header=\"CPU Boost\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"Windows Power Mode\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Device_closed_info_bars_are_removed_from_stack_panel_spacing()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        var infoBarNames = new[]
        {
            "CenterMStartupInfoBar",
            "BatteryChargeLimitInfoBar",
            "TdpInfoBar",
            "CpuBoostInfoBar",
            "PowerModeInfoBar",
        };

        foreach (var name in infoBarNames)
        {
            var declarationStart = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
            var declarationEnd = xaml.IndexOf("/>", declarationStart, StringComparison.Ordinal);
            Assert.True(declarationStart >= 0 && declarationEnd > declarationStart, $"Missing XAML declaration for {name}.");

            var declaration = xaml[declarationStart..declarationEnd];
            Assert.Contains("IsOpen=\"False\"", declaration, StringComparison.Ordinal);
            Assert.Contains("Visibility=\"Collapsed\"", declaration, StringComparison.Ordinal);
            Assert.DoesNotContain($"{name}.IsOpen =", codeBehind, StringComparison.Ordinal);
        }

        Assert.Contains("private static void SetInfoBarOpen(InfoBar infoBar, bool isOpen)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("infoBar.Visibility = Visibility.Visible;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("infoBar.Visibility = Visibility.Collapsed;", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_page_exposes_production_battery_control_as_an_inline_card()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));

        Assert.Contains("x:Name=\"BatteryChargeLimitCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<FontIcon Glyph=\"&#xE86B;\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleSwitch x:Name=\"BatteryChargeLimitEnabledToggleSwitch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Slider x:Name=\"BatteryChargeLimitSlider\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextBlock x:Name=\"BatteryChargeLimitValueText\" MinWidth=\"64\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Set the maximum charge level for this MSI Claw device.", xaml, StringComparison.Ordinal);
        Assert.Contains("<ctcontrols:SettingsCard.Description>", xaml, StringComparison.Ordinal);
        Assert.Contains("TextBlock x:Name=\"BatteryChargeLimitStatusText\" Opacity=\"0.7\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Slider x:Name=\"BatteryChargeLimitSlider\" Grid.Column=\"1\" VerticalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleSwitch x:Name=\"BatteryChargeLimitEnabledToggleSwitch\" Grid.Column=\"2\" VerticalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.Row=\"1\" Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsExpander x:Name=\"BatteryChargeLimitCard\"", xaml, StringComparison.Ordinal);
        Assert.True(xaml.IndexOf("x:Name=\"PowerModeExpander\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"BatteryChargeLimitInfoBar\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"BatteryChargeLimitInfoBar\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"BatteryChargeLimitCard\"", StringComparison.Ordinal));
        Assert.Contains("RenderBatteryChargeLimit(snapshot.BatteryChargeLimit)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureBatteryChargeLimitAsync", codeBehind, StringComparison.Ordinal);
        var frontendContracts = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs"));
        Assert.Contains("CaptureBatteryChargeLimitAsync", frontendContracts, StringComparison.Ordinal);
        Assert.Contains("SetDeviceBatteryChargeLimitPercentAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PointerCaptureLost", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyUp", xaml, StringComparison.Ordinal);
        Assert.Contains("_batteryChargeLimitDraftDirty", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_suppressBatteryChargeLimitEvents", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Battery_slider_value_changed_ignores_xaml_initialization_before_frontend_connects()
    {
        var root = FindRepositoryRoot();
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        var handlerStart = codeBehind.IndexOf("private void BatteryChargeLimitSlider_ValueChanged", StringComparison.Ordinal);
        var handlerEnd = codeBehind.IndexOf("private async void BatteryChargeLimitSlider_PointerCaptureLost", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0);
        Assert.True(handlerEnd > handlerStart);

        var handler = codeBehind[handlerStart..handlerEnd];
        var guard = "if (_suppressBatteryChargeLimitEvents || _frontend is null) return;";
        Assert.Contains(guard, handler, StringComparison.Ordinal);
        Assert.True(handler.IndexOf(guard, StringComparison.Ordinal)
            < handler.IndexOf("_batteryChargeLimitDraftPercent =", StringComparison.Ordinal));
    }

    [Fact]
    public void Battery_slider_draft_policy_commits_the_changed_value_once_after_interaction()
    {
        var dirty = true;
        var commitCount = 0;
        var committedPercent = 0;
        var draftPercent = 85;

        if (DevicePage.BatteryChargeLimitDraftPolicy.ShouldCommit(80, draftPercent, dirty, busy: false))
        {
            dirty = false;
            commitCount++;
            committedPercent = draftPercent;
        }

        Assert.Equal(85, committedPercent);
        Assert.Equal(1, commitCount);
        Assert.False(DevicePage.BatteryChargeLimitDraftPolicy.ShouldCommit(80, draftPercent, dirty, busy: false));
    }

    [Fact]
    public void Device_page_owns_the_msi_center_m_startup_card_below_the_identity_summary_with_explicit_buttons()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));
        var controllerPage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml"));

        // App UI PR-A: the card moved from Controller to Device and is no longer on Controller.
        Assert.Contains("x:Name=\"CenterMStartupCard\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CenterMStartupCard", controllerPage, StringComparison.Ordinal);

        // Ordering: device identity/support summary above Center M, Center M above TDP Control.
        Assert.True(xaml.IndexOf("x:Name=\"DeviceSummaryCard\"", StringComparison.Ordinal) >= 0);
        Assert.True(xaml.IndexOf("x:Name=\"DeviceSummaryCard\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"CenterMStartupCard\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"CenterMStartupCard\"", StringComparison.Ordinal)
            < xaml.IndexOf("Header=\"TDP Control\"", StringComparison.Ordinal));

        // Full1902 removed the user-configurable Steam Input Routing switch entirely.
        Assert.DoesNotContain("SteamInputRouting", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamInputRouting", codeBehind, StringComparison.Ordinal);

        // Re-read on every entry to the Device page (the reboot-bound transition raises no StateInvalidated).
        Assert.Contains("_ = RefreshCenterMStartupAsync();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("if (page == MainNavigationPage.Device) DeviceContent.Activate();", mainWindow, StringComparison.Ordinal);

        // Explicit Enable/Disable buttons, not an inverted toggle.
        Assert.Contains("x:Name=\"CenterMStartupEnableButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CenterMStartupDisableButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Disable MSI Center M", xaml, StringComparison.Ordinal);

        // The buttons confirm a reboot-bound transition and restart immediately -- the labels say so,
        // and there is no "Restart Later" / deferred-restart mode.
        Assert.Contains("and Restart", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Restart Later", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Restart now", codeBehind, StringComparison.Ordinal);

        // No sticky UI restart flag -- the info bar is a pure function of the latest snapshot state.
        Assert.DoesNotContain("_centerMRestartRequired", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CenterMStartupPresentation.ResolveInfoBar(snapshot.State)", codeBehind, StringComparison.Ordinal);

        // The UI never starts the OS restart itself -- that is the Runtime transition owner's job.
        Assert.DoesNotContain("shutdown.exe", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("shutdown", xaml, StringComparison.Ordinal);

        // A failed/cancelled Disable that left preparation behind (roots still Enabled) must re-expose
        // "Enable and Restart" -- the cleanup path the backend message advertises.
        Assert.Contains("!centerMEnabled && result.Snapshot.State == FrontendCenterMStartupState.Enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CenterMStartupEnableButton.IsEnabled = true;", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_navigation_drops_status_and_defaults_to_device()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));
        var navigationState = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainNavigationState.cs"));

        // No Status navigation destination and no Status page instance survive in the shell.
        Assert.DoesNotContain("Tag=\"Status\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusContent", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusContent", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNavigationPage.Status", navigationState, StringComparison.Ordinal);

        // Top-level menu order is Device < Controller < Profile < Overlay < Shortcut < HowToUse.
        Assert.True(xaml.IndexOf("Tag=\"Device\"", StringComparison.Ordinal) < xaml.IndexOf("Tag=\"Controller\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Tag=\"Controller\"", StringComparison.Ordinal) < xaml.IndexOf("Tag=\"Profile\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Tag=\"Profile\"", StringComparison.Ordinal) < xaml.IndexOf("Tag=\"Overlay\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Tag=\"Overlay\"", StringComparison.Ordinal) < xaml.IndexOf("Tag=\"Shortcut\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Tag=\"Shortcut\"", StringComparison.Ordinal) < xaml.IndexOf("Tag=\"HowToUse\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Tag=\"Overlay\"", StringComparison.Ordinal) < xaml.IndexOf("Tag=\"HowToUse\"", StringComparison.Ordinal));
        Assert.Contains("<NavigationViewItem Content=\"ClawHUD\" Tag=\"Overlay\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<NavigationViewItem Content=\"Overlay\" Tag=\"Overlay\">", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationPage.Overlay", navigationState, StringComparison.Ordinal);
        Assert.Contains("OverlayContent", mainWindow, StringComparison.Ordinal);
        Assert.Contains("MainNavigationPage.Shortcut", navigationState, StringComparison.Ordinal);
        Assert.Contains("\"Shortcut\" => MainNavigationPage.Shortcut", navigationState, StringComparison.Ordinal);
        Assert.Contains("ShortcutContent", xaml, StringComparison.Ordinal);
        Assert.Contains("if (page == MainNavigationPage.Shortcut) ShortcutContent.Activate();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("else if (wasShortcut) ShortcutContent.Deactivate();", mainWindow, StringComparison.Ordinal);

        // Device is the default page in both the shell and the navigation state.
        Assert.Contains("MainNavigationView.SelectedItem = DeviceNavigationItem;", mainWindow, StringComparison.Ordinal);
        Assert.Contains("CurrentPage { get; private set; } = MainNavigationPage.Device;", navigationState, StringComparison.Ordinal);
        Assert.Contains("_ => MainNavigationPage.Device", navigationState, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_app_shortcut_editor_is_separate_from_the_overlay_settings_page()
    {
        var root = FindRepositoryRoot();
        var shortcutXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml"));
        var shortcutCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs"));
        var overlayXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));

        Assert.Contains("Add Shortcut", shortcutXaml, StringComparison.Ordinal);
        Assert.Contains("PointerReleased=\"ShortcutTile_PointerReleased\"", shortcutXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CanReorderItems", shortcutXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Screenshot", shortcutXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BrowseScreenshotFolderButton", shortcutXaml, StringComparison.Ordinal);
        Assert.Contains("FolderPicker(windowId)", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("MutateShortcutAsync", shortcutCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SetScreenshotSaveFolderAsync", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("ScreenshotFolder: stagedScreenshotFolder", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("CaptureShortcutEditorAsync", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("screenshotAlreadyExists", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("useDefaultFolderButton.Click", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("openScreenshotFolderButton.Click", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("stagedScreenshotFolder = folder.Path", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("stagedScreenshotFolder = null", shortcutCode, StringComparison.Ordinal);
        var discardStagedEdits = shortcutCode.IndexOf("if (result != ContentDialogResult.Primary) return;", StringComparison.Ordinal);
        var persistStagedEdits = shortcutCode.IndexOf("await ApplyMutationAsync(intent)", StringComparison.Ordinal);
        Assert.True(discardStagedEdits >= 0 && discardStagedEdits < persistStagedEdits);
        Assert.Contains("ShortcutContent.RequestRefresh()", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("ShortcutStore", shortcutCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ShortcutsPath", shortcutCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteAsync", shortcutCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AddShortcutButton", overlayXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ScreenshotFolder", overlayXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Slot1", shortcutXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Slot2", shortcutXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_app_shortcut_editor_uses_three_equal_pointer_reorder_slots_and_keeps_collection_order()
    {
        var root = FindRepositoryRoot();
        var shortcutXaml = XDocument.Load(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml"));
        var shortcutCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs"));
        var list = shortcutXaml.Descendants().Single(element => (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name") == "ShortcutList");
        var wrapGrid = list.Descendants().Single(element => element.Name.LocalName == "ItemsWrapGrid");
        var itemTemplate = shortcutXaml.Descendants().Single(element => element.Name.LocalName == "DataTemplate"
            && (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Key") == "ShortcutCardTemplate");
        var itemContainerStyle = list.Elements().Single(element => element.Name.LocalName == "ListView.ItemContainerStyle")
            .Elements().Single(element => element.Name.LocalName == "Style");
        var horizontalContentAlignment = itemContainerStyle.Elements()
            .Single(element => element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "HorizontalContentAlignment");
        var card = itemTemplate.Descendants().Single(element => element.Name.LocalName == "Border");
        var cardGrid = card.Elements().Single(element => element.Name.LocalName == "Grid");
        var actionStack = cardGrid.Elements().Single(element =>
            element.Name.LocalName == "StackPanel" && (string?)element.Attribute("Grid.Column") == "2");
        var actionButtons = actionStack.Elements().Where(element => element.Name.LocalName == "Button").ToArray();
        var containerSetters = itemContainerStyle.Elements().Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => (string)element.Attribute("Property")!, element => (string?)element.Attribute("Value"));
        Assert.Equal("Horizontal", (string?)wrapGrid.Attribute("Orientation"));
        Assert.Equal("3", (string?)wrapGrid.Attribute("MaximumRowsOrColumns"));
        Assert.Equal("0", (string?)list.Attribute("Padding"));
        Assert.Equal("{StaticResource ShortcutCardTemplate}", (string?)list.Attribute("ItemTemplate"));
        Assert.Equal("ShortcutList_SizeChanged", (string?)list.Attribute("SizeChanged"));
        Assert.Equal("None", (string?)list.Attribute("SelectionMode"));
        Assert.Null(list.Attribute("CanReorderItems"));
        Assert.Null(list.Attribute("CanDragItems"));
        Assert.Null(list.Attribute("AllowDrop"));
        Assert.Null(list.Attribute("DragItemsStarting"));
        Assert.Null(list.Attribute("DragItemsCompleted"));
        Assert.Equal("ListViewItem", (string?)itemContainerStyle.Attribute("TargetType"));
        Assert.Equal("Stretch", (string?)horizontalContentAlignment.Attribute("Value"));
        Assert.Equal("Transparent", containerSetters["Background"]);
        Assert.Equal("Transparent", containerSetters["BorderBrush"]);
        Assert.Equal("0", containerSetters["BorderThickness"]);
        Assert.Equal("0", containerSetters["Padding"]);
        Assert.Equal("Stretch", containerSetters["VerticalContentAlignment"]);
        Assert.Equal("False", containerSetters["UseSystemFocusVisuals"]);
        var itemTemplateSetter = itemContainerStyle.Elements().Single(element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Template");
        var itemControlTemplate = itemTemplateSetter.Elements().Single(element => element.Name.LocalName == "Setter.Value")
            .Elements().Single(element => element.Name.LocalName == "ControlTemplate");
        var itemContentPresenter = itemControlTemplate.Elements().Single(element => element.Name.LocalName == "ContentPresenter");
        Assert.Equal("ListViewItem", (string?)itemControlTemplate.Attribute("TargetType"));
        Assert.Equal("{TemplateBinding Content}", (string?)itemContentPresenter.Attribute("Content"));
        Assert.Equal("{TemplateBinding ContentTemplate}", (string?)itemContentPresenter.Attribute("ContentTemplate"));
        Assert.Equal("{TemplateBinding ContentTransitions}", (string?)itemContentPresenter.Attribute("ContentTransitions"));
        Assert.Equal("{TemplateBinding HorizontalContentAlignment}", (string?)itemContentPresenter.Attribute("HorizontalAlignment"));
        Assert.Equal("{TemplateBinding VerticalContentAlignment}", (string?)itemContentPresenter.Attribute("VerticalAlignment"));
        Assert.DoesNotContain(itemControlTemplate.Descendants(), element => element.Name.LocalName is "VisualState" or "ListViewItemPresenter" or "ThemeShadow");
        Assert.DoesNotContain(shortcutXaml.Descendants(), element => element.Name.LocalName == "ThemeShadow");
        Assert.Single(shortcutXaml.Descendants(), element => element.Name.LocalName == "DataTemplate"
            && (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Key") == "ShortcutCardTemplate");
        Assert.Equal("Auto,*,Auto", (string?)cardGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("{x:Bind TileId}", (string?)card.Attribute("Tag"));
        Assert.Equal("ShortcutTile_PointerPressed", (string?)card.Attribute("PointerPressed"));
        Assert.Equal("ShortcutTile_PointerMoved", (string?)card.Attribute("PointerMoved"));
        Assert.Equal("ShortcutTile_PointerReleased", (string?)card.Attribute("PointerReleased"));
        Assert.Equal("ShortcutTile_PointerCanceled", (string?)card.Attribute("PointerCanceled"));
        Assert.Equal("ShortcutTile_PointerCaptureLost", (string?)card.Attribute("PointerCaptureLost"));
        Assert.Equal("None", (string?)card.Attribute("ManipulationMode"));
        Assert.Equal("0,0,12,12", (string?)card.Attribute("Margin"));
        Assert.Equal("1", (string?)card.Attribute("BorderThickness"));
        Assert.Equal("8", (string?)card.Attribute("CornerRadius"));
        Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}", (string?)card.Attribute("Background"));
        Assert.Equal("{ThemeResource CardStrokeColorDefaultBrush}", (string?)card.Attribute("BorderBrush"));
        Assert.Equal("Vertical", (string?)actionStack.Attribute("Orientation"));
        Assert.Equal(2, actionButtons.Length);
        Assert.All(actionButtons, button => Assert.Equal("Stretch", (string?)button.Attribute("HorizontalAlignment")));
        Assert.All(actionButtons, button => Assert.Null(button.Attribute("PointerPressed")));
        Assert.All(actionButtons, button => Assert.Null(button.Attribute("PointerMoved")));
        Assert.All(actionButtons, button => Assert.NotNull(button.Attribute("Click")));
        var dragGlyph = itemTemplate.Descendants().Single(element => element.Name.LocalName == "FontIcon");
        Assert.Equal("Drag to reorder this Shortcut", (string?)dragGlyph.Attribute("AutomationProperties.Name"));
        Assert.Contains("private const int ShortcutColumnCount = 3;", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("private const double ShortcutCardHorizontalGap = 12;", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("itemsPanel.ItemWidth = itemWidth", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("GetShortcutItemWidth(ShortcutList.ActualWidth)", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("(ShortcutColumnCount - 1) * ShortcutCardHorizontalGap", shortcutCode, StringComparison.Ordinal);
        Assert.Equal(292, ShortcutPage.GetShortcutItemWidth(900));
        Assert.Equal(392, ShortcutPage.GetShortcutItemWidth(1200));
        Assert.Equal(1, ShortcutPage.GetShortcutItemWidth(24));
        Assert.Contains("ShortcutList.ItemsSource = _tiles;", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("CapturePointer(e.Pointer)", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("GetCurrentPoint(ShortcutList)", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("ContainerFromIndex(index)", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("TransformBounds", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("PointerCanceled=\"ShortcutTile_PointerCanceled\"", File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml")), StringComparison.Ordinal);
        Assert.Contains("Unloaded=\"ShortcutPage_Unloaded\"", File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml")), StringComparison.Ordinal);
        Assert.DoesNotContain("DragItemsCompleted", shortcutCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DragItemsStarting", shortcutCode, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDragAsync", shortcutCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.ApplicationModel.DataTransfer", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("Runtime Shortcut move response received.", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("FailureCategory", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("await ApplyMutationAsync(intent);", shortcutCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_tiles.Move(", shortcutCode, StringComparison.Ordinal);
        Assert.DoesNotContain("RowIndex", shortcutCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnIndex", shortcutCode, StringComparison.Ordinal);

        for (var tileCount = 1; tileCount <= 6; tileCount++)
        {
            var positions = Enumerable.Range(0, tileCount)
                .Select(index => (Row: index / 3, Column: index % 3))
                .ToArray();
            Assert.Equal(Enumerable.Range(0, tileCount),
                positions.Select(position => position.Row * 3 + position.Column));
            Assert.All(positions, position => Assert.InRange(position.Column, 0, 2));
        }
    }

    [Fact]
    public void Main_app_shortcut_pointer_release_sends_one_authoritative_move_without_local_reordering()
    {
        var root = FindRepositoryRoot();
        var shortcutXaml = XDocument.Load(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml"));
        var shortcutCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs"));
        var list = shortcutXaml.Descendants().Single(element => (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name") == "ShortcutList");
        var releaseHandlerStart = shortcutCode.IndexOf("private async void ShortcutTile_PointerReleased", StringComparison.Ordinal);
        var releaseHandlerEnd = shortcutCode.IndexOf("private void ShortcutTile_PointerCanceled", releaseHandlerStart, StringComparison.Ordinal);
        Assert.True(releaseHandlerStart >= 0 && releaseHandlerEnd > releaseHandlerStart);
        var releaseHandler = shortcutCode[releaseHandlerStart..releaseHandlerEnd];
        Assert.Equal(1, releaseHandler.Split("await ApplyMutationAsync(intent)", StringSplitOptions.None).Length - 1);
        Assert.Contains("ClearShortcutReorder(outcome, releaseCapture: true)", releaseHandler, StringComparison.Ordinal);
        Assert.Contains("TryCreatePointerMoveIntent", releaseHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_tiles.Clear()", releaseHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_tiles.Add(", releaseHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_tiles.Move(", releaseHandler, StringComparison.Ordinal);
        Assert.True(releaseHandler.IndexOf("ClearShortcutReorder(outcome, releaseCapture: true)", StringComparison.Ordinal)
            < releaseHandler.IndexOf("await ApplyMutationAsync(intent)", StringComparison.Ordinal));
        Assert.Contains("private void ShortcutTile_PointerCanceled", shortcutCode, StringComparison.Ordinal);
        var canceledStart = shortcutCode.IndexOf("private void ShortcutTile_PointerCanceled", StringComparison.Ordinal);
        var captureLostStart = shortcutCode.IndexOf("private void ShortcutTile_PointerCaptureLost", canceledStart, StringComparison.Ordinal);
        var unloadedStart = shortcutCode.IndexOf("private void ShortcutPage_Unloaded", captureLostStart, StringComparison.Ordinal);
        Assert.True(canceledStart >= 0 && captureLostStart > canceledStart && unloadedStart > captureLostStart);
        Assert.Contains("ClearShortcutReorder(\"Canceled\", releaseCapture: false)", shortcutCode[canceledStart..captureLostStart], StringComparison.Ordinal);
        Assert.Contains("private void ShortcutTile_PointerCaptureLost", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("ClearShortcutReorder(\"CaptureLost\", releaseCapture: false)", shortcutCode[captureLostStart..unloadedStart], StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyMutationAsync", shortcutCode[canceledStart..unloadedStart], StringComparison.Ordinal);
        var pointerPressedStart = shortcutCode.IndexOf("private void ShortcutTile_PointerPressed", StringComparison.Ordinal);
        var pointerMovedStart = shortcutCode.IndexOf("private void ShortcutTile_PointerMoved", pointerPressedStart, StringComparison.Ordinal);
        var pointerPressedHandler = shortcutCode[pointerPressedStart..pointerMovedStart].Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("IsPointerSourceInsideButton(e.OriginalSource as DependencyObject, surface)",
            pointerPressedHandler, StringComparison.Ordinal);
        Assert.Contains("if (!isMouse)\n            surface.CancelDirectManipulations();",
            pointerPressedHandler, StringComparison.Ordinal);
        Assert.True(shortcutCode.IndexOf("surface.CancelDirectManipulations();", pointerPressedStart, StringComparison.Ordinal)
            < shortcutCode.IndexOf("surface.CapturePointer(e.Pointer)", pointerPressedStart, StringComparison.Ordinal));
        Assert.Contains("ClearShortcutReorder(\"Canceled\", releaseCapture: true)", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("ClearShortcutReorder(\"Unavailable\", releaseCapture: true)", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("IsPointerSourceInsideButton", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("Shortcut pointer reorder armed.", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("Shortcut pointer reorder released.", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("ShortcutPage_Unloaded", shortcutCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_app_shortcut_pointer_drag_uses_a_shared_lift_preview_and_restores_all_visuals()
    {
        var root = FindRepositoryRoot();
        var shortcutXaml = XDocument.Load(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml"));
        var shortcutCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs"));
        var xamlName = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";
        var xamlKey = "{http://schemas.microsoft.com/winfx/2006/xaml}Key";
        var cardTemplate = shortcutXaml.Descendants().Single(element => element.Name.LocalName == "DataTemplate"
            && (string?)element.Attribute(xamlKey) == "ShortcutCardTemplate");
        var list = shortcutXaml.Descendants().Single(element => (string?)element.Attribute(xamlName) == "ShortcutList");
        var preview = shortcutXaml.Descendants().Single(element => (string?)element.Attribute(xamlName) == "ShortcutDragPreview");
        var indicator = shortcutXaml.Descendants().Single(element => (string?)element.Attribute(xamlName) == "ShortcutDropIndicator");
        var layer = shortcutXaml.Descendants().Single(element => (string?)element.Attribute(xamlName) == "ShortcutDragLayer");
        var listHost = shortcutXaml.Descendants().Single(element => element.Name.LocalName == "Grid"
            && (string?)element.Attribute("Grid.Row") == "2");
        var listHostChildren = listHost.Elements().ToArray();

        Assert.Equal("{StaticResource ShortcutCardTemplate}", (string?)list.Attribute("ItemTemplate"));
        Assert.Equal("{StaticResource ShortcutCardTemplate}", (string?)preview.Attribute("ContentTemplate"));
        Assert.Equal("ShortcutDragLayer", (string?)listHostChildren[^1].Attribute(xamlName));
        Assert.Equal("False", (string?)layer.Attribute("IsHitTestVisible"));
        Assert.Equal("False", (string?)preview.Attribute("IsHitTestVisible"));
        Assert.Equal("False", (string?)indicator.Attribute("IsHitTestVisible"));
        Assert.Equal("2", (string?)indicator.Attribute("BorderThickness"));
        Assert.Equal("{ThemeResource CardStrokeColorDefaultBrush}", (string?)indicator.Attribute("BorderBrush"));
        Assert.Equal("1", (string?)indicator.Attribute("Canvas.ZIndex"));
        Assert.Equal("2", (string?)preview.Attribute("Canvas.ZIndex"));
        Assert.Equal("0.5,0.5", (string?)preview.Attribute("RenderTransformOrigin"));
        Assert.Equal("0.95", (string?)preview.Attribute("Opacity"));
        Assert.Equal("Collapsed", (string?)preview.Attribute("Visibility"));
        Assert.Equal(2, cardTemplate.Descendants().Count(element => element.Name.LocalName == "Button"));

        var pressed = ExtractMethod(shortcutCode, "private void ShortcutTile_PointerPressed");
        var moved = ExtractMethod(shortcutCode, "private void ShortcutTile_PointerMoved");
        var showPreview = ExtractMethod(shortcutCode, "private bool ShowShortcutDragPreview");
        var updatePreviewPosition = ExtractMethod(shortcutCode, "private void UpdateShortcutDragPreviewPosition");
        var updateDropIndicator = ExtractMethod(shortcutCode, "private void UpdateShortcutDropIndicator");
        var itemBounds = ExtractMethod(shortcutCode, "private bool TryGetShortcutItemBoundsInDragLayer");
        var animateLift = ExtractMethod(shortcutCode, "private void BeginShortcutDragLiftAnimation");
        var scaleAnimation = ExtractMethod(shortcutCode, "private void AddShortcutDragScaleAnimation");
        var clear = ExtractMethod(shortcutCode, "private void ClearShortcutReorder");
        var render = ExtractMethod(shortcutCode, "private void Render(");
        var setBusy = ExtractMethod(shortcutCode, "private void SetBusy(bool busy)");
        var deactivate = ExtractMethod(shortcutCode, "public void Deactivate()");
        var unloaded = ExtractMethod(shortcutCode, "private void ShortcutPage_Unloaded");
        var editClick = ExtractMethod(shortcutCode, "private async void EditTileButton_Click");
        var deleteClick = ExtractMethod(shortcutCode, "private async void DeleteTileButton_Click");

        Assert.DoesNotContain("ShowShortcutDragPreview", pressed, StringComparison.Ordinal);
        Assert.DoesNotContain("ShortcutDragPreview", pressed, StringComparison.Ordinal);
        Assert.Contains("GetCurrentPoint(ShortcutDragLayer)", pressed, StringComparison.Ordinal);
        Assert.Contains("HasPassedShortcutReorderThreshold", moved, StringComparison.Ordinal);
        Assert.True(moved.IndexOf("HasPassedShortcutReorderThreshold", StringComparison.Ordinal)
            < moved.IndexOf("ShowShortcutDragPreview", StringComparison.Ordinal));
        Assert.Contains("GetCurrentPoint(ShortcutDragLayer)", moved, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentShortcutDropIndex(position)", moved, StringComparison.Ordinal);
        Assert.Contains("UpdateShortcutDropIndicator(targetIndex)", moved, StringComparison.Ordinal);
        Assert.True(moved.IndexOf("var targetIndex = ResolveCurrentShortcutDropIndex(position)", StringComparison.Ordinal)
            < moved.IndexOf("UpdateShortcutDropIndicator(targetIndex)", StringComparison.Ordinal));
        Assert.DoesNotContain("_tiles.Move(", moved, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyMutationAsync", moved, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard", moved, StringComparison.Ordinal);

        Assert.Contains("ShortcutDragPreview.Content = tile", showPreview, StringComparison.Ordinal);
        Assert.Contains("ShortcutDragPreview.Width = _reorderSourceBoundsInDragLayer.Width", showPreview, StringComparison.Ordinal);
        Assert.Contains("ShortcutDragPreview.Height = _reorderSourceBoundsInDragLayer.Height", showPreview, StringComparison.Ordinal);
        Assert.Contains("ShortcutDragPreview.Visibility = Visibility.Visible", showPreview, StringComparison.Ordinal);
        Assert.Contains("_reorderSurface.Opacity = 0.35", showPreview, StringComparison.Ordinal);
        Assert.Contains("BeginShortcutDragLiftAnimation()", showPreview, StringComparison.Ordinal);
        Assert.Contains("To = 1.05", scaleAnimation, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(120)", scaleAnimation, StringComparison.Ordinal);
        Assert.Contains("EasingMode.EaseOut", scaleAnimation, StringComparison.Ordinal);
        Assert.Equal(2, animateLift.Split("AddShortcutDragScaleAnimation(storyboard", StringSplitOptions.None).Length - 1);
        Assert.Contains("Canvas.SetLeft(ShortcutDragPreview, origin.X)", updatePreviewPosition, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetTop(ShortcutDragPreview, origin.Y)", updatePreviewPosition, StringComparison.Ordinal);
        Assert.Contains("index == _reorderSourceIndex", updateDropIndicator, StringComparison.Ordinal);
        Assert.Contains("TryGetShortcutCardBoundsInDragLayer(index, out var targetBounds)", updateDropIndicator, StringComparison.Ordinal);
        Assert.Contains("ContainerFromIndex(index)", itemBounds, StringComparison.Ordinal);
        Assert.Contains("TransformToVisual(ShortcutDragLayer)", itemBounds, StringComparison.Ordinal);

        Assert.Contains("surface.Opacity = 1.0", clear, StringComparison.Ordinal);
        Assert.Contains("_shortcutDragLiftStoryboard?.Stop()", clear, StringComparison.Ordinal);
        Assert.Contains("ShortcutDragPreview.Content = null", clear, StringComparison.Ordinal);
        Assert.Contains("ShortcutDragPreview.Visibility = Visibility.Collapsed", clear, StringComparison.Ordinal);
        Assert.Contains("ShortcutDragPreviewTransform.ScaleX = 1", clear, StringComparison.Ordinal);
        Assert.Contains("ShortcutDragPreviewTransform.ScaleY = 1", clear, StringComparison.Ordinal);
        Assert.Contains("ShortcutDropIndicator.Visibility = Visibility.Collapsed", clear, StringComparison.Ordinal);
        Assert.Contains("_reorderStartPositionInDragLayer = default", clear, StringComparison.Ordinal);
        Assert.Contains("_reorderSourceBoundsInDragLayer = default", clear, StringComparison.Ordinal);
        Assert.Contains("IsShortcutDragPreviewElement(sender as DependencyObject)", editClick, StringComparison.Ordinal);
        Assert.Contains("IsShortcutDragPreviewElement(sender as DependencyObject)", deleteClick, StringComparison.Ordinal);
        Assert.Contains("ClearShortcutReorder(\"Canceled\", releaseCapture: true)", deactivate, StringComparison.Ordinal);
        Assert.Contains("ClearShortcutReorder(\"Canceled\", releaseCapture: true)", unloaded, StringComparison.Ordinal);
        Assert.Contains("ClearShortcutReorder(\"Unavailable\", releaseCapture: true)", setBusy, StringComparison.Ordinal);
        Assert.True(render.IndexOf("ClearShortcutReorder(\"Canceled\", releaseCapture: true)", StringComparison.Ordinal)
            < render.IndexOf("_tiles.Clear()", StringComparison.Ordinal));

        Assert.Equal(new Point(90, 110), ShortcutPage.GetShortcutDragPreviewOrigin(
            new Rect(40, 60, 200, 100), new Point(55, 70), new Point(105, 120)));
        Assert.False(ShortcutPage.HasPassedShortcutReorderThreshold(new Point(0, 0), new Point(7, 0)));
        Assert.True(ShortcutPage.HasPassedShortcutReorderThreshold(new Point(0, 0), new Point(8, 0)));
    }

    [Fact]
    public void Main_app_shortcut_pointer_drop_resolver_handles_rows_gaps_and_outside_release()
    {
        var fourCards = new (int Index, Rect Bounds)[]
        {
            (0, new Rect(0, 0, 100, 80)),
            (1, new Rect(112, 0, 100, 80)),
            (2, new Rect(224, 0, 100, 80)),
            (3, new Rect(0, 92, 100, 80))
        };
        var fiveCards = fourCards.Append((4, new Rect(112, 92, 100, 80))).ToArray();
        var gridBounds = new Rect(0, 0, 336, 184);

        Assert.Equal(0, ShortcutPage.ResolveShortcutDropIndex(new Point(50, 40), fourCards, gridBounds));
        Assert.Equal(1, ShortcutPage.ResolveShortcutDropIndex(new Point(162, 40), fourCards, gridBounds));
        Assert.Equal(2, ShortcutPage.ResolveShortcutDropIndex(new Point(274, 40), fourCards, gridBounds));
        Assert.Equal(3, ShortcutPage.ResolveShortcutDropIndex(new Point(50, 132), fourCards, gridBounds));
        Assert.Equal(4, ShortcutPage.ResolveShortcutDropIndex(new Point(162, 132), fiveCards, gridBounds));
        Assert.Equal(1, ShortcutPage.ResolveShortcutDropIndex(new Point(110, 40), fourCards, gridBounds));
        Assert.Null(ShortcutPage.ResolveShortcutDropIndex(new Point(-1, 40), fourCards, gridBounds));
        Assert.Null(ShortcutPage.ResolveShortcutDropIndex(new Point(50, 40), [], gridBounds));
        Assert.Null(ShortcutPage.ResolveShortcutDropIndex(new Point(50, 40),
            [(0, new Rect(0, 0, 0, 80))], gridBounds));

        var tallListViewport = new Rect(0, 0, 336, 600);
        Assert.Null(ShortcutPage.ResolveShortcutDropIndex(new Point(50, 450), fourCards, tallListViewport));
        Assert.Equal(3, ShortcutPage.ResolveShortcutDropIndex(new Point(50, 132), fourCards, tallListViewport));
        Assert.Equal(3, ShortcutPage.ResolveShortcutDropIndex(new Point(250, 132), fourCards, tallListViewport));
        Assert.Equal(4, ShortcutPage.ResolveShortcutDropIndex(new Point(250, 132), fiveCards, tallListViewport));
    }

    [Fact]
    public void Main_app_shortcut_pointer_move_intent_requires_a_deliberate_valid_index_change()
    {
        var tileId = Guid.NewGuid();

        var moveToLast = ShortcutPage.TryCreatePointerMoveIntent(tileId, 0, 3, tileCount: 4, thresholdPassed: true);
        Assert.NotNull(moveToLast);
        Assert.Equal(FrontendShortcutMutationKind.Move, moveToLast!.Kind);
        Assert.Equal(tileId, moveToLast.TileId);
        Assert.Equal(3, moveToLast.TargetIndex);

        var moveToFirst = ShortcutPage.TryCreatePointerMoveIntent(tileId, 3, 0, tileCount: 4, thresholdPassed: true);
        Assert.NotNull(moveToFirst);
        Assert.Equal(0, moveToFirst!.TargetIndex);
        Assert.Equal(2, ShortcutPage.TryCreatePointerMoveIntent(tileId, 1, 2, tileCount: 4, thresholdPassed: true)!.TargetIndex);
        var incompleteRowMove = ShortcutPage.TryCreatePointerMoveIntent(tileId, 4, 1, tileCount: 5, thresholdPassed: true);
        Assert.NotNull(incompleteRowMove);
        Assert.Equal(1, incompleteRowMove!.TargetIndex);
        Assert.Equal(4, ShortcutPage.TryCreatePointerMoveIntent(tileId, 0, 4, tileCount: 5, thresholdPassed: true)!.TargetIndex);
        Assert.Null(ShortcutPage.TryCreatePointerMoveIntent(tileId, 4, 1, tileCount: 5, thresholdPassed: false));
        Assert.Null(ShortcutPage.TryCreatePointerMoveIntent(tileId, 1, 1, tileCount: 5, thresholdPassed: true));
        Assert.Null(ShortcutPage.TryCreatePointerMoveIntent(tileId, 0, null, tileCount: 5, thresholdPassed: true));
        Assert.Null(ShortcutPage.TryCreatePointerMoveIntent(tileId, 0, 5, tileCount: 5, thresholdPassed: true));
        Assert.Null(ShortcutPage.TryCreatePointerMoveIntent(Guid.Empty, 0, 1, tileCount: 2, thresholdPassed: true));

        Assert.False(ShortcutPage.HasPassedShortcutReorderThreshold(new Point(10, 10), new Point(15, 15)));
        Assert.True(ShortcutPage.HasPassedShortcutReorderThreshold(new Point(10, 10), new Point(18, 10)));
    }

    [Fact]
    public void Main_app_shortcut_picker_exposes_parameterless_builtins_and_creation_only_titles()
    {
        var root = FindRepositoryRoot();
        var shortcutCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs"));
        var panelHelperStart = shortcutCode.IndexOf("private static StackPanel CreateBuiltInActionPanel", StringComparison.Ordinal);
        var panelHelperEnd = shortcutCode.IndexOf("internal static string? GetDefaultTitle", panelHelperStart, StringComparison.Ordinal);
        var builtInPanelHelper = shortcutCode[panelHelperStart..panelHelperEnd];

        Assert.Contains("AddActionChoice(actionPicker, \"Steam Big Picture\"", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("AddActionChoice(actionPicker, \"Steam\"", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("AddActionChoice(actionPicker, \"Xbox\"", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("AddActionChoice(actionPicker, \"Screenshot\"", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("[FrontendShortcutEditorActionKind.SteamBigPicture] = steamBigPicturePanel", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("[FrontendShortcutEditorActionKind.SteamClient] = steamClientPanel", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("[FrontendShortcutEditorActionKind.XboxApp] = xboxAppPanel", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("panel.Visibility = Visibility.Collapsed", shortcutCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Executable path", builtInPanelHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("Script", builtInPanelHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("URL", builtInPanelHelper, StringComparison.Ordinal);
        Assert.Contains("new FrontendShortcutActionInput(selectedKind)", shortcutCode, StringComparison.Ordinal);

        Assert.Equal("Steam Big Picture", ShortcutPage.GetDefaultTitle(FrontendShortcutEditorActionKind.SteamBigPicture));
        Assert.Equal("Steam", ShortcutPage.GetDefaultTitle(FrontendShortcutEditorActionKind.SteamClient));
        Assert.Equal("Xbox", ShortcutPage.GetDefaultTitle(FrontendShortcutEditorActionKind.XboxApp));
        Assert.Equal("Screenshot", ShortcutPage.GetDefaultTitle(FrontendShortcutEditorActionKind.ScreenshotFullscreen));
        Assert.True(ShortcutPage.ShouldApplyDefaultTitle(true, string.Empty, null));
        Assert.True(ShortcutPage.ShouldApplyDefaultTitle(true, "Screenshot", "Screenshot"));
        Assert.False(ShortcutPage.ShouldApplyDefaultTitle(true, "My Capture", "Screenshot"));
        Assert.False(ShortcutPage.ShouldApplyDefaultTitle(false, "Persisted title", null));
    }

    [Fact]
    public void Missing_prerequisites_start_automatically_after_activation_and_reuse_center_m_confirmation()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));
        var devicePage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        var centerMTransition = ExtractMethod(devicePage, "private async Task RequestCenterMTransitionAsync");

        Assert.Contains("_frontend.CaptureStatusAsync()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("snapshot.CanInstallRequiredComponents", mainWindow, StringComparison.Ordinal);
        Assert.Contains("RunPrerequisiteSetupAsync()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("RequestPrerequisiteSetupActivation()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_prerequisiteSetupInProgress", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_prerequisiteSetupAttemptedForCurrentProcess", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("FrontendPrerequisiteSetupResultKind.Cancelled", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Setup required", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Not now", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Restart required", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("shutdown.exe", mainWindow, StringComparison.Ordinal);
        Assert.Contains("DeviceContent.ConfirmCenterMDisableAfterPrerequisiteSetupAsync()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ConfirmCenterMDisableAfterPrerequisiteSetupAsync", devicePage, StringComparison.Ordinal);
        Assert.Contains("RequestCenterMTransitionAsync(centerMEnabled: false)", devicePage, StringComparison.Ordinal);
        Assert.Contains("_frontend.RequestCenterMAuthorityTransitionAsync(centerMEnabled)", devicePage, StringComparison.Ordinal);
        Assert.Contains("if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;", centerMTransition, StringComparison.Ordinal);
        Assert.True(
            centerMTransition.IndexOf("dialog.ShowAsync()", StringComparison.Ordinal)
            < centerMTransition.IndexOf("RequestCenterMAuthorityTransitionAsync(centerMEnabled)", StringComparison.Ordinal),
            "The Center M mutation request must remain after explicit confirmation.");
        Assert.Contains("_frontend.StateInvalidated += OnFrontendStateInvalidated", mainWindow, StringComparison.Ordinal);

        // The snapshot is now routed to the owner pages instead of a Status page.
        Assert.Contains("DeviceContent.RenderDeviceSummary(snapshot)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SettingsContent.RenderRequiredComponents(snapshot)", mainWindow, StringComparison.Ordinal);

        // The removed "Check Status" wording no longer points users at a page that does not exist.
        Assert.DoesNotContain("Check Status", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Normal_uninstall_paths_never_recursively_delete_the_persistent_data_root()
    {
        var root = FindRepositoryRoot();
        var bootstrap = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Install/UninstallBootstrap.cs"));
        var safeUninstall = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Install/SafeUninstall.cs"));
        var dataPaths = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Install/AddonDataPaths.cs"));
        var boundedCleanup = ExtractMethod(bootstrap, "internal static bool RunBoundedLocalCleanup");
        var fastCallback = ExtractMethod(bootstrap, "internal static void RunFastCallbackOnly");
        var handoff = ExtractMethod(safeUninstall, "internal static FinalUninstallHandoffResult PreserveUserDataAndLaunchVeloPack");

        Assert.DoesNotContain("DeleteFullResetRoot", bootstrap + safeUninstall + dataPaths, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Delete", boundedCleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Delete", fastCallback, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Delete(dataRoot", handoff, StringComparison.Ordinal);
        Assert.Contains("RunBoundedLocalCleanup(runtimeReleased)", fastCallback, StringComparison.Ordinal);
        Assert.Contains("deleteOwnedRuntimeDirectory(clawHudRuntimeRoot)", handoff, StringComparison.Ordinal);
        Assert.Contains("shutdownLogs()", handoff, StringComparison.Ordinal);
        Assert.Contains("launch(startInfo)", handoff, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_invalidations_dispatch_its_refresh_batch_and_keep_device_profile_dispatch_local()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));
        var invalidation = ExtractMethod(mainWindow, "private void OnFrontendStateInvalidated");
        var refreshBatch = ExtractMethod(mainWindow, "private void RefreshInvalidatedFrontendStateOnUiThread");

        Assert.Contains("DispatcherQueue.HasThreadAccess", invalidation, StringComparison.Ordinal);
        Assert.Contains("RefreshInvalidatedFrontendStateOnUiThread();", invalidation, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue(RefreshInvalidatedFrontendStateOnUiThread)", invalidation, StringComparison.Ordinal);
        Assert.Contains("UI dispatcher is unavailable", invalidation, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestAppUpdateRefresh", invalidation, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestGamingHomeRefresh", invalidation, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestClawHudRefresh", invalidation, StringComparison.Ordinal);

        var expectedRefreshes = new[]
        {
            "_ = RefreshSystemStatusAsync();",
            "SettingsContent.RequestAppUpdateRefresh();",
            "SettingsContent.RequestGamingHomeRefresh();",
            "OverlayContent.RequestClawHudRefresh();",
            "ShortcutContent.RequestRefresh();",
        };
        var previousIndex = -1;
        foreach (var refresh in expectedRefreshes)
        {
            var index = refreshBatch.IndexOf(refresh, StringComparison.Ordinal);
            Assert.True(index > previousIndex, $"Missing or out-of-order invalidation refresh: {refresh}");
            previousIndex = index;
        }
        Assert.DoesNotContain("RequestStatusRefresh()", refreshBatch, StringComparison.Ordinal);
        Assert.DoesNotContain("DeviceContent", refreshBatch, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileContent", refreshBatch, StringComparison.Ordinal);

        var devicePage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        var profilePage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var deviceInvalidation = ExtractMethod(devicePage, "private void OnStateInvalidated");
        var profileInvalidation = ExtractMethod(profilePage, "private void OnStateInvalidated");

        Assert.Contains("DispatcherQueue.TryEnqueue", deviceInvalidation, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue", profileInvalidation, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlay_page_owns_the_main_ui_clawhud_surface_and_settings_page_does_not()
    {
        var root = FindRepositoryRoot();
        var overlayXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml"));
        var overlayCodeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml.cs"));
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml"));
        var settingsCodeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));

        Assert.Contains("SettingsCard", overlayXaml, StringComparison.Ordinal);
        foreach (var required in new[]
        {
            "Enable HUD",
            "Display mode",
            "HUD size",
            "Header=\"Font\"",
            "Header=\"Alignment\"",
            "Background width",
            "HUD opacity",
            "Intel VRR Range Fix",
        })
            Assert.Contains(required, overlayXaml, StringComparison.Ordinal);

        Assert.Contains("FrontendClawHudSnapshot", overlayCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CaptureClawHudAsync", overlayCodeBehind, StringComparison.Ordinal);
        Assert.Contains("PreviewOpacity", overlayCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CommitOpacity", overlayCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ClawHudOpacitySlider_KeyUp", overlayXaml, StringComparison.Ordinal);
        Assert.Contains("ClawHudRetryButton", overlayXaml, StringComparison.Ordinal);
        Assert.Contains("SetClawHudEnabledAsync(true)", overlayCodeBehind, StringComparison.Ordinal);
        foreach (var sectionHeading in new[] { "ClawHUD", "Display", "Appearance", "Display compatibility" })
            Assert.DoesNotContain($"Text=\"{sectionHeading}\"", overlayXaml, StringComparison.Ordinal);

        foreach (var icon in new[]
        {
            "<SymbolIcon Symbol=\"Setting\" />",
            "<SymbolIcon Symbol=\"View\" />",
            "<SymbolIcon Symbol=\"FontSize\" />",
            "<SymbolIcon Symbol=\"Font\" />",
            "<SymbolIcon Symbol=\"AlignCenter\" />",
            "<SymbolIcon Symbol=\"FullScreen\" />",
            "<SymbolIcon Symbol=\"FontColor\" />",
            "<SymbolIcon Symbol=\"Repair\" />",
        })
            Assert.Contains(icon, overlayXaml, StringComparison.Ordinal);

        var vrrCardStart = overlayXaml.IndexOf("<ctcontrols:SettingsCard Header=\"Intel VRR Range Fix\"", StringComparison.Ordinal);
        var vrrCardEnd = overlayXaml.IndexOf("</ctcontrols:SettingsCard>", vrrCardStart, StringComparison.Ordinal);
        Assert.True(vrrCardStart >= 0 && vrrCardEnd > vrrCardStart);
        var vrrCard = overlayXaml[vrrCardStart..(vrrCardEnd + "</ctcontrols:SettingsCard>".Length)];
        Assert.DoesNotContain("Description=\"Restore the supported VRR range", vrrCard, StringComparison.Ordinal);
        Assert.Contains("<ctcontrols:SettingsCard.Description>", vrrCard, StringComparison.Ordinal);
        Assert.Contains("ClawHudVrrResultText", vrrCard, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", vrrCard, StringComparison.Ordinal);
        Assert.Contains("OffContent=\"\"", vrrCard, StringComparison.Ordinal);
        Assert.Contains("OnContent=\"\"", vrrCard, StringComparison.Ordinal);
        Assert.DoesNotContain("<StackPanel", vrrCard, StringComparison.Ordinal);

        Assert.DoesNotContain("ClawHud", settingsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ClawHud", settingsCodeBehind, StringComparison.Ordinal);
        Assert.Contains("OverlayContent.RequestClawHudRefresh()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("OverlayContent.Initialize(_frontend)", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_main_ui_toggle_switch_hides_on_and_off_content()
    {
        var root = FindRepositoryRoot();
        var uiRoot = Path.Combine(root, "src/SteamInputAddonforClaw.UI");

        foreach (var path in Directory.EnumerateFiles(uiRoot, "*.xaml", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(path);
            foreach (var toggle in document.Descendants().Where(element => element.Name.LocalName == "ToggleSwitch"))
            {
                Assert.True(toggle.Attribute("OffContent") is { Value: "" },
                    $"{Path.GetRelativePath(root, path)} has a ToggleSwitch with visible Off content.");
                Assert.True(toggle.Attribute("OnContent") is { Value: "" },
                    $"{Path.GetRelativePath(root, path)} has a ToggleSwitch with visible On content.");
            }
        }
    }

    [Fact]
    public void Profile_tab_activation_refreshes_the_catalog_only_when_selected()
    {
        var root = FindRepositoryRoot();
        var profileCodeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));

        Assert.Contains("internal void Activate()", profileCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_ = RefreshGamesAsync();", profileCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_selectedGame is not null) _ = CaptureSelectedAsync(_selectedGame.AppId);", profileCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private async Task RefreshGamesAsync()", profileCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_catalog = await _frontend.ScanProfileGamesAsync();", profileCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_frontend?.StateInvalidated -= OnStateInvalidated;", profileCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_expanders_have_parent_icons_and_option_cards_have_no_icons()
    {
        var root = FindRepositoryRoot();
        var controllerXaml = XDocument.Load(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml"));
        var cards = controllerXaml.Descendants().Where(element => element.Name.LocalName == "SettingsCard").ToArray();
        var expanders = controllerXaml.Descendants().Where(element => element.Name.LocalName == "SettingsExpander").ToArray();

        Assert.Equal(14, cards.Length);
        Assert.Equal(5, expanders.Length);
        Assert.All(expanders, expander => Assert.Single(
            expander.Elements(), element => element.Name.LocalName == "SettingsExpander.HeaderIcon"));
        Assert.All(cards, card => Assert.DoesNotContain(
            card.Elements(), element => element.Name.LocalName == "SettingsCard.HeaderIcon"));
    }

    [Fact]
    public void Settings_header_icons_are_unique()
    {
        var root = FindRepositoryRoot();
        var settingsXaml = XDocument.Load(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml"));
        var symbols = settingsXaml.Descendants()
            .Where(element => element.Name.LocalName == "SymbolIcon")
            .Select(element => (string?)element.Attribute("Symbol"))
            .Where(symbol => symbol is not null)
            .ToArray();

        Assert.Equal(symbols.Length, symbols.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Overlay_opacity_commit_coalesces_rapid_keyboard_adjustments_into_a_follow_up_commit()
    {
        var queued = 0;
        int? pending = null;

        Assert.True(OverlayPage.TryClaimOpacityCommit(ref queued, ref pending, 55));
        Assert.False(OverlayPage.TryClaimOpacityCommit(ref queued, ref pending, 60));
        Assert.Equal(60, pending);

        // The first response may re-render the authoritative value (55), so the follow-up must
        // use the captured latest request (60) rather than reading the slider again.
        Assert.Equal(60, OverlayPage.CompleteOpacityCommit(ref queued, ref pending));
        Assert.Null(pending);

        // The follow-up owns the claim until it completes; after that, a new commit can start.
        Assert.Null(OverlayPage.CompleteOpacityCommit(ref queued, ref pending));
        Assert.True(OverlayPage.TryClaimOpacityCommit(ref queued, ref pending, 65));
    }

    [Fact]
    public void Settings_page_shows_read_only_required_components_and_no_launch_at_startup_toggle()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs"));

        // Required Components (read-only) replaces the Status page's Routing Components group.
        Assert.Contains("x:Name=\"RequiredComponentsExpander\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Required Components\"", xaml, StringComparison.Ordinal);
        foreach (var component in new[] { "\"HidHide\"", "\"usbip-win2\"", "\"VIIPER\"" })
            Assert.Contains(component, codeBehind, StringComparison.Ordinal);
        Assert.Contains("snapshot.Prerequisites.HidHideStatus", codeBehind, StringComparison.Ordinal);
        Assert.Contains("snapshot.Prerequisites.UsbIpStatus", codeBehind, StringComparison.Ordinal);
        Assert.Contains("snapshot.Prerequisites.ViiperStatus", codeBehind, StringComparison.Ordinal);

        // The Developer Menu entry stays.
        Assert.Contains("x:Name=\"DeveloperMenuCard\"", xaml, StringComparison.Ordinal);

        // The launch-at-startup preference UI and its page-local state are gone.
        Assert.DoesNotContain("LaunchAtStartupCard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchAtWindowsStartupToggleSwitch", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchAtWindowsStartup", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivateAsync", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_feature_order_collapsed_defaults_and_resolution_contract_are_explicit()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));

        var fpsDeclarationStart = xaml.IndexOf("<ctcontrols:SettingsExpander x:Name=\"IntelFpsExpander\"", StringComparison.Ordinal);
        var fpsDeclarationEnd = xaml.IndexOf('>', fpsDeclarationStart);
        Assert.True(fpsDeclarationStart >= 0 && fpsDeclarationEnd > fpsDeclarationStart);
        var fpsDeclaration = xaml[fpsDeclarationStart..fpsDeclarationEnd];
        Assert.DoesNotContain("Visibility=\"Collapsed\"", fpsDeclaration, StringComparison.Ordinal);
        Assert.True(xaml.IndexOf("Header=\"TDP Control\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"CPU Boost\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Header=\"CPU Boost\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"Windows Power Mode\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Header=\"Windows Power Mode\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"Minimum GPU Clock\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Header=\"Minimum GPU Clock\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"Intel FPS Limit\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Header=\"Intel FPS Limit\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"Resolution\"", StringComparison.Ordinal));
        foreach (var expanderName in new[] { "TdpExpander", "CpuBoostExpander", "PowerModeExpander", "GpuMinimumClockExpander", "IntelFpsExpander" })
        {
            var declarationStart = xaml.IndexOf($"x:Name=\"{expanderName}\"", StringComparison.Ordinal);
            var declarationEnd = xaml.IndexOf('>', declarationStart);
            Assert.True(declarationStart >= 0 && declarationEnd > declarationStart);
            Assert.Contains("IsExpanded=\"False\"", xaml[declarationStart..declarationEnd], StringComparison.Ordinal);
        }
        Assert.DoesNotContain("x:Name=\"DisplayExpander\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsExpanded=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ctcontrols:SettingsCard Header=\"Resolution\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Expander.IsExpanded =", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new(null, null, \"Do not change\"), new(1920, 1200, \"1920 × 1200\"), new(1920, 1080, \"1920 × 1080\"), new(1680, 1050, \"1680 × 1050\"), new(1440, 900, \"1440 × 900\")", codeBehind, StringComparison.Ordinal);
        Assert.Contains("HeaderIcon", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xEC4A;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE945;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xEEA1;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE83F;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE7F4;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FpsEnabledToggle.IsEnabled = snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable && snapshot.FpsLimit?.Available == true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("snapshot.FpsLimit?.Available == true ? \"Uses Intel's official API. Some games may not support FPS limiting.\" : snapshot.FpsLimit?.UnavailableReason", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock Text=\"Profile\" VerticalAlignment=\"Center\"/>", xaml, StringComparison.Ordinal);
        Assert.Contains("<ctcontrols:SettingsCard Grid.Row=\"1\">", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectedGameNameText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProfileEnabledToggle\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"ProfileEnabledToggle\" Grid.Column=\"1\" HorizontalAlignment=\"Right\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid x:Name=\"DetailPanel\" Visibility=\"Collapsed\" RowSpacing=\"16\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"*\"/></Grid.RowDefinitions>", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid Grid.Row=\"1\" ColumnSpacing=\"12\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer Grid.Row=\"2\" HorizontalScrollMode=\"Disabled\" HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"DetailPanel\" Visibility=\"Collapsed\" HorizontalContentAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollMode=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnContent=\"On\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OffContent=\"Off\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_navigation_content_stretches_horizontally()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml"));
        var navigationStart = xaml.IndexOf("<NavigationView", StringComparison.Ordinal);
        Assert.True(navigationStart >= 0);

        var navigationEnd = xaml.IndexOf('>', navigationStart);
        Assert.True(navigationEnd > navigationStart);

        var declaration = xaml[navigationStart..navigationEnd];
        Assert.Contains("x:Name=\"MainNavigationView\"", declaration, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", declaration, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_page_stretches_its_root_content_horizontally()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var userControlEnd = xaml.IndexOf('>');

        Assert.True(userControlEnd >= 0);

        var declaration = xaml[..userControlEnd];
        Assert.Contains("x:Class=\"SteamInputAddonforClaw.Views.ProfilePage\"", declaration, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", declaration, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_power_mode_enable_mutation_preserves_apply_failure_contract()
    {
        var root = FindRepositoryRoot();
        var control = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs"));
        var start = control.IndexOf("SetGameProfilePowerModeEnabledAsync", StringComparison.Ordinal);
        var end = control.IndexOf("SetGameProfileCpuBoostAcAsync", start, StringComparison.Ordinal);
        var method = control[start..end];
        var mutationStart = control.IndexOf("private async Task<FrontendGameProfileMutationResult> MutateGame(", StringComparison.Ordinal);
        var mutationEnd = control.IndexOf("private bool IsActiveSteamProfileTarget(", mutationStart, StringComparison.Ordinal);
        var dispatcherStart = control.IndexOf("private async Task<ProfileApplyResult> ReconcileActiveProfileMutationAsync(", StringComparison.Ordinal);
        var dispatcherEnd = control.IndexOf("private static bool IncludesFeature(", dispatcherStart, StringComparison.Ordinal);
        var steamMutation = control[mutationStart..mutationEnd];
        var dispatcher = control[dispatcherStart..dispatcherEnd];
        var steamEnabledStart = control.IndexOf("SetGameProfileEnabledAsync(", StringComparison.Ordinal);
        var steamEnabledEnd = control.IndexOf("SetGameProfileCpuBoostEnabledAsync(", steamEnabledStart, StringComparison.Ordinal);
        var xboxEnabledStart = control.IndexOf("SetXboxGameProfileEnabledAsync(", StringComparison.Ordinal);
        var xboxEnabledEnd = control.IndexOf("SetXboxGameProfileCpuBoostEnabledAsync(", xboxEnabledStart, StringComparison.Ordinal);
        var applyKindStart = control.IndexOf("private enum ProfileApplyKind", StringComparison.Ordinal);
        var applyKindEnd = control.IndexOf("private sealed record ProfileApplyFailure", applyKindStart, StringComparison.Ordinal);
        var steamEnabled = control[steamEnabledStart..steamEnabledEnd];
        var xboxEnabled = control[xboxEnabledStart..xboxEnabledEnd];
        var applyKind = control[applyKindStart..applyKindEnd];

        Assert.Contains("ProfileApplyKind.PowerMode", method, StringComparison.Ordinal);
        Assert.Contains("ProfileApplyKind.ExistingProfileEnable", steamEnabled, StringComparison.Ordinal);
        Assert.Contains("ProfileApplyKind.All", xboxEnabled, StringComparison.Ordinal);
        Assert.Contains("ExistingProfileEnable = CpuBoost | Tdp | PowerMode | FpsLimit", applyKind, StringComparison.Ordinal);
        Assert.Contains("All = ExistingProfileEnable | Resolution", applyKind, StringComparison.Ordinal);
        Assert.Contains("(kind & featureKind) != 0", control, StringComparison.Ordinal);
        Assert.Contains("ReconcileActiveProfileMutationAsync(applyKind, \"SteamProfileMutation\")", steamMutation, StringComparison.Ordinal);
        Assert.Contains("ReconcileActiveProfileMutationAsync(applyKind, \"XboxProfileMutation\")", control, StringComparison.Ordinal);
        Assert.Contains("Apply(ProfileApplyKind.PowerMode, \"Power Mode\"", dispatcher, StringComparison.Ordinal);
        Assert.Contains("powerModeRuntime.ReconcileWithResult()", dispatcher, StringComparison.Ordinal);
        Assert.Contains("FrontendGameProfileMutationOutcome.ApplyFailed", steamMutation, StringComparison.Ordinal);
        Assert.Contains("$\"{feature} apply failed.\"", dispatcher, StringComparison.Ordinal);
        Assert.DoesNotContain("ReconcileXboxProfileMutationAsync", control, StringComparison.Ordinal);
        Assert.DoesNotContain("XboxProfileApplyKind", control, StringComparison.Ordinal);
    }

    [Fact]
    public void Developer_cards_have_unique_gyro_icon_and_requested_order()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml"));

        Assert.Equal(1, page.Split("Symbol=\"Rotate\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Header=\"Test Mode\"", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("Text=\"Environment Discovery\"", StringComparison.Ordinal) < page.IndexOf("Header=\"Vibration Test\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf("Text=\"Environment Discovery\"", StringComparison.Ordinal) < page.IndexOf("Header=\"PID1902 Input Cadence\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf("Header=\"PID1902 Input Cadence\"", StringComparison.Ordinal) < page.IndexOf("Header=\"Vibration Test\"", StringComparison.Ordinal));
        Assert.Contains("Content=\"Run 10s Test\"", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("Header=\"Vibration Test\"", StringComparison.Ordinal) < page.IndexOf("Header=\"Gyro / Sensor Test\"", StringComparison.Ordinal));
        Assert.DoesNotContain("Header=\"Logging\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LogLevelComboBox", page, StringComparison.Ordinal);
        Assert.Contains("Content=\"Log Folder\"", page, StringComparison.Ordinal);
        Assert.Contains("Click=\"OpenLogFolderButton_Click\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Vibration_test_card_describes_only_the_full1902_xbox360_terminal_stop_diagnostic()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml"));
        var cardStart = page.IndexOf("Header=\"Vibration Test\"", StringComparison.Ordinal);
        Assert.True(cardStart >= 0);
        var nextCardStart = page.IndexOf("<ctcontrols:SettingsCard", cardStart + 1, StringComparison.Ordinal);
        var card = page[cardStart..(nextCardStart >= 0 ? nextCardStart : page.Length)];

        Assert.Contains("Full1902 Xbox360 terminal STOP callback diagnostic", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Steam Deck", card, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Haptic", card, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Vibration_test_page_exposes_only_explicit_apply_restore_probe_actions()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs"));
        var activateStart = code.IndexOf("internal void Activate()", StringComparison.Ordinal);
        var deactivateStart = code.IndexOf("internal void Deactivate()", activateStart, StringComparison.Ordinal);
        Assert.True(activateStart >= 0 && deactivateStart > activateStart);

        Assert.Contains("Vibration Profile 0/100 Probe", page, StringComparison.Ordinal);
        Assert.Contains("Developer-only physical hardware mutation. No SyncToROM is sent.", page, StringComparison.Ordinal);
        Assert.Contains("press Left Test once, then Right Test once", page, StringComparison.Ordinal);
        Assert.Contains("Content=\"Apply Left 0 / Right 100\"", page, StringComparison.Ordinal);
        Assert.Contains("Content=\"Restore 50 / 50\"", page, StringComparison.Ordinal);
        Assert.Contains("FrontendControllerVibrationProfileWriteProbeMode.ApplyZeroHundred", code, StringComparison.Ordinal);
        Assert.Contains("FrontendControllerVibrationProfileWriteProbeMode.RestoreFiftyFifty", code, StringComparison.Ordinal);
        Assert.Contains("ApplyVibrationProfileProbeButton.IsEnabled = !_profileProbeBusy", code, StringComparison.Ordinal);
        Assert.Contains("RestoreVibrationProfileProbeButton.IsEnabled = !_profileProbeBusy", code, StringComparison.Ordinal);
        Assert.Contains("the motors may remain at test values", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RunControllerVibrationProfileWriteProbeAsync", code[activateStart..deactivateStart], StringComparison.Ordinal);
    }

    [Fact]
    public void Battery_charge_limit_test_page_reports_terminal_failure_results()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml.cs"));

        Assert.Equal(2, page.Split("ResultText.Text = \"Result: Failed\";", StringSplitOptions.None).Length - 1);
        Assert.Contains("snapshot.FailureMessage is not null", page, StringComparison.Ordinal);
        Assert.Contains("snapshot.Available ? \"Result: Failed\" : \"Result: Unavailable\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Battery_charge_limit_test_page_exposes_the_automated_validation_surface()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml.cs"));

        Assert.Contains("Text=\"Automated Validation\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartValidationButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ValidationProgressText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ValidationCurrentStepText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ValidationReportPathText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("new BatteryChargeLimitValidationRunner", codeBehind, StringComparison.Ordinal);
        Assert.Contains("UiLog.DirectoryPath", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BackButton.IsEnabled = !busy", codeBehind, StringComparison.Ordinal);
        Assert.Contains("StartValidationButton.IsEnabled = !busy", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_blocks_navigation_while_battery_validation_is_running()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml.cs"));
        var window = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));

        Assert.Contains("internal bool IsValidationRunning => _busy;", page, StringComparison.Ordinal);
        Assert.Contains("private bool IsBatteryValidationBlockingNavigation()", window, StringComparison.Ordinal);
        Assert.Contains("_batteryChargeLimitTestContent is { IsValidationRunning: true }", window, StringComparison.Ordinal);
        Assert.Contains("if (IsBatteryValidationBlockingNavigation())", window, StringComparison.Ordinal);
        Assert.Contains("sender.SelectedItem = sender.SettingsItem", window, StringComparison.Ordinal);
        Assert.Contains("args.Handled = true", window, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, Visibility.Collapsed)]
    [InlineData(true, Visibility.Visible)]
    public void Settings_developer_menu_card_visibility_follows_bootstrap_flag(bool enabled, Visibility expected)
    {
        Assert.Equal(expected, SettingsPage.GetDeveloperMenuCardVisibility(enabled));
    }

    private static IReadOnlyList<string> References(string relativeProjectPath)
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileName((string?)element.Attribute("Include") ?? string.Empty))
            .ToArray();
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var openBrace = source.IndexOf('{', start);
        var depth = 0;
        var index = openBrace;
        for (; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) break;
        }
        return source[start..(index + 1)];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
