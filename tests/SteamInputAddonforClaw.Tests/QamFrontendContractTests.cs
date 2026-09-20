using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class QamFrontendContractTests
{
    [Fact]
    public void Existing_fiber_patch_and_restore_contract_is_present()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("fiber.elementType !== patch.renderer", source);
        Assert.Contains("fiber.type = patch.patchedType", source);
        Assert.Contains("fiber.alternate.type = patch.patchedType", source);
        Assert.Contains("return container.current ?? container", source);
        Assert.Contains("root._reactRootContainer?._internalRoot?.current ?? null", source);
        Assert.Contains("record.fiber.type === record.patchedType", source);
        Assert.Contains("record.alternate.type === record.patchedType", source);
        Assert.Contains("state.liveFibers = []", source);
    }

    [Fact]
    public void Outer_wrapper_is_inert_after_uninstall_and_shutdown_has_one_teardown_gate()
    {
        var frontend = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var program = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Program.cs");

        Assert.Contains("if (!state.installed) return result;", frontend);
        Assert.Contains("if (!installationSucceeded || teardownAttempted) return;", program);
        Assert.Contains("QAM target already closed; explicit uninstall was not available.", program);
        Assert.Contains("installMayExist = true", program);
        Assert.Contains("if (installMayExist) await TeardownAsync(sessionClient);", program);
        Assert.Contains("installationSucceeded = false", program);
        Assert.Contains("teardownAttempted = false", program);
        Assert.DoesNotContain("QamHost stop requested before installation completed.", program[..program.IndexOf("installationSucceeded = true", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Non_managed_connection_loss_exits_without_reconnect_recovery()
    {
        var program = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Program.cs");

        var lossIndex = program.IndexOf("log.Warn(\"CDP connection lost.\")", StringComparison.Ordinal);
        Assert.True(lossIndex >= 0);
        var lossPath = program[lossIndex..];

        Assert.Contains("if (!managed)", lossPath);
        Assert.Contains("stopRequested = true", lossPath);
        Assert.Contains("reconnect recovery is disabled", lossPath);
    }

    [Fact]
    public void Deterministic_native_install_failure_waits_for_document_reload_without_terminating_host()
    {
        var program = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Program.cs");

        Assert.Contains("async Task InstallForCurrentDocumentAsync(SteamGamepadUiCdpClient client)", program);
        Assert.Contains("waiting for document replacement", program);
        Assert.Contains("await InstallForCurrentDocumentAsync(currentClient);", program);
        var wrapperStart = program.IndexOf("async Task InstallForCurrentDocumentAsync", StringComparison.Ordinal);
        var wrapper = program[wrapperStart..program.IndexOf("async Task TeardownAsync", wrapperStart, StringComparison.Ordinal)];
        Assert.DoesNotContain("stopRequested = true", wrapper);
    }

    [Fact]
    public void Qam_cdp_bridge_serializes_sends_and_drops_old_document_responses()
    {
        var cdp = ReadSource("src", "SteamInputAddonforClaw.QamHost", "SteamGamepadUiCdpClient.cs");
        var program = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Program.cs");

        Assert.Contains("private readonly SemaphoreSlim _sendGate", cdp);
        Assert.Contains("await _sendGate.WaitAsync", cdp);
        Assert.Contains("_sendGate.Release()", cdp);
        Assert.Contains("long documentGeneration = 0", program);
        Assert.Contains("admittedGeneration != Volatile.Read(ref documentGeneration)", program);
        Assert.Contains("Interlocked.Increment(ref documentGeneration)", program);
    }

    [Fact]
    public void Qam_read_only_target_diagnostic_does_not_register_the_addon_bridge()
    {
        var cdp = ReadSource("src", "SteamInputAddonforClaw.QamHost", "SteamGamepadUiCdpClient.cs");
        var start = cdp.IndexOf("internal async Task ConnectReadOnlyAsync", StringComparison.Ordinal);
        var end = cdp.IndexOf("private async Task ConnectSocketAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.DoesNotContain("Runtime.enable", cdp[start..end]);
        Assert.DoesNotContain("Page.enable", cdp[start..end]);
        Assert.DoesNotContain("Runtime.addBinding", cdp[start..end]);
    }

    [Fact]
    public void Qam_first_tab_intent_uses_a_dedicated_current_document_notification()
    {
        var bridge = ReadSource("src", "SteamInputAddonforClaw.QamHost", "QamFrontendBridge.cs");
        var program = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Program.cs");

        Assert.Contains("SelectAddonOnNextQuickAccessOpenRequested", bridge);
        Assert.Contains("SelectAddonOnNextQuickAccessOpenRequested", program);
        Assert.Contains("select-addon-on-next-open", program);
        Assert.Contains("DeliverSelectAddonOnNextQuickAccessOpenAsync", program);
        Assert.Contains("admittedGeneration != Volatile.Read(ref documentGeneration)", program);
        Assert.Contains("AcknowledgeQamSelectAddonOnNextOpenPreparedAsync", program);
        var delivery = program[program.IndexOf("DeliverSelectAddonOnNextQuickAccessOpenAsync", StringComparison.Ordinal)..];
        var evaluateIndex = delivery.IndexOf("EvaluateAsync", StringComparison.Ordinal);
        var postEvaluationGenerationCheck = delivery.IndexOf("admittedGeneration != Volatile.Read(ref documentGeneration)", evaluateIndex + 1, StringComparison.Ordinal);
        var acknowledgementIndex = delivery.IndexOf("AcknowledgeQamSelectAddonOnNextOpenPreparedAsync", StringComparison.Ordinal);
        Assert.True(evaluateIndex >= 0 && postEvaluationGenerationCheck > evaluateIndex && postEvaluationGenerationCheck < acknowledgementIndex);
        Assert.DoesNotContain("state-invalidated')", program[program.IndexOf("DeliverSelectAddonOnNextQuickAccessOpenAsync", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void QamHost_target_inventory_is_read_only_and_event_driven()
    {
        var program = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Program.cs");

        Assert.Contains("CdpTargetSnapshotFormatter.Format(\"initial-acquisition\", targets)", program);
        Assert.Contains("CdpTargetSnapshotFormatter.Format(reason, snapshotTargets)", program);
        Assert.Contains("LogTargetSnapshotAsync(\"select-addon-on-next-open\"", program);
        Assert.Contains("LogQuickAccessGeometrySnapshotsAsync(\"select-addon-on-next-open\"", program);
        Assert.Contains("QuickAccessTargetSelector.SelectQuickAccessTargets", program);
        Assert.Contains("ConnectReadOnlyAsync", program);
        Assert.Contains("sessionDiagnosticsCts", program);
        Assert.Contains("sessionClient.ListTargetsAsync(token)", program);
        Assert.DoesNotContain("ConnectAsync(snapshot", program);
    }

    [Fact]
    public void Qam_bridge_path_is_only_the_generic_quick_settings_seam_for_device_and_profile()
    {
        var bridge = ReadSource("src", "SteamInputAddonforClaw.QamHost", "QamFrontendBridge.cs");

        // SF-V2-05/08 plus shared-surface PR1: the bridge exposes only the closed shell read seam
        // and the Device/Profile generic page/mutation allow-list.
        Assert.Contains("\"captureQuickSettingsShell\" => await _client.CaptureAddonQuickSettingsShellAsync(token),", bridge);
        Assert.Contains("\"captureQuickSettingsShortcut\" => AddonQuickSettingsShortcutContract.Create(),", bridge);
        Assert.Contains("\"captureQuickSettingsPage\" => await CaptureQuickSettingsPageAsync(root, token),", bridge);
        Assert.Contains("\"mutateQuickSetting\" => await MutateQuickSettingAsync(root, token),", bridge);
        Assert.Contains("case QuickSettingsPageId.Device:", bridge);
        Assert.Contains("case QuickSettingsPageId.Profile:", bridge);
        Assert.DoesNotContain("EnsureDeviceMutationAdmittedAsync", bridge);

        // The transitional feature-specific Device/Profile bridge operations are gone now that
        // qam.js renders/mutates both pages only through the shared page.
        Assert.DoesNotContain("\"captureDeviceQuickSettings\"", bridge);
        Assert.DoesNotContain("\"captureCpuBoost\"", bridge);
        Assert.DoesNotContain("\"captureTdp\"", bridge);
        Assert.DoesNotContain("\"capturePowerMode\"", bridge);
        Assert.DoesNotContain("\"setDeviceCpuBoostEnabled\"", bridge);
        Assert.DoesNotContain("\"setDeviceCpuBoostAc\"", bridge);
        Assert.DoesNotContain("\"setDeviceCpuBoostDc\"", bridge);
        Assert.DoesNotContain("\"setDeviceTdpEnabled\"", bridge);
        Assert.DoesNotContain("\"setDeviceTdp\"", bridge);
        Assert.DoesNotContain("\"setDevicePowerModeEnabled\"", bridge);
        Assert.DoesNotContain("\"setDevicePowerModeAc\"", bridge);
        Assert.DoesNotContain("\"setDevicePowerModeDc\"", bridge);
        Assert.DoesNotContain("DecodeTdpConfiguration", bridge);
        Assert.DoesNotContain("\"captureActiveGameProfile\"", bridge);
        Assert.DoesNotContain("\"setActiveGameProfileEnabled\"", bridge);
        Assert.DoesNotContain("\"setActiveGameCpuBoostEnabled\"", bridge);
        Assert.DoesNotContain("\"setActiveGameCpuBoostAc\"", bridge);
        Assert.DoesNotContain("\"setActiveGameCpuBoostDc\"", bridge);
        Assert.DoesNotContain("\"setActiveGameTdp\"", bridge);
        Assert.DoesNotContain("\"setActiveGameTdpEnabled\"", bridge);
        Assert.DoesNotContain("\"setActiveGamePowerModeEnabled\"", bridge);
        Assert.DoesNotContain("\"setActiveGamePowerModeAc\"", bridge);
        Assert.DoesNotContain("\"setActiveGamePowerModeDc\"", bridge);
        Assert.DoesNotContain("\"setActiveGameFpsLimitEnabled\"", bridge);
        Assert.DoesNotContain("\"setActiveGameFpsLimitAc\"", bridge);
        Assert.DoesNotContain("\"setActiveGameFpsLimitDc\"", bridge);
        Assert.DoesNotContain("ActiveMutationAsync", bridge);
        Assert.DoesNotContain("DecodePowerMode", bridge);
    }

    [Fact]
    public void Qam_uninstall_retires_pending_bridge_consumers_without_resetting_ids()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("function retireBridgeConsumers()", source);
        Assert.Contains("pending.reject(new Error(\"QAM bridge stopped\"))", source);
        Assert.Contains("state.bridgePending?.clear()", source);
        Assert.Contains("state.stateInvalidationSubscribers?.clear()", source);
        Assert.DoesNotContain("state.bridgeNextId = 0", source);
    }

    [Fact]
    public void Qam_single_addon_tab_descriptor_is_generation_scoped_and_cleaned_up()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js").ReplaceLineEndings("\n");

        Assert.Contains("const ADDON_TAB_KEY = \"steam-input-addon\";", source);
        Assert.Contains("const LEGACY_ADDON_DEVICE_TAB_KEY = \"steam-input-addon-device\";", source);
        Assert.Contains("const LEGACY_ADDON_PROFILE_TAB_KEY = \"steam-input-addon-profile\";", source);
        Assert.Contains("Historical descriptor cleanup identities only.", source);
        Assert.Contains("they are not current product tab identities.", source);
        Assert.Contains("state.addonTabDescriptor", source);
        Assert.Contains("function buildAddonTab(React, native)", source);
        Assert.Contains("[TAB_MARKER]: ADDON_TAB_KEY", source);
        Assert.Contains("panel: React.createElement(AddonQuickSettingsPanel)", source);
        Assert.DoesNotContain("state.addonTabDescriptors", source);
        var installStart = source.IndexOf("function install()", StringComparison.Ordinal);
        var installReset = source.IndexOf("state.addonTabDescriptor = null;", installStart, StringComparison.Ordinal);
        Assert.True(installStart >= 0);
        Assert.True(installReset > installStart);
        Assert.True(installReset < source.IndexOf("state.diagnostics = {};", installReset, StringComparison.Ordinal));

        var teardownStart = source.IndexOf("Object.assign(state, {\n      patches: null,", StringComparison.Ordinal);
        Assert.True(teardownStart >= 0);
        var teardown = source[teardownStart..source.IndexOf("    });", teardownStart, StringComparison.Ordinal)];
        Assert.Contains("addonTabDescriptor: null,", teardown);
        Assert.Contains("selectAddonOnNextOpenRequested: false,", teardown);

        var uninstallStart = source.IndexOf("function uninstall()", StringComparison.Ordinal);
        var uninstall = source[uninstallStart..source.IndexOf("    state.installed = false;", uninstallStart, StringComparison.Ordinal)];
        Assert.Contains("state.addonTabDescriptor = null;", uninstall);
        Assert.DoesNotContain("state.addonTabDescriptors", uninstall);
    }

    [Fact]
    public void Qam_single_tab_insertion_preserves_steam_tabs_and_removes_historical_addon_tabs()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var insertion = source[source.IndexOf("function ensureAddonTabs", StringComparison.Ordinal)..source.IndexOf("function preservePatchedFunctionShape", StringComparison.Ordinal)];

        Assert.Contains("function addonTabKey(tab)", source);
        Assert.Contains("if (marker === true) return \"legacy\";", source);
        Assert.Contains("if (marker === ADDON_TAB_KEY) return ADDON_TAB_KEY;", source);
        Assert.Contains("if (marker === LEGACY_ADDON_DEVICE_TAB_KEY) return LEGACY_ADDON_DEVICE_TAB_KEY;", source);
        Assert.Contains("if (marker === LEGACY_ADDON_PROFILE_TAB_KEY) return LEGACY_ADDON_PROFILE_TAB_KEY;", source);
        Assert.Contains("const steamTabs = tabs.filter(tab => !addonTabKey(tab));", insertion);
        Assert.Contains("const desired = [descriptor];", insertion);
        Assert.Contains("tabs.splice(0, tabs.length, ...nextTabs);", insertion);
        Assert.Contains("RemovedOwned=${removedCount}", insertion);
        Assert.DoesNotContain("state.qamSelectionContext", source);
    }

    [Fact]
    public void Qam_initial_tab_selection_uses_native_menu_authority_and_fails_open_when_unavailable()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var selectionStart = source.IndexOf("function resolveNativeQamMenuAuthority", StringComparison.Ordinal);
        var selectionEnd = source.IndexOf("function preservePatchedFunctionShape", selectionStart, StringComparison.Ordinal);
        Assert.True(selectionStart >= 0 && selectionEnd > selectionStart);
        var selection = source[selectionStart..selectionEnd];

        Assert.Contains("function resolveQamWindowAuthority()", source);
        Assert.Contains("windowStore.GetOverlayInstance(steamAppId, 0)", source);
        Assert.Contains("lookup: \"method-unavailable\"", source);
        Assert.DoesNotContain("GetOverlayInstanceWithFallback", source);
        Assert.Contains("function resolveNativeQamMenuAuthority()", selection);
        Assert.Contains("const resolved = resolveQamWindowAuthority();", selection);
        Assert.Contains("const menuStore = resolved.instance?.MenuStore;", selection);
        Assert.Contains("typeof menuStore.OpenQuickAccessMenu !== \"function\"", selection);
        Assert.Contains("!Object.prototype.hasOwnProperty.call(menuStore, \"m_eOpenSideMenu\")", selection);
        Assert.Contains("const QUICK_ACCESS_SIDE_MENU_ID = 2;", source);
        Assert.Contains("isQuickAccessOpen: () => menuStore.m_eOpenSideMenu === QUICK_ACCESS_SIDE_MENU_ID", selection);
        Assert.Contains("menuStore.OpenQuickAccessMenu(ADDON_TAB_KEY, false);", selection);
        Assert.DoesNotContain("const candidates =", selection);
        Assert.DoesNotContain("activeTab", selection);
        Assert.Contains("function requestAddonSelectionOnNextQuickAccessOpen()", selection);
        Assert.Contains("function tryConsumeAddonSelectionRequest()", selection);
        Assert.Contains("if (!state.selectAddonOnNextOpenRequested || !state.addonTabDescriptor) return;", selection);
        Assert.Contains("authority.selectAddon();", selection);
        Assert.DoesNotContain("captureStatus", selection);
        Assert.DoesNotContain("if (!state.installed || !state.qamSurfaceActive) return;", selection);
        Assert.Contains("QAM initial Addon tab selection unavailable; tabs remain usable.", selection);
        Assert.DoesNotContain("initialTabSelectionOwners", selection);
        Assert.DoesNotContain("owner?._owner", selection);
        Assert.DoesNotContain("document.querySelector", selection);
        Assert.DoesNotContain(".click(", selection);
        Assert.DoesNotContain("focus()", selection);
        Assert.DoesNotContain("setInterval", selection);
        Assert.DoesNotContain("MutationObserver", selection);
    }

    [Fact]
    public void Qam_open_selection_does_not_use_renderer_visibility()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        Assert.DoesNotContain("qamSurfaceActive", source);
        Assert.DoesNotContain("qamInitialSelectionRequested", source);
        Assert.DoesNotContain("updateQamSurfaceVisibility", source);

        var patchedTypeStart = source.IndexOf("const patchedType = preservePatchedFunctionShape", StringComparison.Ordinal);
        var patchedTypeEnd = source.IndexOf("patch.patchedType = patchedType;", patchedTypeStart, StringComparison.Ordinal);
        Assert.True(patchedTypeStart >= 0 && patchedTypeEnd > patchedTypeStart);
        var patchedType = source[patchedTypeStart..patchedTypeEnd];
        Assert.DoesNotContain("args[0]?.visible", patchedType);
        Assert.DoesNotContain("onFocusNavActivated", patchedType);
        Assert.DoesNotContain("onFocusNavDeactivated", patchedType);
    }

    [Fact]
    public void Qam_open_selection_is_intent_driven_and_consumed_after_descriptor_insertion()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var insertionStart = source.IndexOf("function ensureAddonTabs", StringComparison.Ordinal);
        var insertionEnd = source.IndexOf("function preservePatchedFunctionShape", insertionStart, StringComparison.Ordinal);
        var insertion = source[insertionStart..insertionEnd];
        var triggerIndex = insertion.IndexOf("tryConsumeAddonSelectionRequest();", StringComparison.Ordinal);
        Assert.True(triggerIndex >= 0);
        Assert.DoesNotContain("state.qamSelectionContext", insertion);
        var descriptorIndex = insertion.IndexOf("const descriptor = buildAddonTab(React, native);", StringComparison.Ordinal);
        Assert.True(descriptorIndex >= 0 && descriptorIndex < triggerIndex);

        var notificationStart = source.IndexOf("function receiveBridgeNotification", StringComparison.Ordinal);
        var notificationEnd = source.IndexOf("function retireBridgeConsumers", notificationStart, StringComparison.Ordinal);
        var notification = source[notificationStart..notificationEnd];
        Assert.Contains("select-addon-on-next-open", notification);
        Assert.Contains("requestAddonSelectionOnNextQuickAccessOpen", notification);
        Assert.Contains("if (!state.selectAddonOnNextOpenRequested || !state.addonTabDescriptor)", source);
        Assert.DoesNotContain("state.initialTabSelectionOwners", source);
        Assert.DoesNotContain("patchQamLifecycle", source);
        Assert.DoesNotContain("setTimeout", source[source.IndexOf("function requestAddonSelectionOnNextQuickAccessOpen", StringComparison.Ordinal)..source.IndexOf("function ensureAddonTabs", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Qam_width_class_discovery_uses_unique_semantic_quick_access_keys_and_fails_open()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var start = source.IndexOf("function isQuickAccessMenuClassModule", StringComparison.Ordinal);
        var end = source.IndexOf("function findNativeQamComponents", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var discovery = source[start..end];

        Assert.Contains("function isQuickAccessMenuClassModule(candidate)", discovery);
        Assert.Contains("isQuickAccessMenuClassModule(moduleExports)", discovery);
        Assert.Contains("isQuickAccessMenuClassModule(candidate)", discovery);
        Assert.True(discovery.IndexOf("isQuickAccessMenuClassModule(moduleExports)", StringComparison.Ordinal) < discovery.IndexOf("Object.values(moduleExports)", StringComparison.Ordinal));
        Assert.Contains("candidate.Title", discovery);
        Assert.Contains("candidate.QuickAccessMenu", discovery);
        Assert.Contains("candidate.BatteryDetailsLabels", discovery);
        Assert.Contains("candidate.PanelOuterNav", discovery);
        Assert.Contains("unique[0].TabGroupPanel", discovery);
        Assert.Contains("TabGroupPanel: tabGroupPanel", discovery);
        Assert.Contains("unique.length !== 1", discovery);
        Assert.Contains("return null", discovery);
        Assert.DoesNotContain("quickaccessmenu_PanelOuterNav_2BB6u", source);
    }

    [Fact]
    public void Qam_width_selection_uses_the_rendered_native_active_tab_without_patching_menu_store()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var widthStart = source.IndexOf("function findQamActiveTabOwner", StringComparison.Ordinal);
        var widthEnd = source.IndexOf("function findTabsPropOwner", widthStart, StringComparison.Ordinal);
        Assert.True(widthStart >= 0 && widthEnd > widthStart);
        var width = source[widthStart..widthEnd];

        Assert.Contains("state.qamWidthClassNames?.PanelOuterNav", width);
        Assert.Contains("findReactNode(panelOuter", width);
        Assert.Contains("Array.isArray(props.tabs)", width);
        Assert.Contains("Object.prototype.hasOwnProperty.call(props, \"activeTab\")", width);
        Assert.Contains("const activeTab = tabOwner?.props?.activeTab", width);
        Assert.Contains("activeTab === ADDON_TAB_KEY", width);
        Assert.Contains("function applyQamPanelOuterWidth(result, activeTab)", width);
        Assert.Contains("activeTab !== ADDON_TAB_KEY", width);
        Assert.Contains("width: `${ADDON_QAM_WIDTH_PX}px`", width);
        Assert.Contains("maxWidth: `${ADDON_QAM_WIDTH_PX}px`", width);
        Assert.Contains("target.props.style === record.appliedStyle", width);
        Assert.Contains("restoreQamPanelOuterWidth();", width);
        Assert.Contains("String(activeTab)", width);
        Assert.DoesNotContain("typeof activeTab !== \"string\"", width);
        Assert.Contains("QAM active top-level tab was not found", width);
        Assert.DoesNotContain("addonQamWidthActive", width);

        Assert.DoesNotContain("function installAddonQamWidthSelectionHook", source);
        Assert.DoesNotContain("function uninstallAddonQamWidthSelectionHook", source);
        Assert.DoesNotContain("menuStore.OpenQuickAccessMenu =", source);

        var insertionStart = source.IndexOf("function ensureAddonTabs", StringComparison.Ordinal);
        var insertionEnd = source.IndexOf("function preservePatchedFunctionShape", insertionStart, StringComparison.Ordinal);
        var insertion = source[insertionStart..insertionEnd];
        Assert.DoesNotContain("installAddonQamWidthSelectionHook", insertion);
        Assert.Contains("tryConsumeAddonSelectionRequest();", insertion);

        var nestedStart = source.IndexOf("function patchedTabsProducer", StringComparison.Ordinal);
        var nestedEnd = source.IndexOf("}, originalTarget);", nestedStart, StringComparison.Ordinal);
        Assert.True(nestedStart >= 0 && nestedEnd > nestedStart);
        var nested = source[nestedStart..nestedEnd];
        Assert.Contains("patchQamTabGroupOwner(owner);", nested);
        Assert.DoesNotContain("applyAddonQamContentWidth(result);", nested);

        var ownerStart = source.IndexOf("function patchQamTabGroupOwner", StringComparison.Ordinal);
        var producerStart = source.IndexOf("function patchQamTabGroupProducer", StringComparison.Ordinal);
        Assert.True(producerStart >= 0 && ownerStart > producerStart);
        var producer = source[producerStart..ownerStart];
        Assert.Contains("args[0]?.tab?.key !== ADDON_TAB_KEY", producer);
        Assert.Contains("function findQamAddonTabPanelProducer(result)", source);
        Assert.Contains("Object.prototype.hasOwnProperty.call(node.props, \"bActive\")", source);
        Assert.Contains("function patchQamTabPanelProducer(node)", source);
        Assert.Contains("return applyAddonQamTabGroupPanelWidth(result);", source);
        Assert.Contains("patchQamTabPanelProducer(panelProducerSearch.node)", producer);
        Assert.DoesNotContain("applyAddonQamTabGroupPanelWidth(result)", producer);
        Assert.Contains("rebuildComponentType(node.type, resolved, patchedTarget)", source);
        Assert.Contains("function patchQamTabGroupOwner(node)", source);
        Assert.Contains("findQamAddonTabProducer(result)", source);
        Assert.Contains("patchQamTabGroupProducer(tabProducerSearch.node)", source);
    }

    [Fact]
    public void Qam_fe_return_tree_diagnostic_is_bounded_one_shot_and_read_only()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var start = source.IndexOf("function captureQamFeReturnTreeDiagnostic", StringComparison.Ordinal);
        var end = source.IndexOf("function patchQamTabPanelProducer", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var diagnostic = source[start..end];

        Assert.Contains("QAM_FE_DIAGNOSTIC_NODE_BUDGET", source);
        Assert.Contains("QAM_FE_DIAGNOSTIC_DEPTH_BUDGET", source);
        Assert.Contains("state.diagnostics?.qamFeReturnTree", diagnostic);
        Assert.Contains("propsKeys", diagnostic);
        Assert.Contains("tabKey", diagnostic);
        Assert.Contains("bActivePresent", diagnostic);
        Assert.Contains("describeQamDiagnosticChild", diagnostic);
        Assert.Contains("kind: \"function\"", source);
        Assert.Contains("kind: \"array\"", diagnostic);
        Assert.Contains("BudgetExhausted", diagnostic);
        Assert.Contains("logOnce(\"qamFeReturnTree\"", diagnostic);
        Assert.DoesNotContain("document", diagnostic);
        Assert.DoesNotContain("MutationObserver", diagnostic);
        Assert.DoesNotContain("setInterval", diagnostic);
        Assert.DoesNotContain("setTimeout", diagnostic);
        Assert.DoesNotContain(".style =", diagnostic);
        Assert.DoesNotContain(".style.", diagnostic);

        var producerStart = source.IndexOf("function patchQamTabGroupProducer", StringComparison.Ordinal);
        var ownerStart = source.IndexOf("function patchQamTabGroupOwner", producerStart, StringComparison.Ordinal);
        Assert.True(producerStart >= 0 && ownerStart > producerStart);
        var producer = source[producerStart..ownerStart];
        Assert.Contains("captureQamFeReturnTreeDiagnostic(result);", producer);
        Assert.Contains("args[0]?.tab?.key !== ADDON_TAB_KEY", producer);
    }

    [Fact]
    public void Qam_authority_diagnostic_is_read_only_and_compares_main_with_overlay_instance()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var diagnosticStart = source.IndexOf("function qamDiagnosticObjectId", StringComparison.Ordinal);
        var authorityStart = source.IndexOf("function captureQamAuthorityState", diagnosticStart, StringComparison.Ordinal);
        var authorityEnd = source.IndexOf("function findTabsPropOwner", authorityStart, StringComparison.Ordinal);
        Assert.True(diagnosticStart >= 0 && authorityStart > diagnosticStart && authorityEnd > authorityStart);
        var diagnostic = source[diagnosticStart..authorityEnd];

        Assert.Contains("function captureQamAuthorityState(activeTab = null)", diagnostic);
        Assert.Contains("function captureQamAuthorityDiagnostic(reason, activeTab = null)", diagnostic);
        Assert.Contains("const capturedState = captureQamAuthorityState(activeTab);", diagnostic);
        Assert.Contains("void enrichAndLogQamAuthorityDiagnostic(reason, capturedState);", diagnostic);
        var stateStart = diagnostic.IndexOf("function captureQamAuthorityState", StringComparison.Ordinal);
        var stateEnd = diagnostic.IndexOf("// Runtime AppId remains", stateStart, StringComparison.Ordinal);
        var state = diagnostic[stateStart..stateEnd];
        var enrichStart = diagnostic.IndexOf("async function enrichAndLogQamAuthorityDiagnostic", stateEnd, StringComparison.Ordinal);
        var enrichEnd = diagnostic.IndexOf("// Read-only authority diagnostic", enrichStart, StringComparison.Ordinal);
        var enrich = diagnostic[enrichStart..enrichEnd];
        Assert.Contains("MainRunningAppID", diagnostic);
        Assert.Contains("resolveQamWindowAuthority", state);
        Assert.Contains("describeQamWindowInstance(resolved.instance)", state);
        Assert.DoesNotContain("GetOverlayInstanceWithFallback", enrich);
        Assert.DoesNotContain("describeQamWindowInstance(overlay)", enrich);
        Assert.Contains("GetOverlayInstance(steamAppId, 0)", diagnostic);
        Assert.Contains("GetOpenSideMenu", diagnostic);
        Assert.Contains("GetQuickAccessTab", diagnostic);
        Assert.Contains("captureStatus", diagnostic);
        Assert.Contains("MainWindowInstance", diagnostic);
        Assert.Contains("OverlayLookup", diagnostic);
        Assert.Contains("logStateChange(\"qamAuthority\"", diagnostic);
        Assert.DoesNotContain("OpenQuickAccessMenu(ADDON_TAB_KEY", diagnostic);
        Assert.DoesNotContain("document", diagnostic);
        Assert.DoesNotContain(".click(", diagnostic);
        Assert.DoesNotContain("focus()", diagnostic);
        Assert.DoesNotContain("MutationObserver", diagnostic);
        Assert.DoesNotContain("setInterval", diagnostic);
    }

    [Fact]
    public void Qam_authority_diagnostic_is_triggered_at_selection_and_render_boundaries()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("captureQamAuthorityDiagnostic(\"selection-request\")", source);
        Assert.Contains("captureQamAuthorityDiagnostic(\"selection-before\", ADDON_TAB_KEY)", source);
        Assert.Contains("captureQamAuthorityDiagnostic(\"selection-after\", ADDON_TAB_KEY)", source);
        Assert.Contains("captureQamAuthorityDiagnostic(\"render-active-tab\", activeTab)", source);

        var selectionStart = source.IndexOf("function tryConsumeAddonSelectionRequest", StringComparison.Ordinal);
        var selectionEnd = source.IndexOf("function ensureAddonTabs", selectionStart, StringComparison.Ordinal);
        var selection = source[selectionStart..selectionEnd];
        var beforeIndex = selection.IndexOf("captureQamAuthorityDiagnostic(\"selection-before\", ADDON_TAB_KEY)", StringComparison.Ordinal);
        var selectIndex = selection.IndexOf("authority.selectAddon();", StringComparison.Ordinal);
        var afterIndex = selection.IndexOf("captureQamAuthorityDiagnostic(\"selection-after\", ADDON_TAB_KEY)", StringComparison.Ordinal);
        Assert.True(beforeIndex >= 0 && beforeIndex < selectIndex && selectIndex < afterIndex);

        var diagnosticStart = source.IndexOf("function captureQamAuthorityDiagnostic", StringComparison.Ordinal);
        var diagnosticEnd = source.IndexOf("function findTabsPropOwner", diagnosticStart, StringComparison.Ordinal);
        var diagnostic = source[diagnosticStart..diagnosticEnd];
        Assert.Contains("const capturedState = captureQamAuthorityState(activeTab);", diagnostic);
        Assert.DoesNotContain("await", diagnostic[..diagnostic.IndexOf("void enrichAndLogQamAuthorityDiagnostic", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Qam_width_patch_targets_only_addon_tab_group_panel_return_element()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        Assert.Contains("const ADDON_QAM_WIDTH_PX = 400;", source);
        var start = source.IndexOf("function applyAddonQamTabGroupPanelWidth", StringComparison.Ordinal);
        var end = source.IndexOf("function findTabsPropOwner", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var width = source[start..end];

        Assert.Contains("findReactNode(result, node => hasExactClass(node, tabGroupPanelClass))", width);
        Assert.Contains("QAM Addon TabGroupPanel element was not found in the producer result", width);
        Assert.Contains("width: `${ADDON_QAM_WIDTH_PX}px`", width);
        Assert.Contains("maxWidth: `${ADDON_QAM_WIDTH_PX}px`", width);
        Assert.DoesNotContain("ADDON_QAM_CONTENT_ID", source);
        Assert.DoesNotContain("props?.id ===", width);
        Assert.DoesNotContain("height:", width);
        Assert.DoesNotContain("margin:", width);
        Assert.DoesNotContain("MutationObserver", width);
        Assert.DoesNotContain("setInterval", width);
    }

    [Fact]
    public void Qam_width_geometry_diagnostic_is_read_only_and_active_tab_scoped()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var start = source.IndexOf("function describeQamGeometry", StringComparison.Ordinal);
        var end = source.IndexOf("function restoreQamPanelOuterWidth", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var diagnostic = source[start..end];

        Assert.Contains("getBoundingClientRect", diagnostic);
        Assert.Contains("getComputedStyle", diagnostic);
        Assert.Contains("getElementsByClassName", diagnostic);
        Assert.Contains("parentElement", diagnostic);
        Assert.Contains("depth < 6", diagnostic);
        Assert.Contains("requestAnimationFrame", diagnostic);
        Assert.Contains("cancelAnimationFrame", diagnostic);
        Assert.Contains("qamGeometryScheduledSignature === signature", diagnostic);
        Assert.Contains("logStateChange(\"qamGeometry\", String(activeTab)", diagnostic);
        Assert.Contains("Realm: \"SharedJSContext\"", diagnostic);
        Assert.Contains("QAM SharedJSContext geometry fallback", diagnostic);
        Assert.Contains("Functional QAM discovery remains React-based", diagnostic);
        Assert.DoesNotContain(".style =", diagnostic);
        Assert.DoesNotContain(".style.", diagnostic);
        Assert.DoesNotContain("classList", diagnostic);
        Assert.DoesNotContain("setProperty", diagnostic);
        Assert.DoesNotContain("appendChild", diagnostic);
        Assert.DoesNotContain("removeChild", diagnostic);
        Assert.DoesNotContain("MutationObserver", diagnostic);
        Assert.DoesNotContain("setInterval", diagnostic);
        Assert.DoesNotContain("setTimeout", diagnostic);
        Assert.Contains("function getQamGeometryClassNames()", source);
        Assert.Contains("__getQamGeometryClassNames", source);
    }

    [Fact]
    public void Qam_width_cleanup_is_conservative_and_generation_local()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var installStart = source.IndexOf("function install()", StringComparison.Ordinal);
        var install = source[installStart..source.IndexOf("const webpackRequire", installStart, StringComparison.Ordinal)];
        Assert.Contains("cancelQamGeometryReadback();", install);
        Assert.Contains("state.qamWidthClassNames = null", install);
        Assert.Contains("state.qamWidthPatches = new Map()", install);
        Assert.Contains("state.qamOuterStyleRecords = new WeakMap()", install);
        Assert.Contains("state.qamOuterPatchedTarget = null", install);

        Assert.DoesNotContain("state.qamWidthSelectionPatch", source);
        Assert.DoesNotContain("state.addonQamWidthActive", source);
        Assert.Contains("state.qamWidthClassNames = null", source);
        Assert.Contains("function restoreQamWidthPatches()", source);
        Assert.Contains("function restoreQamPanelOuterWidth()", source);
        Assert.Contains("qamOuterStyleRecords: new WeakMap()", source);
        Assert.Contains("qamWidthPatches: null", source);
        Assert.Contains("cancelQamGeometryReadback();", source[source.IndexOf("function uninstall()", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void Qam_invalidation_subscribers_are_shared_but_each_panel_owns_its_refresh_and_pending_context()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        Assert.Contains("state.stateInvalidationSubscribers ??= new Set();", source);
        Assert.Contains("for (const callback of [...(state.stateInvalidationSubscribers ?? [])])", source);
        Assert.Contains("return () => state.stateInvalidationSubscribers?.delete(callback);", source);
        Assert.Contains("if (pageId === QS_PAGE_PROFILE && previousContext && !sameQuickSettingsContext(previousContext, nextContext))", source);
        Assert.Contains("cancelQuickSettingsPendingForContext(previousContext);", source);
        Assert.Contains("state.stateInvalidationSubscribers?.clear();", source);
        Assert.Contains("if (pageId === QS_PAGE_DEVICE)", source);
        Assert.Contains("nextContext = { pageId: QS_PAGE_DEVICE, appId: null };", source);
    }

    [Fact]
    public void Qam_immediate_toggle_retires_same_section_same_context_pending_work_generically()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var index = source.IndexOf("const commitQuickSettingsImmediate = async (page, section, row, nextValue) =>", StringComparison.Ordinal);
        Assert.True(index >= 0);
        var path = source[index..source.IndexOf("const scheduleQuickSettingsCommit", index, StringComparison.Ordinal)];

        // Same-section cancellation is derived from the shared page/AppId/section identity, not
        // device-*/profile-* strings.
        Assert.Contains("cancelQamSliderCommits((key, pending) => pending?.pageId === page.pageId && (pending?.appId ?? null) === (page.appId ?? null) && pending?.sectionId === section.sectionId);", path);
        Assert.DoesNotContain("device-cpu-", path);
        Assert.DoesNotContain("profile-cpu-", path);
        Assert.Contains("if (!state.installed || !requireQuickSettingsRowMutation(row)) return;", path);
        Assert.Contains("request(\"mutateQuickSetting\"", path);
        Assert.Contains("editedRowId: row.rowId", path);
        Assert.DoesNotContain("setDeviceCpuBoostEnabled", source);
        Assert.DoesNotContain("setActiveGameCpuBoostEnabled", source);
    }

    [Fact]
    public void Nested_react_walker_traverses_props_children_child_and_sibling()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("const REACT_WALK_KEYS = [\"props\", \"children\", \"child\", \"sibling\"]", source);
        Assert.Contains("node[REACT_WALK_KEYS[index]]", source);
        Assert.Contains("const visited = new Set();", source);
        Assert.Contains("REACT_WALK_NODE_BUDGET", source);
        Assert.Contains("budgetExhausted", source);
        Assert.Contains("Visited=${producerSearch.visited}", source);
        Assert.Contains("Visited=${ownerSearch.visited}", source);
    }

    [Fact]
    public void Nested_producer_discovery_no_longer_requires_function_type()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        // Discovery predicate: only presence of the lifecycle prop gates matching -- not
        // typeof candidate.type, and not typeof candidate.props.onFocusNavDeactivated either.
        var patchTabsProducerIndex = source.IndexOf("function patchTabsProducer", StringComparison.Ordinal);
        Assert.True(patchTabsProducerIndex >= 0);
        var findReactNodeCallIndex = source.IndexOf("findReactNode(", patchTabsProducerIndex, StringComparison.Ordinal);
        var predicateEndIndex = source.IndexOf(");", findReactNodeCallIndex, StringComparison.Ordinal);
        var predicateSlice = source[findReactNodeCallIndex..predicateEndIndex];

        Assert.Contains("candidate.props?.onFocusNavDeactivated != null", predicateSlice);
        Assert.DoesNotContain("typeof candidate.type === \"function\"", predicateSlice);
        Assert.DoesNotContain("typeof candidate.props.onFocusNavDeactivated === \"function\"", predicateSlice);
    }

    [Fact]
    public void Nested_react_walker_is_cycle_safe_for_arrays()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        // The visited/budget gate must run before an array node is expanded, otherwise a
        // self-referential children array bypasses the bound entirely.
        var findReactNodeIndex = source.IndexOf("function findReactNode(", StringComparison.Ordinal);
        Assert.True(findReactNodeIndex >= 0);
        var arrayCheckIndex = source.IndexOf("Array.isArray(node)", findReactNodeIndex, StringComparison.Ordinal);
        var visitedAddIndex = source.IndexOf("visited.add(node)", findReactNodeIndex, StringComparison.Ordinal);
        Assert.True(visitedAddIndex >= 0 && arrayCheckIndex >= 0);
        Assert.True(visitedAddIndex < arrayCheckIndex, "visited/budget bookkeeping must happen before array expansion.");
    }

    [Fact]
    public void Nested_react_walker_preserves_depth_first_structural_order()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        var findReactNodeIndex = source.IndexOf("function findReactNode(", StringComparison.Ordinal);
        Assert.True(findReactNodeIndex >= 0);
        var walker = source[findReactNodeIndex..];

        // The LIFO stack must receive both arrays and named links in reverse order so
        // traversal visits array elements first-to-last and props -> children -> child -> sibling.
        Assert.Contains("for (let index = node.length - 1; index >= 0; index--)", walker);
        Assert.Contains("for (let index = REACT_WALK_KEYS.length - 1; index >= 0; index--)", walker);
        Assert.Contains("node[REACT_WALK_KEYS[index]]", walker);
    }

    [Fact]
    public void Nested_producer_component_shape_is_resolved_and_guarded_explicitly()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        // Function component path is preserved.
        Assert.Contains("if (typeof type === \"function\") return { kind: \"function\", target: type };", source);
        // Object wrapper render paths are explicit, not generic.
        Assert.Contains("typeof type.render === \"function\"", source);
        Assert.Contains("typeof type.type === \"function\"", source);
        // Unsupported shapes fail open with a distinct diagnostic and no throw path around it.
        Assert.Contains("Nested tabs producer found but component type is unsupported.", source);
        Assert.Contains("if (!resolved) {", source);
    }

    [Fact]
    public void Nested_producer_compare_and_restore_ownership_is_preserved()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var nestedStart = source.IndexOf("function patchTabsProducer", StringComparison.Ordinal);
        var nestedEnd = source.IndexOf("function findReactRootFiber", nestedStart, StringComparison.Ordinal);
        Assert.True(nestedStart >= 0 && nestedEnd > nestedStart);
        var nested = source[nestedStart..nestedEnd];

        Assert.Contains("if (node.type === record.originalType) {", nested);
        Assert.Contains("record = { node: null, originalType: null, patchedType: null, tabs: null }", nested);
        Assert.Contains("record.node = node;", nested);
        Assert.Contains("record.tabs = ensureAddonTabs(owner, React, native);", nested);
        Assert.DoesNotContain("record.nodes", nested);
        Assert.DoesNotContain("record.tabs.add", nested);
        Assert.Contains("state.nestedPatches ??= new Map();", nested);

        Assert.Contains("function restoreQamWidthPatches()", source);
        Assert.Contains("if (node?.type === record.patchedType) node.type = record.originalType;", source);
    }

    [Fact]
    public void Patched_function_shape_and_supported_component_paths_are_explicit()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("function preservePatchedFunctionShape(patched, original)", source);
        Assert.Contains("Object.assign(patched, original);", source);
        Assert.Contains("Function.prototype.toString.call(original)", source);
        Assert.Contains("preservePatchedFunctionShape(function patchedTabsProducer", source);
        Assert.Contains("preservePatchedFunctionShape(function patchedType", source);
        Assert.Contains("return { kind: \"function\", target: type };", source);
        Assert.Contains("return { kind: \"object.render\", target: type.render };", source);
        Assert.Contains("return { kind: \"object.type\", target: type.type };", source);
    }

    [Fact]
    public void Qam_preserves_steam_native_component_discovery_and_the_addon_tab_descriptor()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.DoesNotContain("QAM integration test", source);
        Assert.Contains("title: \"Addon\"", source);
        Assert.DoesNotContain("QamTitleClass", source);
        Assert.DoesNotContain("paddingTop: \"16px\"", source);
        Assert.DoesNotContain("function findPanelComponents(modules)", source);
        Assert.DoesNotContain("function findNativeClassStyles(modules)", source);
        Assert.DoesNotContain("FieldLabelRowClass", source);
        Assert.DoesNotContain("FieldLabelClass", source);
        Assert.DoesNotContain("FieldLabelValueClass", source);
        Assert.DoesNotContain("function labelRow", source);
        Assert.DoesNotContain("justifyContent: \"space-between\"", source);
        Assert.Contains("PanelSection", source);
        Assert.Contains("PanelSectionRow", source);
        Assert.Contains(".TabRowTabs", source);
        Assert.Contains("activeTab:", source);
        Assert.Contains("const Tabs = findUniqueObject(", source);
        Assert.Contains("typeof value?.type === \"function\"", source);
        Assert.Contains("String(value.type).includes(\"(function()\")", source);
        Assert.DoesNotContain("findUniqueFunction(tabsModule", source);
        Assert.Contains("native.Tabs", source);
        Assert.Contains("QAM native Tabs discovery failed", source);
        Assert.DoesNotContain("marginTop: \"-4px\"", source);
        Assert.Contains("fill: \"currentColor\"", source);
        Assert.DoesNotContain("AC Mode", source);
        Assert.DoesNotContain("DC Mode", source);
        Assert.Contains("request(\"captureStatus\")", source);
        Assert.Contains("function scheduleQamSliderCommit", source);
        Assert.Contains("setTimeout(async () =>", source);
        Assert.Contains("state.stateInvalidationSubscribers", source);
        Assert.DoesNotContain("setInterval", source);
        Assert.DoesNotContain("request(\"captureTdp\")", source);
        Assert.DoesNotContain("request(\"capturePowerMode\")", source);
        Assert.DoesNotContain("request(\"captureCpuBoost\")", source);
        Assert.Contains("cancelQamSliderCommits", source);
        Assert.Contains("return subscribeStateInvalidation(handler);", source);
        Assert.Contains("function findNativeQamComponents(webpackRequire)", source);
        Assert.Contains("function findUniqueFactory(webpackRequire, requiredTokens)", source);
        Assert.Contains("function findUniqueFunction(exports, requiredTokens)", source);
        Assert.Contains("function findUniqueObject(exports, predicate)", source);
        Assert.Contains("DialogSlider_Container", source);
        Assert.Contains("DropDownField", source);
        Assert.Contains("SliderField", source);
        Assert.Contains("PanelSectionTitle", source);
        Assert.Contains("spinner", source);
        Assert.Contains("onChangeComplete", source);
        Assert.Contains("valueSuffix", source);
        Assert.Contains("explainerTitle", source);
        Assert.Contains("OnToggleChange", source);
        Assert.Contains("this.Toggle()", source);
        Assert.DoesNotContain("webpackRequire.c", source);
        Assert.DoesNotContain("findNativeComponent", source);
        Assert.DoesNotContain("findUniqueNativeComponent", source);
        Assert.DoesNotContain("requiredProps", source);
        Assert.Contains("QAM native fields factory discovery failed", source);
        Assert.Contains("QAM native layout factory discovery failed", source);
        Assert.Contains("QAM native SliderField discovery failed", source);
        Assert.Contains("QAM native ToggleField discovery failed", source);
        Assert.Contains("QAM native PanelSection discovery failed", source);
        Assert.Contains("QAM native PanelSectionRow discovery failed", source);
        Assert.Contains("state.installFailureKind = \"native-components\"", source);
        Assert.Contains("native.ToggleField", source);
        Assert.Contains("native.SliderField", source);
        Assert.Contains("notchTicksVisible: true", source);
        Assert.Contains("notchLabels", source);
        Assert.Contains("controlled: true", source);
        Assert.Contains("showValue: true", source);
        Assert.Contains("showBookendLabels: true", source);
        Assert.Contains("function logStateChange(key, signature, message)", source);
        Assert.Contains("state.runtimeDiagnostics", source);
        Assert.Contains("QAM page state: Page=", source);
        Assert.Contains("QAM mutation request: Page=", source);
        Assert.Contains("QAM mutation result: Page=", source);
        Assert.Contains("function quickSettingsRowMutationBlockReason(row, busy)", source);
        Assert.Contains("mutationDepthRef", source);
        Assert.Contains("deferredInvalidationRef", source);
        Assert.Contains("beginMutation", source);
        Assert.Contains("endMutation", source);
        Assert.DoesNotContain("type: \"checkbox\"", source);
        Assert.DoesNotContain("type: \"range\"", source);
        Assert.DoesNotContain("fontFamily: \"sans-serif\"", source);
        Assert.Contains("const failClosed", source);
        Assert.Contains("QAM native semantic controls resolved", source);
    }

    [Fact]
    public void Qam_addon_panel_maps_runtime_shell_to_native_inner_tabs_without_local_order_or_fake_apis()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var panelStart = source.IndexOf("function AddonQuickSettingsPanel()", StringComparison.Ordinal);
        var panelEnd = source.IndexOf("state.addonTabDescriptor =", panelStart, StringComparison.Ordinal);
        Assert.True(panelStart >= 0 && panelEnd > panelStart);
        var panel = source[panelStart..panelEnd];

        Assert.Contains("request(\"captureQuickSettingsShell\")", panel);
        Assert.Contains("request(\"captureQuickSettingsTabOrder\")", panel);
        Assert.Contains("validateQuickSettingsShell(shell)", panel);
        Assert.Contains("validateTabOrderState(order)", panel);
        Assert.Contains("const innerTabs = shellTabs.map(tab => ({", panel);
        Assert.Contains("id: String(tab.tabId)", panel);
        Assert.Contains("title: tab.label", panel);
        Assert.Contains("content: buildInnerTabContent(React, native, tab, QuickSettingsPanel,", panel);
        Assert.Contains("tabs: innerTabs", panel);
        Assert.Contains("activeTab,", panel);
        Assert.Contains("onShowTab: setActiveTab", panel);
        Assert.Contains("autoFocusContents: true", panel);
        Assert.Contains("const preferred = Number(status?.steam?.appId || 0) > 0 ? AQS_TAB_PROFILE : AQS_TAB_DEVICE;", panel);
        Assert.Contains("moveQuickSettingsTab", panel);
        Assert.Contains("result?.state", panel);
        Assert.Contains("SettingTabOrderPanel", source);
        Assert.Contains("native.SliderField", source);
        Assert.Contains("React.createElement(SettingTabOrderPanel, { React, native, ...settingProps })", source);
        Assert.DoesNotContain("[\"Device\", \"Profile\", \"Controller\", \"Shortcut\", \"Setting\"]", source);
        Assert.DoesNotContain("QuickSettingsPageId", source);
        Assert.Contains("This page is not available in QAM yet.", source);
    }

    [Fact]
    public void Qam_shortcut_is_a_feature_local_read_only_shared_payload_renderer()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var inner = source[source.IndexOf("function buildInnerTabContent", StringComparison.Ordinal)..source.IndexOf("function SettingTabOrderPanel", StringComparison.Ordinal)];
        var shortcut = source[source.IndexOf("function validateQuickSettingsShortcut", StringComparison.Ordinal)..source.IndexOf("function preservePatchedFunctionShape", StringComparison.Ordinal)];

        Assert.Contains("case AQS_TAB_SHORTCUT:", inner);
        Assert.Contains("QuickSettingsShortcutPanel", inner);
        Assert.Contains("React.createElement(QuickSettingsShortcutPanel, { React, native, title: tab.label });", inner);
        Assert.Contains("function QuickSettingsShortcutPanel({ React, native, title })", shortcut);
        Assert.Contains("request(\"captureQuickSettingsShortcut\")", shortcut);
        Assert.Contains("validateQuickSettingsShortcut(snapshot)", shortcut);
        Assert.Contains("snapshot.slots.length !== 4", shortcut);
        Assert.Contains("KNOWN_AQS_SHORTCUT_SLOT_IDS.has(slot?.slotId)", shortcut);
        Assert.Contains("slot.label", shortcut);
        Assert.Contains("slot.statusLabel", shortcut);
        Assert.Contains("slots.map(slot =>", shortcut);
        Assert.Contains("native.PanelSection", shortcut);
        Assert.Contains("native.PanelSectionRow", shortcut);
        Assert.DoesNotContain("onClick", shortcut);
        Assert.DoesNotContain("ButtonItem", source);
        Assert.DoesNotContain("DialogButton", source);
        Assert.DoesNotContain("setInterval", shortcut);
        Assert.DoesNotContain("captureQuickSettingsShortcut", source[source.IndexOf("function AddonQuickSettingsPanel", StringComparison.Ordinal)..]);
        Assert.Contains("case AQS_TAB_CONTROLLER:", inner);
        Assert.Contains("This page is not available in QAM yet.", inner);
    }

    [Fact]
    public void Qam_setting_renderer_receives_generation_scoped_react_and_native_dependencies()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var start = source.IndexOf("function SettingTabOrderPanel", StringComparison.Ordinal);
        var end = source.IndexOf("function validateQuickSettingsShell", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);

        var setting = source[start..end];
        Assert.Contains("function SettingTabOrderPanel({ React, native, tabOrderState, busy, error, onMove })", setting);
        Assert.DoesNotContain("function SettingTabOrderPanel({ tabOrderState, busy, error, onMove })", setting);
        Assert.Contains("React.createElement(SettingTabOrderPanel, { React, native, ...settingProps })", source);
    }

    [Fact]
    public void Overlay_shortcut_renderer_consumes_the_shared_contract_but_keeps_local_geometry()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs");
        var pageStart = source.IndexOf("private FrameworkElement BuildShortcutPage()", StringComparison.Ordinal);
        var pageEnd = source.IndexOf("private void SelectShortcutSlot", pageStart, StringComparison.Ordinal);
        Assert.True(pageStart >= 0 && pageEnd > pageStart);
        var page = source[pageStart..pageEnd];

        Assert.Contains("AddonQuickSettingsShortcutContract.Create()", page);
        Assert.Contains("slot.Label", page);
        Assert.Contains("slot.StatusLabel", page);
        Assert.DoesNotContain("OverlayShortcutSlotId", source);
        Assert.DoesNotContain("(\"Slot 1\"", page);
        Assert.DoesNotContain("\"Unassigned\"", page);
        Assert.Contains("index / 2", page);
        Assert.Contains("index % 2", page);
    }

    [Fact]
    public void Qam_shell_validation_is_fail_closed_and_only_accepts_the_five_known_identities()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var validationStart = source.IndexOf("function validateQuickSettingsShell(shell)", StringComparison.Ordinal);
        var validationEnd = source.IndexOf("function preservePatchedFunctionShape", validationStart, StringComparison.Ordinal);
        Assert.True(validationStart >= 0 && validationEnd > validationStart);
        var validation = source[validationStart..validationEnd];

        Assert.Contains("shell?.available !== true", validation);
        Assert.Contains("shell.tabs.length !== 5", validation);
        Assert.Contains("KNOWN_AQS_TAB_IDS.has(tab?.tabId)", validation);
        Assert.Contains("seen.has(tab.tabId)", validation);
        Assert.Contains("typeof tab.label !== \"string\" || tab.label.length === 0", validation);
        Assert.Contains("return null;", validation);
        Assert.DoesNotContain("shell.tabs.sort", validation);
    }

    [Fact]
    public void Qam_device_and_profile_share_one_generic_renderer_driven_by_quick_settings_metadata()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var index = source.IndexOf("const renderQuickSettingsRow = (page, section, row) =>", StringComparison.Ordinal);
        Assert.True(index >= 0);
        var renderer = source[index..source.IndexOf("const sections = (quickSettingsPage", index, StringComparison.Ordinal)];

        // Control kind, labels, ranges, options, and value shape all come from the row payload.
        Assert.Contains("row.controlKind === QS_CONTROL_TOGGLE", renderer);
        Assert.Contains("row.controlKind !== QS_CONTROL_SLIDER", renderer);
        Assert.Contains("label: row.label", renderer);
        Assert.Contains("row.sliderSpec.kind === QS_SLIDER_NUMERIC", renderer);
        Assert.Contains("min: row.sliderSpec.minimum, max: row.sliderSpec.maximum, step: row.sliderSpec.step", renderer);
        Assert.Contains("row.sliderSpec.suffix", renderer);
        Assert.Contains("const options = row.sliderSpec.options ?? [];", renderer);
        Assert.Contains("options.findIndex(option => Number(option.value) === Number(effective.integerValue))", renderer);
        Assert.Contains("if (optionIndex < 0) return null;", renderer);
        Assert.Contains("label: option.label", renderer);
        Assert.Contains("scheduleQuickSettingsCommit(page, section, row, option.value)", renderer);
        Assert.Contains("native.ToggleField", renderer);
        Assert.Contains("native.SliderField", renderer);

        // Sections/rows are iterated in payload order with identity-derived keys, for whichever
        // page (Device or Profile) is currently loaded.
        Assert.Contains("(quickSettingsPage?.sections ?? []).map(section =>", source);
        Assert.Contains("key: `qs-section-${section.sectionId}`, title: section.label || undefined", source);
        Assert.Contains("key: `qs-row-${row.rowId}`", source);

        // Neither page owns product labels/options/ranges/policy in JS.
        Assert.DoesNotContain("const QAM_SLIDER_COMMIT_DELAY_MS", source);
        Assert.DoesNotContain("sideValue", source);
        Assert.DoesNotContain("const mutationAvailable", source);
        Assert.DoesNotContain("cpu.lastFailure", source);
        Assert.DoesNotContain("PROFILE_SLIDER_COMMIT_DELAY_MS", source);
    }

    [Fact]
    public void Qam_tdp_groups_use_shared_commit_group_and_a_whole_section_draft_for_either_page()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var scheduleIndex = source.IndexOf("const scheduleQuickSettingsCommit = (page, section, row, nextProductValue) =>", StringComparison.Ordinal);
        Assert.True(scheduleIndex >= 0);
        var schedule = source[scheduleIndex..source.IndexOf("const renderQuickSettingsRow", scheduleIndex, StringComparison.Ordinal)];

        // Pending identity: PageId + AppId + (RowId for independent rows, CommitGroupId for grouped
        // rows) -- section 12 -- so Device and every Profile AppId's drafts stay isolated.
        var pendingKey = source[source.IndexOf("function quickSettingsPendingKey", StringComparison.Ordinal)..source.IndexOf("function quickSettingsPendingValue", StringComparison.Ordinal)];
        Assert.Contains("row.commitGroupId == null", pendingKey);
        Assert.Contains("qs-page:${page.pageId}:app:${appKey}:row:${row.rowId}", pendingKey);
        Assert.Contains("qs-page:${page.pageId}:app:${appKey}:group:${row.commitGroupId}", pendingKey);
        Assert.Contains("const appKey = page.appId ?? \"none\";", pendingKey);
        // A grouped edit seeds the WHOLE containing section (Enabled + four PL sliders) in payload
        // order -- with no hard-coded TDP RowId knowledge in JS, for either page.
        Assert.Contains(": seedQuickSettingsSectionDraft(section);", schedule);
        Assert.Contains("if (row.commitGroupId != null) applyQuickSettingsLinkedConstraints(quickSettingsPageRef.current, draft.values, row.rowId);", schedule);
        Assert.Contains("draft.order.map(rowId => ({ rowId, value: draft.values[rowId] }))", schedule);
        // Delay comes from the row's commit policy, never a JS constant.
        Assert.Contains("const delayMs = row.commitPolicy?.mode === QS_COMMIT_TRAILING", schedule);
        Assert.Contains("Number(row.commitPolicy.delayMilliseconds)", schedule);
        Assert.Contains("\"mutateQuickSetting\"", schedule);
        // The pending Map is outside React -- scheduling a draft forces one renderer-local pass so
        // the immediate preview / linked paired correction is visible before the trailing commit.
        Assert.Contains("const [, bumpQuickSettingsDraftRender] = React.useState(0);", source);
        Assert.Contains("bumpQuickSettingsDraftRender(value => value + 1);", schedule);

        // Only the in-flight delayed RPC runs inside the component's existing mutation/invalidation
        // gate (beginMutation/endMutation via onRequestStart/onRequestEnd); the debounce itself is
        // not gated, and the settlement consumes the mutation's own deferred invalidation.
        Assert.Contains("deferredInvalidationRef.current = false;", schedule);
        var lines = schedule.Split('\n');
        Assert.Contains(lines, l => l.Trim() == "beginMutation,");
        Assert.Contains(lines, l => l.Trim() == "endMutation);");
        var scheduler = source[source.IndexOf("function scheduleQamSliderCommit", StringComparison.Ordinal)..source.IndexOf("// --- Shared Quick Settings Device/Profile helpers", StringComparison.Ordinal)];
        Assert.Contains("onRequestStart = null, onRequestEnd = null", scheduler);
        Assert.Contains("requestStarted = true;", scheduler);
        Assert.Contains("onRequestStart?.();", scheduler);
        Assert.Contains("if (requestStarted) onRequestEnd?.();", scheduler);

        // Seeding reads only the shared section rows and their values, in order.
        var seed = source[source.IndexOf("function seedQuickSettingsSectionDraft", StringComparison.Ordinal)..source.IndexOf("function applyQuickSettingsLinkedConstraints", StringComparison.Ordinal)];
        Assert.Contains("for (const row of section?.rows ?? [])", seed);
        Assert.Contains("if (row.value == null) return null;", seed);

        // Linked correction is metadata-driven: identities + gap + row slider bounds from the page,
        // never known Claw limit tuples or PL1/PL2 label parsing in JS.
        var constraints = source[source.IndexOf("function applyQuickSettingsLinkedConstraints", StringComparison.Ordinal)..source.IndexOf("function request(method, payload)", StringComparison.Ordinal)];
        Assert.Contains("page?.linkedSliderConstraints ?? []", constraints);
        Assert.Contains("constraint.lowerRowId", constraints);
        Assert.Contains("constraint.upperRowId", constraints);
        Assert.Contains("Number(constraint.minimumGap)", constraints);
        Assert.Contains("upperRow.sliderSpec.maximum", constraints);
        Assert.DoesNotContain("pl1MaximumWatts === 30", constraints);
        Assert.DoesNotContain("PL1", constraints);

        // No legacy tuple/label policy survives anywhere in qam.js.
        Assert.DoesNotContain("legacyProfileAdjustTdpPair", source);
        Assert.DoesNotContain("scheduleQamSliderCommit(\"device-tdp\"", source);
        Assert.DoesNotContain("scheduleQamSliderCommit(\"profile-tdp\"", source);
        Assert.DoesNotContain("request(\"setDeviceTdpEnabled\"", source);
        Assert.DoesNotContain("request(\"setActiveGameTdpEnabled\"", source);
        Assert.DoesNotContain("Success", source[source.IndexOf("function buildAddonTab", StringComparison.Ordinal)..]);
        Assert.DoesNotContain("setInterval", source);
        Assert.DoesNotContain("keydown", source);
        Assert.DoesNotContain("gamepad", source);
    }

    [Fact]
    public void Qam_device_and_profile_panels_use_fixed_page_identity_and_profile_tracks_active_app()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        var panelStart = source.IndexOf("function QuickSettingsPanel({ pageId })", StringComparison.Ordinal);
        Assert.True(panelStart >= 0);
        var panel = source[panelStart..source.IndexOf("function AddonQuickSettingsPanel()", panelStart, StringComparison.Ordinal)];
        Assert.Contains("if (pageId === QS_PAGE_DEVICE)", panel);
        var deviceBranch = panel[panel.IndexOf("if (pageId === QS_PAGE_DEVICE)", StringComparison.Ordinal)..panel.IndexOf("} else {", panel.IndexOf("if (pageId === QS_PAGE_DEVICE)", StringComparison.Ordinal), StringComparison.Ordinal)];
        Assert.Contains("nextContext = { pageId: QS_PAGE_DEVICE, appId: null };", deviceBranch);
        Assert.DoesNotContain("captureStatus", deviceBranch);
        Assert.Contains("const nextStatus = await request(\"captureStatus\")", panel);
        Assert.Contains("nextContext = { pageId: QS_PAGE_PROFILE, appId: nextAppId > 0 ? nextAppId : null };", panel);
        Assert.Contains("request(\"captureQuickSettingsPage\", { pageId: nextContext.pageId, appId: nextContext.appId })", panel);
        Assert.Contains("if (pageId === QS_PAGE_PROFILE && previousContext", panel);
        Assert.Contains("if (sameQuickSettingsContext(quickSettingsContextRef.current, nextContext)) {", panel);
        Assert.DoesNotContain("const nextContext = activeGame ?", source);
        Assert.DoesNotContain("const activeGame = nextAppId > 0", source);
        Assert.Contains("state.stateInvalidationSubscribers", source);
        Assert.Contains("function subscribeStateInvalidation(callback)", source);
        Assert.Contains("return subscribeStateInvalidation(handler);", panel);
    }

    [Fact]
    public void Qam_bridge_no_longer_owns_a_second_active_game_profile_gate()
    {
        // SF-V2-08 section 9.4: the legacy feature-specific Profile bridge allowlist and its
        // ActiveMutationAsync helper are gone -- Profile reaches the Runtime only through the same
        // generic captureQuickSettingsPage/mutateQuickSetting seam Device uses.
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "QamFrontendBridge.cs");

        Assert.DoesNotContain("captureActiveGameProfile", source);
        Assert.DoesNotContain("setActiveGameProfileEnabled", source);
        Assert.DoesNotContain("setActiveGameCpuBoostAc", source);
        Assert.DoesNotContain("setActiveGameCpuBoostDc", source);
        Assert.DoesNotContain("setActiveGameTdp", source);
        Assert.DoesNotContain("setActiveGameFpsLimitEnabled", source);
        Assert.DoesNotContain("setActiveGameFpsLimitAc", source);
        Assert.DoesNotContain("setActiveGameFpsLimitDc", source);
        Assert.DoesNotContain("ActiveMutationAsync", source);
        // Typed NamedPipeAddonFrontendClient Profile methods remain available (Main UI/other code) --
        // this test only asserts the removed QAM bridge string-method allowlist.
        Assert.Contains("_client.CaptureQuickSettingsPageAsync", source);
        Assert.Contains("_client.MutateQuickSettingAsync", source);
    }

    [Fact]
    public void Qam_no_visible_legacy_profile_product_definition_or_hidden_fps_branch_remains()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        // The generic page-selection/context plumbing is what now drives Profile.
        Assert.Contains("const nextAppId = Number(nextStatus?.steam?.appId || 0)", source);
        Assert.Contains("nextContext = { pageId: QS_PAGE_PROFILE, appId: nextAppId > 0 ? nextAppId : null };", source);
        Assert.Contains("request(\"captureQuickSettingsPage\", { pageId: nextContext.pageId, appId: nextContext.appId })", source);

        // No hard-coded visible Profile product policy/labels/keys survive.
        Assert.DoesNotContain("PROFILE_SLIDER_COMMIT_DELAY_MS", source);
        Assert.DoesNotContain("legacyProfileAdjustTdpPair", source);
        Assert.DoesNotContain("profileTdpControls", source);
        Assert.DoesNotContain("profileCpuControls", source);
        Assert.DoesNotContain("profilePowerControls", source);
        Assert.DoesNotContain("profile-cpu-toggle", source);
        Assert.DoesNotContain("profile-tdp-toggle", source);
        Assert.DoesNotContain("profile-power-toggle", source);
        Assert.DoesNotContain("profileTdpDraft", source);
        Assert.DoesNotContain("previewAc", source);
        Assert.DoesNotContain("previewDc", source);
        Assert.DoesNotContain("powerPreview", source);
        Assert.DoesNotContain("activeProfileAppIdRef", source);
        Assert.DoesNotContain("setActiveGameCpuBoostEnabled", source);
        Assert.DoesNotContain("setActiveGameTdpEnabled", source);
        Assert.DoesNotContain("setActiveGamePowerModeEnabled", source);
        Assert.DoesNotContain("Plugged in · PL1", source); // now a Runtime-supplied row.label, not a JS literal
        Assert.DoesNotContain("captureActiveGameProfile\")", source);

        // Hidden Intel FPS Limit QAM UI is removed entirely (SF-V2-08 section 5.4/9.4).
        Assert.DoesNotContain("SHOW_INTEL_FPS_LIMIT", source);
        Assert.DoesNotContain("fpsControls", source);
        Assert.DoesNotContain("profile-fps-section", source);
        Assert.DoesNotContain("fpsDraft", source);
        Assert.DoesNotContain("Intel FPS Limit", source);
        Assert.DoesNotContain("setActiveGameFpsLimitAc", source);
        Assert.DoesNotContain("setActiveGameFpsLimitDc", source);

        // No Resolution row/control anywhere in qam.js.
        Assert.DoesNotContain("Resolution", source);

        Assert.DoesNotContain("setInterval", source);
        Assert.DoesNotContain("type: \"checkbox\"", source);
        Assert.DoesNotContain("type: \"range\"", source);
    }

    [Fact]
    public void Qam_context_identity_and_transition_are_pageid_and_appid_based()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("function quickSettingsContextOf(page)", source);
        Assert.Contains("function sameQuickSettingsContext(a, b)", source);
        Assert.Contains("function cancelQuickSettingsPendingForContext(context)", source);
        Assert.Contains("pending?.pageId === context.pageId && (pending?.appId ?? null) === (context.appId ?? null)", source);
        // Context transition retires the OLD context's pending work on change.
        var refresh = source[source.IndexOf("const refresh = React.useCallback(async () => {", StringComparison.Ordinal)..source.IndexOf("const beginMutation", StringComparison.Ordinal)];
        Assert.Contains("if (pageId === QS_PAGE_PROFILE && previousContext && !sameQuickSettingsContext(previousContext, nextContext)) {", refresh);
        // Late-result guard: a stale settlement is dropped instead of overwriting the current page.
        var apply = source[source.IndexOf("const applyQuickSettingsResult = result =>", StringComparison.Ordinal)..source.IndexOf("const commitQuickSettingsImmediate", StringComparison.Ordinal)];
        Assert.Contains("if (!sameQuickSettingsContext(quickSettingsContextOf(result?.page), quickSettingsContextRef.current)) return;", apply);
    }

    [Fact]
    public void Qam_generic_prune_retires_a_pending_row_the_fresh_page_no_longer_allows()
    {
        // Review fix (PR #509): a pending child draft (e.g. a Profile TDP/CPU/Power slider) must be
        // retired the moment a fresh same-context authoritative page makes its edited row absent or
        // non-writable -- generically, driven only by page/row metadata, never a per-feature or
        // per-section special case (SF-V2-08 sections 13.1/14/25).
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        var pruneStart = source.IndexOf("function pruneQuickSettingsPendingAgainstPage(page)", StringComparison.Ordinal);
        Assert.True(pruneStart >= 0);
        var prune = source[pruneStart..source.IndexOf("// onRequestStart / onRequestEnd wrap", pruneStart, StringComparison.Ordinal)];

        Assert.Contains("pending?.pageId !== page.pageId || (pending?.appId ?? null) !== (page.appId ?? null)", prune);
        Assert.Contains("const editedRowId = pending?.payload?.editedRowId;", prune);
        Assert.Contains("const row = findQuickSettingsRow(page, editedRowId);", prune);
        Assert.Contains("if (row?.available === true && row?.writable === true) continue;", prune);
        Assert.Contains("clearTimeout(pending.timer);", prune);
        Assert.Contains("state.qamSliderCommits.delete(key);", prune);
        // Purely page/row-metadata driven -- never special-cases a specific row/section identity.
        Assert.DoesNotContain("ProfileEnabled", prune);
        Assert.DoesNotContain("ProfileTdp", prune);
        Assert.DoesNotContain("ProfileCpuBoost", prune);
        Assert.DoesNotContain("ProfilePowerMode", prune);
        Assert.DoesNotContain("sectionId", prune);

        // Called for every fresh same-context authoritative page: an ordinary refresh...
        var refresh = source[source.IndexOf("const refresh = React.useCallback(async () => {", StringComparison.Ordinal)..source.IndexOf("const beginMutation", StringComparison.Ordinal)];
        Assert.Contains("pruneQuickSettingsPendingAgainstPage(nextPage);", refresh);
        Assert.True(refresh.IndexOf("pruneQuickSettingsPendingAgainstPage(nextPage);", StringComparison.Ordinal)
            < refresh.IndexOf("setQuickSettingsPage(nextPage);", StringComparison.Ordinal));
        // ...and a mutation settlement (both success and typed failure, since both carry a page).
        var apply = source[source.IndexOf("const applyQuickSettingsResult = result =>", StringComparison.Ordinal)..source.IndexOf("const commitQuickSettingsImmediate", StringComparison.Ordinal)];
        Assert.Contains("pruneQuickSettingsPendingAgainstPage(result.page);", apply);
        Assert.True(apply.IndexOf("pruneQuickSettingsPendingAgainstPage(result.page);", StringComparison.Ordinal)
            < apply.IndexOf("setQuickSettingsPage(result.page);", StringComparison.Ordinal));
    }

    [Fact]
    public void Qam_all_sliders_use_the_shared_trailing_commit_path_while_toggles_stay_immediate()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        // The one shared scheduler mechanism is reused for both pages; no JS Device/Profile delay
        // constant or fallback remains.
        Assert.DoesNotContain("QAM_SLIDER_COMMIT_DELAY_MS", source);
        Assert.DoesNotContain("PROFILE_SLIDER_COMMIT_DELAY_MS", source);
        Assert.DoesNotContain("QS_FALLBACK_COMMIT_DELAY_MS", source);
        Assert.Contains("state.qamSliderCommits", source);
        Assert.Contains("clearTimeout(pending.timer)", source);
        Assert.Contains("cancelQamSliderCommits();", source);

        // Slider delay comes from row.commitPolicy; malformed policy fails closed without scheduling.
        Assert.Contains("const delayMs = row.commitPolicy?.mode === QS_COMMIT_TRAILING", source);
        Assert.Contains("Number(row.commitPolicy.delayMilliseconds)", source);
        Assert.Contains("if (!Number.isFinite(delayMs) || delayMs <= 0)", source);
        Assert.Contains("setError(\"Quick Settings is unavailable.\")", source);
        Assert.Contains("}, delayMs);", source);
        Assert.DoesNotContain("QS_FALLBACK_COMMIT_DELAY_MS", source);
        var immediate = source[source.IndexOf("const commitQuickSettingsImmediate", StringComparison.Ordinal)..source.IndexOf("const scheduleQuickSettingsCommit", StringComparison.Ordinal)];
        Assert.DoesNotContain("scheduleQamSliderCommit", immediate);
        Assert.Contains("request(\"mutateQuickSetting\"", immediate);
        Assert.DoesNotContain("250", source);
        Assert.DoesNotContain("275", source);
        Assert.DoesNotContain("300", source);
    }

    [Fact]
    public void Qam_pending_drafts_restore_after_remount_and_old_commit_responses_cannot_rewind_new_edits()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        // refresh() has no unscoped state.qamSliderCommits deletion inline (only the explicit
        // context/admission-scoped cancellation and the generic row-validity prune, both of which
        // leave a still-writable same-context pending entry untouched), so that entry survives an
        // ordinary StateInvalidated; the rendered value checks the pending draft before row.value.
        var refresh = source[source.IndexOf("const refresh = React.useCallback(async () => {", StringComparison.Ordinal)..source.IndexOf("const beginMutation", StringComparison.Ordinal)];
        Assert.DoesNotContain("qamSliderCommits.delete", refresh);
        Assert.DoesNotContain("qamSliderCommits.set", refresh);
        Assert.Contains("const quickSettingsRowEffectiveValue = row => quickSettingsPendingValue(quickSettingsPage, row.rowId) ?? row.value;", source);

        var scheduler = source[source.IndexOf("function scheduleQamSliderCommit", StringComparison.Ordinal)..source.IndexOf("// --- Shared Quick Settings Device/Profile helpers", StringComparison.Ordinal)];
        Assert.Contains("if (state.qamSliderCommits.get(key)?.token !== token) return;", scheduler);
        Assert.Contains("state.qamSliderCommits.delete(key);", scheduler);
        Assert.True(scheduler.IndexOf("const result = await request(method, payload)", StringComparison.Ordinal)
            < scheduler.IndexOf("state.qamSliderCommits.delete(key);", scheduler.IndexOf("const result = await request(method, payload)", StringComparison.Ordinal), StringComparison.Ordinal));
        // The pre-fire staleness re-check reuses the same generic capture seam Device uses, not a
        // legacy Profile-only bridge method.
        Assert.Contains("entry.pageId === QS_PAGE_PROFILE && entry.appId", scheduler);
        Assert.Contains("request(\"captureQuickSettingsPage\", { pageId: QS_PAGE_PROFILE, appId: entry.appId })", scheduler);
        Assert.DoesNotContain("captureActiveGameProfile", scheduler);
    }

    [Fact]
    public void Qam_invalidation_keeps_pending_drafts_and_never_clears_the_page_directly()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        var handlerStart = source.IndexOf("const handler = () =>", StringComparison.Ordinal);
        var handlerEnd = source.IndexOf("return subscribeStateInvalidation(handler);", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = source[handlerStart..handlerEnd];
        // The invalidation handler only re-refreshes -- it never erases pending drafts/the page.
        Assert.DoesNotContain("setQuickSettingsPage(null)", handler);
        Assert.DoesNotContain("cancelQamSliderCommits", handler);
        Assert.Contains("void refresh();", handler);
    }

    [Fact]
    public void Qam_mutation_result_page_is_authoritative_on_success_and_failure()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        var apply = source[source.IndexOf("const applyQuickSettingsResult = result =>", StringComparison.Ordinal)..source.IndexOf("const commitQuickSettingsImmediate", StringComparison.Ordinal)];
        // result.page always wins -- no optimistic rollback to the previous page on a typed failure.
        Assert.Contains("setQuickSettingsPage(result.page); quickSettingsPageRef.current = result.page;", apply);
        Assert.Contains("setError(!result?.succeeded ? (result?.failureMessage || \"Quick Settings update failed\") : null);", apply);
        Assert.DoesNotContain("rollback", source);

        // The immediate path swallows its own self-triggered invalidation.
        var immediate = source[source.IndexOf("const commitQuickSettingsImmediate", StringComparison.Ordinal)..source.IndexOf("const scheduleQuickSettingsCommit", StringComparison.Ordinal)];
        Assert.Contains("applyQuickSettingsResult(result);", immediate);
        Assert.Contains("deferredInvalidationRef.current = false;", immediate);
        Assert.Contains("finally { endMutation(); setBusy(false); }", immediate);
    }

    [Fact]
    public void Qam_cpu_boost_panel_reuses_its_descriptor_and_gates_mutation_generically()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("state.addonTabDescriptor", source);
        Assert.Contains("[TAB_MARKER]: ADDON_TAB_KEY", source);
        Assert.Contains("function ensureAddonTabs", source);
        Assert.Contains("addonTabKey(tab)", source);
        Assert.Contains("const desired = [descriptor];", source);
        // Row mutation is gated only by shared writability and local busy state.
        Assert.Contains("const canMutateQuickSettingsRow = row => quickSettingsRowMutationBlockReason(row, busy) == null;", source);
        Assert.Contains("const requireQuickSettingsRowMutation = row =>", source);
        Assert.Contains("QAM mutation blocked: Row=", source);
        Assert.Contains("if (!state.installed || !requireQuickSettingsRowMutation(row)) return;", source);
        Assert.Contains("cancelQamSliderCommits();", source);
        Assert.Contains("retireBridgeConsumers", source);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts])).ReplaceLineEndings("\n");
    }
}
