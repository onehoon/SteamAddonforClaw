using System.Xml.Linq;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Views;
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
    public void UI_and_overlay_use_framework_dependent_windows_app_runtime_contract()
    {
        var root = FindRepositoryRoot();
        foreach (var relativePath in new[]
        {
            "src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj",
            "src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj",
        })
        {
            var project = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.Contains("<WindowsPackageType>None</WindowsPackageType>", project, StringComparison.Ordinal);
            Assert.Contains("<UseWinUI>true</UseWinUI>", project, StringComparison.Ordinal);
            Assert.Contains("<SelfContained>false</SelfContained>", project, StringComparison.Ordinal);
            Assert.Contains("<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>", project, StringComparison.Ordinal);
            Assert.Contains("<RuntimeIdentifier>win-x64</RuntimeIdentifier>", project, StringComparison.Ordinal);
            Assert.Contains("<PackageReference Include=\"Microsoft.WindowsAppSDK\" Version=\"2.5.1\"", project, StringComparison.Ordinal);
            Assert.DoesNotContain("Bootstrap.Initialize", project, StringComparison.Ordinal);
        }
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

    [Fact] // Full1902 Cleanup J: the Vibration Test page stays in the Developer navigation shell but
           // is a static unavailable placeholder -- it must not open/close a Runtime vibration session
           // or send any vibration RPC while the feature is disconnected.
    public void Vibration_test_page_is_a_static_unavailable_shell_with_no_vibration_rpc_dependency()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs"));
        var mainWindowXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));

        Assert.Contains("VibrationTestPage", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("unavailable in this build", page, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "RunVibrationTestAsync", "OpenVibrationTestSessionAsync", "CloseVibrationTestSessionAsync" })
        {
            Assert.DoesNotContain(forbidden, page, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, mainWindow, StringComparison.Ordinal);
        }
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
        // Whole-transport failure fails closed: all three children render Unavailable and the TDP
        // dirty draft is not preserved as if it were still authoritative/editable (section 10.4).
        Assert.Contains("Render(FrontendCpuBoostSnapshot.Unavailable);", refresh, StringComparison.Ordinal);
        Assert.Contains("RenderTdp(FrontendTdpSnapshot.Unavailable, preserveDirtyDraft: false);", refresh, StringComparison.Ordinal);
        Assert.Contains("RenderPowerMode(FrontendPowerModeSnapshot.Unavailable);", refresh, StringComparison.Ordinal);

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
    public void Device_feature_expanders_follow_authoritative_enabled_snapshots()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        var normalizedXaml = xaml.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("IsExpanded=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CpuBoostExpander.IsExpanded = snapshot.Enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PowerModeExpander.IsExpanded = snapshot.Enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TdpExpander.IsExpanded = snapshot.Configuration?.Enabled == true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("<ctcontrols:SettingsExpander.HeaderIcon>\n                    <FontIcon Glyph=\"&#xE83F;\" />\n                </ctcontrols:SettingsExpander.HeaderIcon>", normalizedXaml, StringComparison.Ordinal);
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
        Assert.Contains("CaptureBatteryChargeLimitAsync", codeBehind, StringComparison.Ordinal);
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
        Assert.Contains("CanReorderItems=\"True\"", shortcutXaml, StringComparison.Ordinal);
        Assert.Contains("Screenshot", shortcutXaml, StringComparison.Ordinal);
        Assert.Contains("FolderPicker(windowId)", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("MutateShortcutAsync", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("SetScreenshotSaveFolderAsync", shortcutCode, StringComparison.Ordinal);
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
    public void Removing_status_ui_keeps_the_window_level_status_capture_and_prerequisite_setup_path()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"));

        // The Status page is gone but the frontend status snapshot pipeline that drives the
        // prerequisite setup prompt must remain intact.
        Assert.Contains("_frontend.CaptureStatusAsync()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("snapshot.CanInstallRequiredComponents", mainWindow, StringComparison.Ordinal);
        Assert.Contains("PromptForPrerequisiteSetupAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_frontend.StateInvalidated += OnFrontendStateInvalidated", mainWindow, StringComparison.Ordinal);

        // The snapshot is now routed to the owner pages instead of a Status page.
        Assert.Contains("DeviceContent.RenderDeviceSummary(snapshot)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SettingsContent.RenderRequiredComponents(snapshot)", mainWindow, StringComparison.Ordinal);

        // The removed "Check Status" wording no longer points users at a page that does not exist.
        Assert.DoesNotContain("Check Status", mainWindow, StringComparison.Ordinal);
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
    public void Controller_option_cards_have_header_icons()
    {
        var root = FindRepositoryRoot();
        var controllerXaml = XDocument.Load(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml"));
        var cards = controllerXaml.Descendants().Where(element => element.Name.LocalName == "SettingsCard").ToArray();
        var icons = cards.Select(card => card.Elements().SingleOrDefault(element => element.Name.LocalName == "SettingsCard.HeaderIcon"))
            .ToArray();

        Assert.Equal(10, cards.Length);
        Assert.All(icons, icon => Assert.NotNull(icon));
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
    public void Profile_feature_order_expander_state_and_resolution_contract_are_explicit()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));

        var fpsDeclarationStart = xaml.IndexOf("<ctcontrols:SettingsExpander x:Name=\"IntelFpsExpander\"", StringComparison.Ordinal);
        var fpsDeclarationEnd = xaml.IndexOf('>', fpsDeclarationStart);
        Assert.True(fpsDeclarationStart >= 0 && fpsDeclarationEnd > fpsDeclarationStart);
        var fpsDeclaration = xaml[fpsDeclarationStart..fpsDeclarationEnd];
        Assert.Contains("Visibility=\"Collapsed\"", fpsDeclaration, StringComparison.Ordinal);
        Assert.True(xaml.IndexOf("Header=\"TDP Control\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"Intel FPS Limit\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Header=\"Intel FPS Limit\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"CPU Boost\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Header=\"CPU Boost\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"Windows Power Mode\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Header=\"Windows Power Mode\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"Resolution\"", StringComparison.Ordinal));
        Assert.DoesNotContain("x:Name=\"DisplayExpander\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsExpanded=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ctcontrols:SettingsCard Header=\"Resolution\">", xaml, StringComparison.Ordinal);
        Assert.Contains("IntelFpsExpander.IsExpanded = snapshot.FpsLimit?.Enabled == true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PowerModeExpander.IsExpanded = snapshot.PowerMode?.Enabled == true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CpuBoostExpander.IsExpanded = snapshot.CpuBoost.Enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TdpExpander.IsExpanded = snapshot.Tdp.Enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new(null, null, \"Do not change\"), new(1920, 1200, \"1920 × 1200\"), new(1920, 1080, \"1920 × 1080\"), new(1680, 1050, \"1680 × 1050\"), new(1440, 900, \"1440 × 900\")", codeBehind, StringComparison.Ordinal);
        Assert.Contains("HeaderIcon", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xEC4A;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE83F;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE7F4;\"", xaml, StringComparison.Ordinal);
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

        Assert.Contains("ReconcileWithResult", method, StringComparison.Ordinal);
        Assert.Contains("FrontendGameProfileMutationOutcome.ApplyFailed", method, StringComparison.Ordinal);
        Assert.Contains("Power Mode apply failed.", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Developer_cards_have_unique_gyro_icon_and_requested_order()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml"));

        Assert.Equal(1, page.Split("Symbol=\"Rotate\"", StringSplitOptions.None).Length - 1);
        Assert.True(page.IndexOf("Header=\"Test Mode\"", StringComparison.Ordinal) < page.IndexOf("Text=\"Environment Discovery\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf("Text=\"Environment Discovery\"", StringComparison.Ordinal) < page.IndexOf("Header=\"Vibration Test\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf("Header=\"Vibration Test\"", StringComparison.Ordinal) < page.IndexOf("Header=\"Gyro / Sensor Test\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf("Header=\"Gyro / Sensor Test\"", StringComparison.Ordinal) < page.IndexOf("Header=\"Logging\"", StringComparison.Ordinal));
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
        Assert.Contains("BatteryChargeLimitTestContent.IsValidationRunning", window, StringComparison.Ordinal);
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
