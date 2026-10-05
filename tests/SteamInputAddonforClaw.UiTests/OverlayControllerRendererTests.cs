using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class OverlayControllerRendererTests
{
    [Fact]
    public void Controller_tab_keeps_two_runtime_owned_mapping_rows_and_adds_existing_shared_controls()
    {
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");

        Assert.Contains("AddonQuickSettingsTabId.Controller => BuildControllerPage(rows)", shell);
        Assert.DoesNotContain("AddonQuickSettingsTabId.Controller => CreatePlaceholderPage", shell);
        Assert.Equal(3, CountOccurrences(controller, "CreateOverlaySectionCard("));
        Assert.Contains("CreateControllerSection(\"M1 / M2\")", controller);
        Assert.Contains("CreateControllerSection(\"Vibration Strength\")", controller);
        Assert.DoesNotContain("CreateControllerSection(\"Joystick LED\")", controller);
        Assert.Equal(1, CountOccurrences(controller, "new OverlayValueRow(\"M1\""));
        Assert.Equal(1, CountOccurrences(controller, "new OverlayValueRow(\"M2\""));
        Assert.Equal(2, CountOccurrences(controller, "OverlayValueButtonKind.DiscreteChoice"));
        Assert.Contains("AddBackButtonMappingRow(mappingDetails, rows, m1Row)", controller);
        Assert.Contains("AddBackButtonMappingRow(mappingDetails, rows, m2Row)", controller);
        Assert.Contains("CreateOverlayDetailStack()", controller);
        Assert.Contains("RegisterRowPointerSelection(row.Container)", controller);
        Assert.Contains("rows.Add(new(row.Container, row.Capabilities))", controller);
        Assert.Contains("_rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Controller), preferredIndex);", controller);
    }

    [Fact]
    public void Controller_page_uses_only_back_mapping_bootstrap_availability_and_authoritative_state()
    {
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");

        Assert.Contains("private BackButtonMappingSettings _backButtonMapping = BackButtonMappingSettings.Default", controller);
        Assert.Contains("private bool _backButtonMappingAvailable", controller);
        Assert.Contains("private bool _backButtonMutationInFlight", controller);
        Assert.Contains("_backButtonMappingAvailable && !_backButtonMutationInFlight", controller);
        Assert.Contains("_backButtonMutationInFlight = false", controller);
        Assert.Contains("var minimum = (double)Xbox360BackButtonTarget.Disabled", controller);
        Assert.Contains("var maximum = (double)Xbox360BackButtonTarget.XboxGuide", controller);
        Assert.Contains("_m1MappingRow?.ApplyState(enabled, minimum, maximum, 1, (double)_backButtonMapping.M1)", controller);
        Assert.Contains("_m2MappingRow?.ApplyState(enabled, minimum, maximum, 1, (double)_backButtonMapping.M2)", controller);
        Assert.Contains("var preferredIndex = _rowSelection.SelectedIndex;", controller);
        Assert.Contains("if (!_backButtonMutationInFlight && !_controllerLedMutationInFlight && !_controllerVibrationMutationInFlight)", controller);
        Assert.Contains("_rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Controller), preferredIndex);", controller);
        Assert.Contains("HandleBackButtonMappingStateAsync", app);
        Assert.Contains("BackButtonMappingEditRequested", app);
        Assert.Contains("_backButtonMappingAvailable = state.Available", controller);
        Assert.DoesNotContain("FrontButtonMappingAvailable", controller);
        Assert.DoesNotContain("QuickSettingsPageId.Controller", controller);
        Assert.DoesNotContain("QuickSettingsRowId.M1", controller);
        Assert.DoesNotContain("QuickSettingsRowId.M2", controller);
    }

    [Theory]
    [InlineData(Xbox360BackButtonTarget.Disabled, "Disabled")]
    [InlineData(Xbox360BackButtonTarget.A, "A")]
    [InlineData(Xbox360BackButtonTarget.B, "B")]
    [InlineData(Xbox360BackButtonTarget.X, "X")]
    [InlineData(Xbox360BackButtonTarget.Y, "Y")]
    [InlineData(Xbox360BackButtonTarget.DPadUp, "D-Pad Up")]
    [InlineData(Xbox360BackButtonTarget.DPadRight, "D-Pad Right")]
    [InlineData(Xbox360BackButtonTarget.DPadDown, "D-Pad Down")]
    [InlineData(Xbox360BackButtonTarget.DPadLeft, "D-Pad Left")]
    [InlineData(Xbox360BackButtonTarget.LeftBumper, "Left Bumper (LB)")]
    [InlineData(Xbox360BackButtonTarget.RightBumper, "Right Bumper (RB)")]
    [InlineData(Xbox360BackButtonTarget.LeftTrigger, "Left Trigger (LT)")]
    [InlineData(Xbox360BackButtonTarget.RightTrigger, "Right Trigger (RT)")]
    [InlineData(Xbox360BackButtonTarget.LeftStickClick, "Left Stick Click (L3)")]
    [InlineData(Xbox360BackButtonTarget.RightStickClick, "Right Stick Click (R3)")]
    [InlineData(Xbox360BackButtonTarget.View, "View")]
    [InlineData(Xbox360BackButtonTarget.Menu, "Menu")]
    [InlineData(Xbox360BackButtonTarget.XboxGuide, "Xbox Guide")]
    public void Every_xbox360_target_has_the_main_ui_wording(Xbox360BackButtonTarget target, string label)
    {
        Assert.Equal(label, OverlayWindow.FormatBackButtonTarget((double)target));
    }

    [Fact]
    public void Mapping_candidate_preserves_the_other_button_and_allows_duplicate_targets()
    {
        var current = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.RightBumper);

        var editM1 = OverlayWindow.CreateBackButtonMappingCandidate(current, isM1: true, Xbox360BackButtonTarget.B);
        var editM2 = OverlayWindow.CreateBackButtonMappingCandidate(editM1, isM1: false, Xbox360BackButtonTarget.B);

        Assert.Equal(new BackButtonMappingSettings(Xbox360BackButtonTarget.B, Xbox360BackButtonTarget.RightBumper), editM1);
        Assert.Equal(new BackButtonMappingSettings(Xbox360BackButtonTarget.B, Xbox360BackButtonTarget.B), editM2);
        Assert.True(BackButtonMappingValidation.IsValid(editM2));
    }

    [Fact]
    public void Controller_keeps_the_M1_M2_heading_and_zero_gap_rows_without_auxiliary_text()
    {
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var build = controller[controller.IndexOf("private FrameworkElement BuildControllerPage", StringComparison.Ordinal)..
            controller.IndexOf("private void AddBackButtonMappingRow", StringComparison.Ordinal)];

        Assert.Contains("CreateControllerSection(\"M1 / M2\")", build);
        Assert.Contains("CreateControllerSection(\"Vibration Strength\")", build);
        Assert.Contains("var mappingDetails = CreateOverlayDetailStack();", build);
        Assert.Contains("mappingSection.Children.Add(mappingDetails);", build);
        Assert.Contains("AddBackButtonMappingRow(mappingDetails, rows, m1Row)", build);
        Assert.Contains("AddBackButtonMappingRow(mappingDetails, rows, m2Row)", build);
        Assert.DoesNotContain("AddBackButtonMappingRow(mappingSection, rows,", build);
        Assert.Contains("SetOverlaySectionSeparator(mappingCard, visible: false)", build);
        Assert.Contains("SetOverlaySectionSeparator(_controllerLedSectionCard, visible: true)", build);
        Assert.Contains("SetOverlaySectionSeparator(vibrationCard, visible: true)", build);
        Assert.DoesNotContain("_backButtonStatusText", controller);
        Assert.DoesNotContain("BackButtonMappingCaption", controller);
        Assert.Contains("FailureMessage = response.Succeeded", controller);
        Assert.Contains("RenderBackButtonMappingRows();", controller);
    }

    [Fact]
    public void Controller_shared_LED_and_vibration_use_existing_authority_without_test_operations()
    {
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");

        Assert.Contains("new OverlayToggleRow(\"Joystick LED\", RequestControllerLedEnabled)", controller);
        Assert.DoesNotContain("new OverlayToggleRow(\"Enabled\", RequestControllerLedEnabled)", controller);
        Assert.Contains("private StackPanel? _controllerLedDetailStack", controller);
        Assert.Contains("_controllerLedDetailStack = details;", controller);
        Assert.Contains("var details = CreateOverlayDetailStack();", controller);
        Assert.Equal(2, CountOccurrences(controller, "var details = CreateOverlayDetailStack();"));
        Assert.Contains("_controllerLedDetailStack.Visibility = _controllerLed.Enabled ? Visibility.Visible : Visibility.Collapsed", controller);
        Assert.Contains("new OverlayValueRow(\"Brightness\"", controller);
        Assert.Contains("new TextBlock { Text = \"Color\"", controller);
        Assert.Contains("new OverlayValueRow(\"Red\"", controller);
        Assert.Contains("new OverlayValueRow(\"Green\"", controller);
        Assert.Contains("new OverlayValueRow(\"Blue\"", controller);
        Assert.Contains("_controllerLedRedRow.Container, _controllerLedRedRow.Capabilities", controller);
        Assert.Contains("_controllerLedGreenRow.Container, _controllerLedGreenRow.Capabilities", controller);
        Assert.Contains("_controllerLedBlueRow.Container, _controllerLedBlueRow.Capabilities", controller);
        Assert.Contains("RequestControllerLedRgb(int? red, int? green, int? blue)", controller);
        Assert.Contains("Red = (byte)(red ?? _controllerLed.Red)", controller);
        Assert.Contains("Green = (byte)(green ?? _controllerLed.Green)", controller);
        Assert.Contains("Blue = (byte)(blue ?? _controllerLed.Blue)", controller);
        Assert.Contains("_controllerLedRedRow?.ApplyState(colorAvailable, 0, 255, 1, _controllerLed.Red)", controller);
        Assert.Contains("_controllerLedGreenRow?.ApplyState(colorAvailable, 0, 255, 1, _controllerLed.Green)", controller);
        Assert.Contains("_controllerLedBlueRow?.ApplyState(colorAvailable, 0, 255, 1, _controllerLed.Blue)", controller);
        Assert.Contains("_controllerLedBrightnessRow?.ApplyState(available && _controllerLed.Enabled", controller);
        Assert.Contains("private void BuildControllerVibrationRows", controller);
        Assert.Contains("new OverlayValueRow(\"Left Motor\"", controller);
        Assert.Contains("new OverlayValueRow(\"Right Motor\"", controller);
        Assert.Contains("AddControllerRow(details, rows, _leftVibrationRow.Container", controller);
        Assert.Contains("AddControllerRow(details, rows, _rightVibrationRow.Container", controller);
        Assert.DoesNotContain("ColorPicker", controller);
        Assert.DoesNotContain("ColorChanged", controller);
        Assert.Contains("ControllerLedEditRequested?.Invoke(settings)", controller);
        Assert.Contains("new OverlayValueRow(\"Left Motor\"", controller);
        Assert.Contains("new OverlayValueRow(\"Right Motor\"", controller);
        Assert.Contains("await Task.Delay(300, token)", controller);
        Assert.Contains("ControllerVibrationStrengthEditRequested?.Invoke(left, right)", controller);
        Assert.Contains("SendControllerLedMutationAsync", app);
        Assert.Contains("SendControllerVibrationMutationAsync", app);
        Assert.DoesNotContain("TestControllerVibrationMotorAsync", controller);
        Assert.DoesNotContain("TestControllerVibrationMotorAsync", app);
        Assert.DoesNotContain("RunControllerVibrationProfileWriteProbeAsync", controller);
        Assert.DoesNotContain("MsiClaw", controller);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
