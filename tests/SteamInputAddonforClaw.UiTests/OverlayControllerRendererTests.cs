using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Overlay;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class OverlayControllerRendererTests
{
    [Fact]
    public void Controller_page_wires_shared_controller_and_motion_settings()
    {
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");

        Assert.Contains("AddonQuickSettingsTabId.Controller => BuildControllerPage(rows)", shell);
        Assert.DoesNotContain("AddonQuickSettingsTabId.Controller => CreatePlaceholderPage", shell);
        Assert.Contains("var steamDetails = CreateOverlayDetailStack();", controller);
        Assert.Contains("AddControllerRow(steamDetails, rows, _steamControllerRow.Container, _steamControllerRow.Capabilities,", controller);
        Assert.Contains("AddControllerRow(steamDetails, rows, gyroscopeRow.Container, gyroscopeRow.Capabilities with", controller);
        Assert.Contains("steamSection.Children.Add(steamDetails)", controller);
        Assert.Contains("RequestSteamControllerChange, OverlayValueButtonKind.DiscreteChoice", controller);
        Assert.Contains("RequestGyroscopeChange);", controller);
        Assert.Contains("var controllerAvailable = _frontendSettingsAvailable && _controllerForSteam.Available", controller);
        Assert.Contains("_steamControllerRow?.ApplyState(controllerAvailable, 0, controllerAvailable ? 1 : 2, 1, displayValue);", controller);
        Assert.Contains("_gyroscopeRow?.ApplyState(_controllerMotionSupported && _frontendSettingsAvailable && !_gyroscopeMutationInFlight", controller);
        Assert.Contains("ControllerForSteamEditRequested?.Invoke(preference, null, ControllerForSteamMutationKind.Controller)", controller);
        Assert.Contains("ControllerForSteamEditRequested?.Invoke(null, enabled, ControllerForSteamMutationKind.Gyroscope)", controller);
        Assert.DoesNotContain("_controllerForSteamMutationInFlight", controller);
        Assert.Contains("RequestBackButtonMappingChange(isM1: true, value)", controller);
        Assert.Contains("RequestBackButtonMappingChange(isM1: false, value)", controller);
        Assert.Equal(3, CountOccurrences(controller, "OverlayValueButtonKind.DiscreteChoice"));
        Assert.Contains("AddBackButtonMappingRow(mappingDetails, rows, m1Row)", controller);
        Assert.Contains("AddBackButtonMappingRow(mappingDetails, rows, m2Row)", controller);
        Assert.Contains("CreateOverlayDetailStack()", controller);
        Assert.Contains("RegisterRowPointerSelection(row.Container)", controller);
        Assert.Contains("PreserveControllerPendingSelection(row.Capabilities,", controller);
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");
        Assert.Contains("ReconcileRowSelectionIfNeeded(AddonQuickSettingsTabId.Controller);", controller);
        Assert.Contains("ReconcileRowSelection(", navigation);
        Assert.Contains("SelectedRowContainer(tab)", navigation);
    }

    [Fact]
    public void Controller_and_gyro_overlay_mutations_have_independent_pending_display_and_settlement()
    {
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");

        Assert.Contains("private bool _steamControllerMutationInFlight", controller);
        Assert.Contains("private bool _gyroscopeMutationInFlight", controller);
        Assert.Contains("_pendingSteamControllerPreference = preference", controller);
        Assert.Contains("_pendingGyroscopeEnabled = enabled", controller);
        Assert.Contains("_pendingBackButtonMappingPreview = new(isM1, target)", controller);
        Assert.Contains("_pendingControllerLedEdit = new(_controllerLed, settings)", controller);
        Assert.Contains("? (int)pendingPreference", controller);
        Assert.Contains("? pendingGyroscope", controller);
        Assert.Contains("ControllerForSteamMutationKind? controllerForSteamMutation", controller);
        Assert.Contains("_pendingSteamControllerPreference = null", controller);
        Assert.Contains("_pendingGyroscopeEnabled = null", controller);
        Assert.Contains("controllerForSteamMutation: mutationKind", app);
        Assert.Contains("ApplyFrontendSettingsMutationResult(result, led, controllerForSteamMutation, yButtonQuickJumpMutation, simplifiedTdpControlsMutation)", app);
        Assert.Contains("ApplyFrontendSettingsMutationFailure(led, controllerForSteamMutation, yButtonQuickJumpMutation, simplifiedTdpControlsMutation)", app);

        var mutationResultStart = controller.IndexOf("internal void ApplyFrontendSettingsMutationResult(", StringComparison.Ordinal);
        var mutationFailureStart = controller.IndexOf("internal void ApplyFrontendSettingsMutationFailure(", mutationResultStart, StringComparison.Ordinal);
        var ledRequestStart = controller.IndexOf("private void RequestControllerLedEnabled(", mutationFailureStart, StringComparison.Ordinal);
        Assert.Contains("_pendingControllerLedEdit = null", controller[mutationResultStart..mutationFailureStart]);
        Assert.Contains("_pendingControllerLedEdit = null", controller[mutationFailureStart..ledRequestStart]);

        var gyroRequestStart = controller.IndexOf("private void RequestGyroscopeChange", StringComparison.Ordinal);
        var gyroRequestEnd = controller.IndexOf("private void RenderControllerForSteamRows", gyroRequestStart, StringComparison.Ordinal);
        var gyroRequest = controller[gyroRequestStart..gyroRequestEnd];
        Assert.DoesNotContain("_steamControllerMutationInFlight", gyroRequest);
        var renderStart = gyroRequestEnd;
        var renderEnd = controller.IndexOf("private static StackPanel CreateControllerSection", renderStart, StringComparison.Ordinal);
        var render = controller[renderStart..renderEnd];
        Assert.Contains("&& !_steamControllerMutationInFlight", render);
        Assert.Contains("&& !_gyroscopeMutationInFlight", render);
    }

    [Fact]
    public void Controller_frontend_settings_reconcile_selection_once_after_all_rows_are_projected()
    {
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");

        var stateStart = controller.IndexOf("internal void ApplyFrontendSettingsState(", StringComparison.Ordinal);
        var resultStart = controller.IndexOf("internal void ApplyFrontendSettingsMutationResult(", stateStart, StringComparison.Ordinal);
        var failureStart = controller.IndexOf("internal void ApplyFrontendSettingsMutationFailure(", resultStart, StringComparison.Ordinal);
        var settingsState = controller[stateStart..resultStart];
        Assert.Contains("var selectedContainer = SelectedRowContainer(activeTab);", settingsState);
        Assert.True(settingsState.IndexOf("var selectedContainer = SelectedRowContainer(activeTab);", StringComparison.Ordinal)
            < settingsState.IndexOf("_frontendSettingsSnapshot = settings;", StringComparison.Ordinal));
        Assert.True(settingsState.IndexOf("RenderControllerForSteamRows();", StringComparison.Ordinal)
            < settingsState.IndexOf("RenderControllerLedRows();", StringComparison.Ordinal));
        Assert.True(settingsState.IndexOf("RenderControllerLedRows();", StringComparison.Ordinal)
            < settingsState.IndexOf("ReconcileRowSelectionIfNeeded(activeTab, selectedContainer);", StringComparison.Ordinal));
        Assert.True(settingsState.IndexOf("RenderControllerVibrationRows();", StringComparison.Ordinal)
            < settingsState.IndexOf("ReconcileRowSelectionIfNeeded(activeTab, selectedContainer);", StringComparison.Ordinal));
        Assert.Contains("settingsAvailable && !_controllerLedVisibilityEligible && IsControllerLedDetailActive()", settingsState);
        var returnFromLedDetail = settingsState.IndexOf("TryReturnFromDetailPage();", StringComparison.Ordinal);
        var recapturedRootSelection = settingsState.IndexOf("selectedContainer = SelectedRowContainer(activeTab);",
            returnFromLedDetail, StringComparison.Ordinal);
        Assert.True(returnFromLedDetail >= 0 && recapturedRootSelection > returnFromLedDetail);
        Assert.True(recapturedRootSelection
            < settingsState.IndexOf("ReconcileRowSelectionIfNeeded(activeTab, selectedContainer);", StringComparison.Ordinal));
        Assert.True(settingsState.IndexOf("ApplyQuickSettingsYButtonQuickJumpTarget(", StringComparison.Ordinal)
            < settingsState.IndexOf("ReconcileRowSelectionIfNeeded(activeTab, selectedContainer);", StringComparison.Ordinal));
        Assert.Equal(1, CountOccurrences(settingsState, "ReconcileRowSelectionIfNeeded(activeTab, selectedContainer);"));

        var mutationResult = controller[resultStart..failureStart];
        Assert.Contains("ApplyFrontendSettingsState(response.Settings, response.ControllerLedAvailable, response.SettingsAvailable);", mutationResult);

        var ledRequestStart = controller.IndexOf("private void RequestControllerLedEnabled(", failureStart, StringComparison.Ordinal);
        var failure = controller[failureStart..ledRequestStart];
        Assert.True(failure.IndexOf("RenderControllerLedRows();", StringComparison.Ordinal)
            < failure.LastIndexOf("ReconcileRowSelectionIfNeeded(_tabState.SelectedTab);", StringComparison.Ordinal));
        Assert.True(failure.IndexOf("RenderQuickSettingsYButtonQuickJumpRow();", StringComparison.Ordinal)
            < failure.LastIndexOf("ReconcileRowSelectionIfNeeded(_tabState.SelectedTab);", StringComparison.Ordinal));
        Assert.DoesNotContain("RestoreControllerRowSelection", controller);
    }

    [Fact]
    public void Quick_settings_mutation_settlement_reenables_both_preference_rows_without_a_state_broadcast()
    {
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var currentSourceStart = clawHud.IndexOf("private void SetQuickSettingsCurrentPowerSourceMutationInFlight(", StringComparison.Ordinal);
        var simplifiedStart = clawHud.IndexOf("private void SetSimplifiedTdpControlsMutationInFlight(", currentSourceStart, StringComparison.Ordinal);
        var currentSourceSettle = clawHud[currentSourceStart..simplifiedStart];
        var simplifiedSettleEnd = clawHud.IndexOf("internal void RenderQuickSettingsCurrentPowerSourceRow()", simplifiedStart, StringComparison.Ordinal);
        var simplifiedSettle = clawHud[simplifiedStart..simplifiedSettleEnd];

        Assert.Contains("_quickSettingsMutationInFlight = inFlight", currentSourceSettle, StringComparison.Ordinal);
        Assert.Contains("_pendingQuickSettingsCurrentPowerSourceOnly = null", currentSourceSettle, StringComparison.Ordinal);
        Assert.Contains("RenderQuickSettingsCurrentPowerSourceRow();", currentSourceSettle, StringComparison.Ordinal);
        Assert.Contains("RenderSimplifiedTdpControlsRow();", currentSourceSettle, StringComparison.Ordinal);

        Assert.Contains("_quickSettingsMutationInFlight = inFlight", simplifiedSettle, StringComparison.Ordinal);
        Assert.Contains("_pendingQuickSettingsSimplifiedTdpControls = null", simplifiedSettle, StringComparison.Ordinal);
        Assert.Contains("RenderQuickSettingsCurrentPowerSourceRow();", simplifiedSettle, StringComparison.Ordinal);
        Assert.Contains("RenderSimplifiedTdpControlsRow();", simplifiedSettle, StringComparison.Ordinal);

        var currentSourceRenderer = clawHud[clawHud.IndexOf("internal void RenderQuickSettingsCurrentPowerSourceRow()", StringComparison.Ordinal)..
            clawHud.IndexOf("internal void RenderSimplifiedTdpControlsRow()", StringComparison.Ordinal)];
        var simplifiedRenderer = clawHud[clawHud.IndexOf("internal void RenderSimplifiedTdpControlsRow()", StringComparison.Ordinal)..
            clawHud.IndexOf("private StackPanel BuildClawHudPage()", StringComparison.Ordinal)];
        Assert.Contains("_quickSettingsSettingsAvailable && !_quickSettingsMutationInFlight", currentSourceRenderer, StringComparison.Ordinal);
        Assert.Contains("_quickSettingsSettingsAvailable && !_quickSettingsMutationInFlight", simplifiedRenderer, StringComparison.Ordinal);

        var resultStart = controller.IndexOf("internal void ApplyFrontendSettingsMutationResult(", StringComparison.Ordinal);
        var failureStart = controller.IndexOf("internal void ApplyFrontendSettingsMutationFailure(", resultStart, StringComparison.Ordinal);
        var mutationResult = controller[resultStart..failureStart];
        Assert.Contains("SetQuickSettingsCurrentPowerSourceMutationInFlight(false)", mutationResult, StringComparison.Ordinal);
        Assert.Contains("SetSimplifiedTdpControlsMutationInFlight(false)", mutationResult, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_and_setting_renderers_only_project_controls_and_share_selection_reconciliation()
    {
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");

        foreach (var (source, start, end) in new[]
        {
            (controller, "private void RenderControllerForSteamRows()", "private static StackPanel CreateControllerSection("),
            (controller, "private void RenderBackButtonMappingRows()", "internal void ApplyFrontendSettingsState("),
            (controller, "private void RenderControllerLedRows()", "private void RenderControllerLedColor("),
            (controller, "private void RenderControllerVibrationRows()", "internal static BackButtonMappingSettings CreateBackButtonMappingCandidate("),
            (clawHud, "internal void RenderQuickSettingsYButtonQuickJumpRow()", "private void RequestQuickSettingsCurrentPowerSourceOnly("),
            (clawHud, "internal void RenderQuickSettingsCurrentPowerSourceRow()", "private StackPanel BuildClawHudPage("),
        })
        {
            var render = source[source.IndexOf(start, StringComparison.Ordinal)..source.IndexOf(end,
                source.IndexOf(start, StringComparison.Ordinal), StringComparison.Ordinal)];
            Assert.DoesNotContain("SetRows(", render);
            Assert.DoesNotContain("ApplyRowSelectionVisual", render);
        }

        var activeReconcile = navigation[navigation.IndexOf("private void ReconcileRowSelectionIfNeeded(", StringComparison.Ordinal)..
            navigation.IndexOf("private void ReconcileRowSelection(", navigation.IndexOf("private void ReconcileRowSelectionIfNeeded(", StringComparison.Ordinal), StringComparison.Ordinal)];
        var reconcile = navigation[navigation.IndexOf("private void ReconcileRowSelection(", StringComparison.Ordinal)..
            navigation.IndexOf("private void ApplyRowSelectionVisual()", StringComparison.Ordinal)];
        Assert.Contains("rows[index].Capabilities.IsSelectable()", activeReconcile);
        Assert.Contains("preferredContainer is null || ReferenceEquals(selectedContainer, preferredContainer)", activeReconcile);
        Assert.Contains("rows.Any(row => row.Capabilities.IsSelectable())", activeReconcile);
        Assert.True(activeReconcile.IndexOf("ReferenceEquals(selectedContainer, preferredContainer)", StringComparison.Ordinal)
            < activeReconcile.IndexOf("ReconcileRowSelection(", StringComparison.Ordinal));
        Assert.Contains("preferredContainer", reconcile);
        Assert.Contains("var previousContainer = preferredContainer ?? SelectedRowContainer(tab);", reconcile);
        Assert.Contains("_rowSelection.SetRows(CapabilitiesFor(tab), resolvedIndex);", reconcile);
        Assert.Contains("if (ReferenceEquals(previousContainer, selectedContainer))", reconcile);
        Assert.Contains("BringSelectedRowIntoView();", reconcile);
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
        Assert.Contains("_pendingBackButtonMappingPreview = new(isM1, target)", controller);
        Assert.Contains("var displayMapping = ProjectBackButtonMappingPreview(_backButtonMapping, _pendingBackButtonMappingPreview)", controller);
        Assert.Contains("_m1MappingRow?.ApplyState(enabled, minimum, maximum, 1, (double)displayMapping.M1)", controller);
        Assert.Contains("_m2MappingRow?.ApplyState(enabled, minimum, maximum, 1, (double)displayMapping.M2)", controller);
        Assert.Contains("IsSelectable = () => capabilities.IsSelectable() || selectableDuringPendingMutation()", controller);
        Assert.Contains("ReconcileRowSelectionIfNeeded(AddonQuickSettingsTabId.Controller);", controller);
        Assert.Contains("HandleBackButtonMappingStateAsync", app);
        Assert.Contains("BackButtonMappingEditRequested", app);
        Assert.Contains("_backButtonMappingAvailable = state.Available", controller);
        Assert.DoesNotContain("FrontButtonMappingAvailable", controller);
        Assert.DoesNotContain("QuickSettingsPageId.Controller", controller);
        Assert.DoesNotContain("QuickSettingsRowId.M1", controller);
        Assert.DoesNotContain("QuickSettingsRowId.M2", controller);
    }

    [Theory]
    [MemberData(nameof(Xbox360Targets))]
    public void Every_xbox360_target_has_a_nonblank_shared_label_on_all_existing_surfaces(Xbox360BackButtonTarget target)
    {
        var label = BackButtonMappingLabels.GetDisplayName(target);
        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.Equal(label, BackButtonMappingUiOptions.Describe(target));
        Assert.Equal(label, OverlayWindow.FormatBackButtonTarget((double)target));
    }

    public static IEnumerable<object[]> Xbox360Targets =>
        Enum.GetValues<Xbox360BackButtonTarget>().Select(target => new object[] { target });

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
    public void Controller_mapping_rows_remain_connected_to_the_shared_selection_and_mutation_path()
    {
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var build = controller[controller.IndexOf("private FrameworkElement BuildControllerPage", StringComparison.Ordinal)..
            controller.IndexOf("private void AddBackButtonMappingRow", StringComparison.Ordinal)];

        Assert.Contains("var mappingDetails = CreateOverlayDetailStack();", build);
        Assert.Contains("mappingSection.Children.Add(mappingDetails);", build);
        Assert.Contains("AddBackButtonMappingRow(mappingDetails, rows, m1Row)", build);
        Assert.Contains("AddBackButtonMappingRow(mappingDetails, rows, m2Row)", build);
        Assert.Contains("FailureMessage = response.Succeeded", controller);
        Assert.Contains("RenderBackButtonMappingRows();", controller);
    }

    [Fact]
    public void Controller_shared_LED_and_vibration_use_existing_authority_without_test_operations()
    {
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var chromeStart = clawHud.IndexOf("private static (Border Container, Button Button) CreateDetailMenuChrome", StringComparison.Ordinal);
        var chromeEnd = clawHud.IndexOf("private void OpenSettingDetail", chromeStart, StringComparison.Ordinal);
        var menuChrome = clawHud[chromeStart..chromeEnd];
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");

        Assert.Contains("new OverlayToggleRow(", controller);
        Assert.Contains("RequestControllerLedEnabled)", controller);
        Assert.Contains("rootRows.Add(new(_controllerLedSectionCard,", controller);
        Assert.Contains("_controllerLedSectionCard = container;", controller);
        Assert.Contains("IsSelectable: () => _controllerLedAvailable && !_controllerLedMutationInFlight", controller);
        Assert.DoesNotContain("IsSelectable: () => _controllerLed.Enabled", controller);
        Assert.Contains("headerButton.Click += (_, _) => OpenControllerLedDetail();", controller);
        Assert.Contains("Activate: OpenControllerLedDetail", controller);
        Assert.Contains("RegisterRowPointerSelection(_controllerLedSectionCard);", controller);
        Assert.Contains("AutomationProperties.SetName(button, automationName)", menuChrome);
        Assert.Contains("IsTabStop = false", menuChrome);
        Assert.Contains("AllowFocusOnInteraction = false", menuChrome);
        Assert.Contains("EnterDetailPage(", controller);
        Assert.Contains("ControllerDetailId.JoystickLed", controller);
        Assert.Contains("_controllerLedDetailRows,", controller);
        Assert.Contains("if (isDetail)", controller);
        Assert.Contains("private StackPanel? _controllerLedOptionsStack", controller);
        Assert.Contains("_controllerLedOptionsStack = options;", controller);
        Assert.Contains("var details = CreateOverlayDetailStack();", controller);
        Assert.DoesNotContain("CreateOverlayDetailStack();", controller[
            controller.IndexOf("private void BuildControllerLedRows", StringComparison.Ordinal)..
            controller.IndexOf("private void OpenControllerLedDetail", StringComparison.Ordinal)]);
        Assert.Contains("details.Children.Add(options);", controller);
        Assert.Contains("var optionsVisibility = _controllerLed.Enabled ? Visibility.Visible : Visibility.Collapsed", controller);
        Assert.Contains("_controllerLedOptionsStack.Visibility != optionsVisibility", controller);
        Assert.Contains("_controllerLedOptionsStack.Visibility = optionsVisibility", controller);
        Assert.Contains("options.Children.Add(new Border", controller);
        Assert.DoesNotContain("detailRows.Add", controller[
            controller.IndexOf("var colorPreview =", StringComparison.Ordinal)..
            controller.IndexOf("_controllerLedRedRow =", StringComparison.Ordinal)]);
        Assert.Contains("RequestControllerLedBrightness(", controller);
        Assert.Contains("_controllerLedRedRow = new OverlayValueRow(", controller);
        Assert.Contains("_controllerLedGreenRow = new OverlayValueRow(", controller);
        Assert.Contains("_controllerLedBlueRow = new OverlayValueRow(", controller);
        Assert.Contains("RequestControllerLedRgb(", controller);
        var ledRenderStart = controller.IndexOf("private void RenderControllerLedRows()", StringComparison.Ordinal);
        var ledRenderEnd = controller.IndexOf("private void RenderControllerLedColor(", ledRenderStart, StringComparison.Ordinal);
        var ledRender = controller[ledRenderStart..ledRenderEnd];
        Assert.DoesNotContain("BuildControllerLedRows", ledRender);
        Assert.DoesNotContain("new OverlayToggleRow", ledRender);
        Assert.DoesNotContain("new OverlayValueRow", ledRender);
        Assert.Contains("_controllerLedRedRow.Container, _controllerLedRedRow.Capabilities", controller);
        Assert.Contains("_controllerLedGreenRow.Container, _controllerLedGreenRow.Capabilities", controller);
        Assert.Contains("_controllerLedBlueRow.Container, _controllerLedBlueRow.Capabilities", controller);
        Assert.Contains("RequestControllerLedRgb(int? red, int? green, int? blue)", controller);
        Assert.Contains("Red = (byte)(red ?? _controllerLed.Red)", controller);
        Assert.Contains("Green = (byte)(green ?? _controllerLed.Green)", controller);
        Assert.Contains("Blue = (byte)(blue ?? _controllerLed.Blue)", controller);
        Assert.Contains("var displaySettings = ProjectControllerLedPendingEdit(_controllerLed, _pendingControllerLedEdit)", controller);
        Assert.Contains("_controllerLedRedRow?.ApplyState(colorAvailable, 0, 255, 1, displaySettings.Red)", controller);
        Assert.Contains("_controllerLedGreenRow?.ApplyState(colorAvailable, 0, 255, 1, displaySettings.Green)", controller);
        Assert.Contains("_controllerLedBlueRow?.ApplyState(colorAvailable, 0, 255, 1, displaySettings.Blue)", controller);
        Assert.Contains("_controllerLedBrightnessRow?.ApplyState(available && _controllerLed.Enabled", controller);
        Assert.Contains("private void BuildControllerVibrationRows", controller);
        Assert.Contains("RequestVibrationEdit(left: (int)Math.Round(value), right: null)", controller);
        Assert.Contains("RequestVibrationEdit(left: null, right: (int)Math.Round(value)", controller);
        Assert.Contains("IsSelectable = () => _controllerVibrationSupported && leftVibrationRow.Capabilities.IsSelectable()", controller);
        Assert.Contains("IsSelectable = () => _controllerVibrationSupported && rightVibrationRow.Capabilities.IsSelectable()", controller);
        Assert.Contains("AddControllerRow(details, rows, leftVibrationRow.Container", controller);
        Assert.Contains("AddControllerRow(details, rows, rightVibrationRow.Container", controller);
        Assert.DoesNotContain("ColorPicker", controller);
        Assert.DoesNotContain("ColorChanged", controller);
        Assert.Contains("ControllerLedEditRequested?.Invoke(settings)", controller);
        Assert.Contains("await Task.Delay(300, token)", controller);
        Assert.Contains("ControllerVibrationStrengthEditRequested?.Invoke(left, right)", controller);
        Assert.Contains("SendControllerLedMutationAsync", app);
        Assert.Contains("SendControllerVibrationMutationAsync", app);
        Assert.DoesNotContain("TestControllerVibrationMotorAsync", controller);
        Assert.DoesNotContain("TestControllerVibrationMotorAsync", app);
        Assert.DoesNotContain("RunControllerVibrationProfileWriteProbeAsync", controller);
        Assert.DoesNotContain("MsiClaw", controller);
    }

    [Fact]
    public void Structural_controller_support_is_separate_from_temporary_availability()
    {
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var settingsStateStart = controller.IndexOf("internal void ApplyFrontendSettingsState(", StringComparison.Ordinal);
        var settingsStateEnd = controller.IndexOf("internal void ApplyFrontendSettingsMutationResult(", settingsStateStart, StringComparison.Ordinal);
        var settingsState = controller[settingsStateStart..settingsStateEnd];

        Assert.Contains("if (settingsAvailable)", settingsState);
        Assert.Contains("_controllerMotionSupported = settings.FeatureSupport.Motion", settingsState);
        Assert.Contains("_controllerVibrationSupported = settings.FeatureSupport.ControllerVibrationStrength", settingsState);
        Assert.Contains("_controllerLedSupported = settings.FeatureSupport.ControllerLed", settingsState);
        Assert.Contains("_controllerLedVisibilityEligible = _controllerLedSupported && controllerLedAvailable", settingsState);
        Assert.Contains("_controllerLedAvailable = settingsAvailable && _controllerLedSupported && controllerLedAvailable", settingsState);
        Assert.Contains("_controllerLedSectionCard.Visibility != menuVisibility", controller);
        Assert.Contains("_controllerLedSectionCard.Visibility = menuVisibility", controller);
        Assert.Contains("private bool IsControllerLedDetailActive()", controller);
        Assert.Contains("detail.Identity.Equals(ControllerDetailId.JoystickLed)", controller);
        Assert.Contains("_controllerVibrationSectionCard.Visibility = _controllerVibrationSupported", controller);
        Assert.Contains("IsSelectable = () => _controllerMotionSupported", controller);
        Assert.Contains("IsSelectable = () => _controllerVibrationSupported", controller);
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
