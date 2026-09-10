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
    public void Qam_bridge_requires_active_big_picture_without_a_running_game()
    {
        var bridge = ReadSource("src", "SteamInputAddonforClaw.QamHost", "QamFrontendBridge.cs");

        Assert.Contains("!status.Steam.Active", bridge);
        Assert.Contains("status.Steam.AppId != 0", bridge);
        Assert.Contains("status.Steam.Source != FrontendSteamSource.BigPicture", bridge);
    }

    [Fact]
    public void Qam_bridge_device_path_is_only_the_generic_quick_settings_seam()
    {
        var bridge = ReadSource("src", "SteamInputAddonforClaw.QamHost", "QamFrontendBridge.cs");

        // SF-V2-05: exactly the two approved generic bridge names, and the one Device admission rule.
        Assert.Contains("\"captureQuickSettingsPage\" => await CaptureQuickSettingsPageAsync(root, token),", bridge);
        Assert.Contains("\"mutateQuickSetting\" => await MutateQuickSettingAsync(root, token),", bridge);
        Assert.Contains("await EnsureDeviceMutationAdmittedAsync(token)", bridge);
        // Product validation stays in SF-V2-03; the bridge only scopes the surface to Device.
        Assert.Contains("intent.PageId != QuickSettingsPageId.Device", bridge);

        // The transitional feature-specific Device bridge operations are gone now that qam.js
        // renders/mutates Device only through the shared page.
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

        // Legacy Profile bridge operations remain untouched.
        Assert.Contains("\"setActiveGameCpuBoostAc\"", bridge);
        Assert.Contains("\"setActiveGameTdp\"", bridge);
        Assert.Contains("DecodePowerMode", bridge);
    }

    [Fact]
    public void Qam_uninstall_retires_pending_bridge_consumers_without_resetting_ids()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("function retireBridgeConsumers()", source);
        Assert.Contains("pending.reject(new Error(\"QAM bridge stopped\"))", source);
        Assert.Contains("state.bridgePending?.clear()", source);
        Assert.Contains("state.onStateInvalidated = null", source);
        Assert.DoesNotContain("state.bridgeNextId = 0", source);
    }

    [Fact]
    public void Qam_device_immediate_toggle_retires_same_section_pending_work_generically()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var index = source.IndexOf("const commitDeviceImmediate = async (page, section, row, nextValue) =>", StringComparison.Ordinal);
        Assert.True(index >= 0);
        var path = source[index..source.IndexOf("const scheduleDeviceQuickSettingsCommit", index, StringComparison.Ordinal)];

        // Same-section cancellation is derived from the shared section identity, not device-* strings.
        Assert.Contains("cancelQamSliderCommits((key, pending) => pending?.deviceSectionId === section.sectionId);", path);
        Assert.DoesNotContain("device-cpu-", path);
        Assert.Contains("if (!state.installed || !canMutateDeviceRow(row)) return;", path);
        Assert.Contains("request(\"mutateQuickSetting\"", path);
        Assert.Contains("editedRowId: row.rowId", path);
        Assert.DoesNotContain("setDeviceCpuBoostEnabled", source);
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

        Assert.Contains("if (node.type === record.originalType) {", source);
        Assert.Contains("record = { node: null, originalType: null, patchedType: null, tabs: null }", source);
        Assert.Contains("record.node = node;", source);
        Assert.Contains("if (record.node?.type === record.patchedType)", source);
        Assert.Contains("record.node.type = record.originalType;", source);
        Assert.Contains("record.tabs = owner.props.tabs;", source);
        Assert.Contains("record.node = null;", source);
        Assert.Contains("record.tabs = null;", source);
        Assert.DoesNotContain("record.nodes", source);
        Assert.DoesNotContain("record.tabs.add", source);
        Assert.Contains("state.nestedPatches ??= new Map();", source);
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
        Assert.Contains("title: null", source);
        Assert.Contains("className: native.QamTitleClass", source);
        Assert.Contains("style: { paddingTop: \"16px\" }", source);
        Assert.Contains("function findPanelComponents(modules)", source);
        Assert.Contains("let defaultCandidate = null", source);
        Assert.Contains("try { defaultCandidate = module?.default ?? null; } catch (_) { }", source);
        Assert.Contains("candidate === window", source);
        Assert.Contains("let panelSection = null", source);
        Assert.Contains("candidate[exportName]", source);
        Assert.Contains("source?.includes(\".PanelSection\")", source);
        Assert.Contains("return { PanelSection: panelSection, PanelSectionRow: panelSectionRow }", source);
        Assert.DoesNotContain("Object.values(candidate).find(value => value?.toString", source);
        Assert.Contains("try { value = candidate[exportName]; } catch (_) { continue; }", source);
        Assert.Contains("PanelSection", source);
        Assert.Contains("PanelSectionRow", source);
        Assert.Contains("function isSteamClassModule(candidate)", source);
        Assert.Contains("candidate.Title && candidate.QuickAccessMenu && candidate.BatteryDetailsLabels", source);
        Assert.Contains("candidate.FieldLabelRow && candidate.FieldLabel && candidate.FieldLabelValue", source);
        Assert.Contains("FieldLabelRowClass", source);
        Assert.Contains("FieldLabelClass", source);
        Assert.Contains("FieldLabelValueClass", source);
        Assert.Contains("style: { display: \"flex\", width: \"100%\", justifyContent: \"space-between\" }", source);
        Assert.DoesNotContain("marginTop: \"-4px\"", source);
        Assert.Contains("fill: \"currentColor\"", source);
        Assert.DoesNotContain("AC Mode", source);
        Assert.DoesNotContain("DC Mode", source);
        Assert.Contains("request(\"captureStatus\")", source);
        Assert.Contains("function scheduleQamSliderCommit", source);
        Assert.Contains("setTimeout(async () =>", source);
        Assert.Contains("state.onStateInvalidated", source);
        Assert.DoesNotContain("setInterval", source);
        Assert.DoesNotContain("request(\"captureTdp\")", source);
        Assert.DoesNotContain("request(\"capturePowerMode\")", source);
        Assert.DoesNotContain("request(\"captureCpuBoost\")", source);
        Assert.Contains("cancelQamSliderCommits", source);
        Assert.Contains("state.onStateInvalidated === handler", source);
        Assert.Contains("function findNativeQamComponents(webpackRequire)", source);
        Assert.Contains("const module = webpackRequire(id)", source);
        Assert.Contains("for (const module of modules)", source);
        Assert.Contains("if (module?.default && isCommonUiModule(module.default))", source);
        Assert.Contains("if (isCommonUiModule(module))", source);
        Assert.Contains("Source=default", source);
        Assert.Contains("Source=root", source);
        Assert.DoesNotContain("webpackRequire.c", source);
        Assert.Contains("function findCommonUiModule(modules)", source);
        Assert.Contains("Object.keys(candidate).length > 60", source);
        Assert.Contains("candidate[prop]?.contextType?._currentValue", source);
        Assert.Contains("function findToggleField(commonUiModule)", source);
        Assert.Contains("function findSliderField(commonUiModule)", source);
        Assert.Contains("Object.values(commonUiModule)", source);
        Assert.Contains("candidate?.render?.toString?.()", source);
        Assert.Contains("candidate?.toString?.()", source);
        Assert.Contains("source?.includes('ToggleField\",')", source);
        Assert.Contains("source?.includes('SliderField\",')", source);
        Assert.DoesNotContain("findNativeComponent", source);
        Assert.DoesNotContain("findUniqueNativeComponent", source);
        Assert.DoesNotContain("requiredProps", source);
        Assert.Contains("Steam CommonUIModule unavailable.", source);
        Assert.Contains("native ToggleField unavailable", source);
        Assert.Contains("native SliderField unavailable", source);
        Assert.Contains("state.installFailureKind = \"native-components\"", source);
        Assert.Contains("native.ToggleField", source);
        Assert.Contains("native.SliderField", source);
        Assert.Contains("notchTicksVisible: true", source);
        Assert.DoesNotContain("numericNotches", source);
        Assert.DoesNotContain("notchLabels", source);
        Assert.Contains("mutationDepthRef", source);
        Assert.Contains("deferredInvalidationRef", source);
        Assert.Contains("beginMutation", source);
        Assert.Contains("endMutation", source);
        Assert.DoesNotContain("type: \"checkbox\"", source);
        Assert.DoesNotContain("type: \"range\"", source);
        Assert.DoesNotContain("fontFamily: \"sans-serif\"", source);
        Assert.Contains("const failClosed", source);
        Assert.Contains("QAM required native controls/layout unavailable", source);
    }

    [Fact]
    public void Qam_device_page_renders_generically_from_shared_quick_settings_metadata()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var index = source.IndexOf("const renderDeviceQuickSettingsRow = (page, section, row) =>", StringComparison.Ordinal);
        Assert.True(index >= 0);
        var renderer = source[index..source.IndexOf("if (activeProfile) {", index, StringComparison.Ordinal)];

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
        Assert.Contains("options[optionIndex].label", renderer);
        Assert.Contains("scheduleDeviceQuickSettingsCommit(page, section, row, option.value)", renderer);
        Assert.Contains("native.ToggleField", renderer);
        Assert.Contains("native.SliderField", renderer);

        // Sections/rows are iterated in payload order with identity-derived keys.
        Assert.Contains("(devicePage?.sections ?? []).map(section =>", source);
        Assert.Contains("key: `qs-section-${section.sectionId}`, title: section.label || undefined", source);
        Assert.Contains("key: `qs-row-${row.rowId}`", source);

        // The Device path no longer owns product labels/options/ranges/policy.
        Assert.DoesNotContain("const QAM_SLIDER_COMMIT_DELAY_MS", source);
        Assert.DoesNotContain("sideValue", source);
        Assert.DoesNotContain("const mutationAvailable", source);
        Assert.DoesNotContain("cpu.lastFailure", source);
    }

    [Fact]
    public void Qam_device_tdp_group_uses_shared_commit_group_and_a_whole_section_draft()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var scheduleIndex = source.IndexOf("const scheduleDeviceQuickSettingsCommit = (page, section, row, nextProductValue) =>", StringComparison.Ordinal);
        Assert.True(scheduleIndex >= 0);
        var schedule = source[scheduleIndex..source.IndexOf("const renderDeviceQuickSettingsRow", scheduleIndex, StringComparison.Ordinal)];

        // Pending identity: independent row -> RowId key; grouped row -> CommitGroupId key.
        var pendingKey = source[source.IndexOf("function deviceQuickSettingsPendingKey", StringComparison.Ordinal)..source.IndexOf("function deviceQuickSettingsPendingValue", StringComparison.Ordinal)];
        Assert.Contains("row.commitGroupId == null", pendingKey);
        Assert.Contains("`device-row:${row.rowId}`", pendingKey);
        Assert.Contains("`device-group:${row.commitGroupId}`", pendingKey);
        // A grouped edit seeds the WHOLE containing section (Enabled + four PL sliders) in payload
        // order -- with no hard-coded Device TDP RowId knowledge in JS.
        Assert.Contains(": seedDeviceQuickSettingsSectionDraft(section);", schedule);
        Assert.Contains("if (row.commitGroupId != null) applyDeviceQuickSettingsLinkedConstraints(devicePageRef.current, draft.values, row.rowId);", schedule);
        Assert.Contains("draft.order.map(rowId => ({ rowId, value: draft.values[rowId] }))", schedule);
        // Delay comes from the row's commit policy, never a JS constant.
        Assert.Contains("const delayMs = row.commitPolicy?.mode === QS_COMMIT_TRAILING ? Number(row.commitPolicy.delayMilliseconds) : 0;", schedule);
        Assert.Contains("\"mutateQuickSetting\"", schedule);
        // The pending Map is outside React -- scheduling a draft forces one renderer-local pass so
        // the immediate preview / linked paired correction is visible before the trailing commit.
        Assert.Contains("const [, bumpDeviceDraftRender] = React.useState(0);", source);
        Assert.Contains("bumpDeviceDraftRender(value => value + 1);", schedule);

        // Seeding reads only the shared section rows and their values, in order.
        var seed = source[source.IndexOf("function seedDeviceQuickSettingsSectionDraft", StringComparison.Ordinal)..source.IndexOf("function applyDeviceQuickSettingsLinkedConstraints", StringComparison.Ordinal)];
        Assert.Contains("for (const row of section?.rows ?? [])", seed);
        Assert.Contains("if (row.value == null) return null;", seed);

        // Linked correction is metadata-driven: identities + gap + row slider bounds from the page,
        // never known Claw limit tuples or PL1/PL2 label parsing in the Device path.
        var constraints = source[source.IndexOf("function applyDeviceQuickSettingsLinkedConstraints", StringComparison.Ordinal)..source.IndexOf("function request(method, payload)", StringComparison.Ordinal)];
        Assert.Contains("page?.linkedSliderConstraints ?? []", constraints);
        Assert.Contains("constraint.lowerRowId", constraints);
        Assert.Contains("constraint.upperRowId", constraints);
        Assert.Contains("Number(constraint.minimumGap)", constraints);
        Assert.Contains("upperRow.sliderSpec.maximum", constraints);
        Assert.DoesNotContain("pl1MaximumWatts === 30", constraints);
        Assert.DoesNotContain("PL1", constraints);

        // The legacy tuple/label policy survives only in a Profile-scoped helper.
        Assert.Contains("const legacyProfileAdjustTdpPair", source);
        Assert.DoesNotContain("scheduleQamSliderCommit(\"device-tdp\"", source);
        Assert.DoesNotContain("request(\"setDeviceTdpEnabled\"", source);
        Assert.DoesNotContain("Success", source[source.IndexOf("function buildAddonTab", StringComparison.Ordinal)..]);
        Assert.DoesNotContain("setInterval", source);
        Assert.DoesNotContain("keydown", source);
        Assert.DoesNotContain("gamepad", source);
    }

    [Fact]
    public void Qam_no_active_game_device_refresh_uses_the_shared_aggregate()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        var refreshStart = source.IndexOf("const refresh = React.useCallback(async () => {", StringComparison.Ordinal);
        Assert.True(refreshStart >= 0);
        var refresh = source[refreshStart..source.IndexOf("const beginMutation", refreshStart, StringComparison.Ordinal)];

        Assert.Contains("const nextDevicePage = activeGame ? null : await request(\"captureQuickSettingsPage\", { pageId: QS_PAGE_DEVICE, appId: null });", refresh);
        Assert.Contains("setDevicePage(nextDevicePage); devicePageRef.current = nextDevicePage;", refresh);
        Assert.DoesNotContain("captureDeviceQuickSettings", refresh);
        Assert.DoesNotContain("captureCpuBoost", refresh);
        Assert.DoesNotContain("capturePowerMode", refresh);
        Assert.DoesNotContain("\"captureTdp\"", refresh);
        // Status/active Profile stay their own separate reads (surface admission / page selection).
        Assert.Contains("await request(\"captureStatus\")", refresh);
        Assert.Contains("await request(\"captureActiveGameProfile\")", refresh);
        // Device delayed commits are retired when the Device surface context/admission is lost.
        Assert.Contains("if (activeGame || !deviceMutationAdmitted) cancelQamSliderCommits(key => key.startsWith(\"device-\"));", refresh);
    }

    [Fact]
    public void Qam_bridge_exposes_active_game_profile_path_separate_from_device_mutation_gate()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "QamFrontendBridge.cs");

        Assert.Contains("captureActiveGameProfile", source);
        Assert.Contains("setActiveGameProfileEnabled", source);
        Assert.Contains("setActiveGameCpuBoostAc", source);
        Assert.Contains("setActiveGameCpuBoostDc", source);
        Assert.Contains("setActiveGameTdp", source);
        Assert.Contains("setActiveGameFpsLimitEnabled", source);
        Assert.Contains("setActiveGameFpsLimitAc", source);
        Assert.Contains("setActiveGameFpsLimitDc", source);
        var activePath = source[source.IndexOf("private async Task<object> ActiveMutationAsync", StringComparison.Ordinal)..];
        Assert.Contains("CaptureActiveGameProfileAsync", activePath);
        Assert.DoesNotContain("CaptureStatusAsync", activePath);
        Assert.DoesNotContain("MutateAsync", activePath);
    }

    [Fact]
    public void Qam_projects_active_game_profile_without_device_controls_or_polling()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("const nextAppId = Number(nextProfile?.appId || 0)", source);
        Assert.Contains("const activeProfile = Number(profile?.appId || 0) > 0;", source);
        Assert.DoesNotContain("const activeProfile = !!profile && status?.steam?.appId > 0;", source);
        Assert.DoesNotContain("const nextAppId = Number(nextStatus.steam?.appId || 0)", source);
        Assert.Contains("request(\"captureActiveGameProfile\")", source);
        Assert.Contains("key: \"profile-toggle\"", source);
        Assert.Contains("request(\"setActiveGameProfileEnabled\"", source);
        Assert.Contains("\"setActiveGameCpuBoostAc\"", source);
        Assert.Contains("\"setActiveGameCpuBoostDc\"", source);
        Assert.Contains("\"setActiveGameTdp\"", source);
        Assert.Contains("const activeProfileAppIdRef = React.useRef(0);", source);
        Assert.Contains("if (activeProfileAppIdRef.current !== nextAppId)", source);
        Assert.Contains("cancelQamSliderCommits(key => key.startsWith(\"profile-\"))", source);
        Assert.Contains("labelFor(preview ?? value)", source);
        Assert.Contains("profile-cpu-toggle", source);
        Assert.Contains("profile-tdp-toggle", source);
        Assert.Contains("profile-power-toggle", source);
        Assert.DoesNotContain("profile-tdp-ac-heading", source);
        Assert.DoesNotContain("profile-tdp-dc-heading", source);
        Assert.DoesNotContain("fps-description", source);
        Assert.Contains("setActiveGameCpuBoostEnabled", source);
        Assert.Contains("setActiveGameTdpEnabled", source);
        Assert.Contains("setActiveGamePowerModeEnabled", source);
        var featureToggle = source[source.IndexOf("const toggleProfileFeature", StringComparison.Ordinal)..source.IndexOf("const scheduleProfileTdp", StringComparison.Ordinal)];
        Assert.Contains("beginMutation();", featureToggle);
        Assert.Contains("await refresh();", featureToggle);
        Assert.Contains("deferredInvalidationRef.current = false;", featureToggle);
        Assert.Contains("finally { endMutation(); setBusy(false); }", featureToggle);
        Assert.Contains("Plugged in · PL1", source);
        Assert.Contains("On battery · PL2", source);
        Assert.Contains("if (feature === \"CPU Boost\")", source);
        Assert.Contains("if (feature === \"Power Mode\")", source);
        Assert.Contains("if (feature === \"TDP\" && result.snapshot?.tdp)", source);
        Assert.Contains("profileTdpDraftRef.current = nextDraft; setProfileTdpDraft(nextDraft);", source);
        Assert.Contains("disabled: !writable || !enabled", source);
        Assert.Contains("disabled: !profile.persistenceWritable || !enabled", source);
        Assert.Contains("profile.cpuBoost?.ac", source);
        Assert.Contains("profileTdpDraft?.dc?.pl2Watts", source);
        Assert.Contains("const SHOW_INTEL_FPS_LIMIT = false;", source);
        Assert.Contains("profile-fps-section", source);
        Assert.Contains("Intel FPS Limit", source);
        Assert.Contains("label: \"Intel FPS Limit\"", source);
        Assert.Contains("fps.unavailableReason || \"Intel FPS Limit is unavailable.\"", source);
        Assert.DoesNotContain("key: \"profile-fps-section\", title:", source);
        Assert.Contains("SHOW_INTEL_FPS_LIMIT ? React.createElement(native.PanelSection, { key: \"profile-fps-section\" }", source);
        Assert.Contains("min: 40, max: 120, step: 1", source);
        Assert.Contains("`${currentValue} FPS`", source);
        Assert.Contains("value: currentValue", source);
        Assert.Contains("setActiveGameFpsLimitAc", source);
        Assert.Contains("setActiveGameFpsLimitDc", source);
        Assert.Contains("scheduleQamSliderCommit(`profile-fps-${side}`", source);
        Assert.DoesNotContain("setInterval", source);
        Assert.DoesNotContain("type: \"checkbox\"", source);
        Assert.DoesNotContain("type: \"range\"", source);
    }

    [Fact]
    public void Qam_active_profile_renders_power_mode_in_its_own_section()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        var profileStart = source.IndexOf("const profileCpuControls", StringComparison.Ordinal);
        var tdpSection = source.IndexOf("key: \"profile-tdp-section\"", profileStart, StringComparison.Ordinal);
        var fpsSection = source.IndexOf("key: \"profile-fps-section\"", tdpSection, StringComparison.Ordinal);
        var cpuSection = source.IndexOf("key: \"profile-cpu-section\"", fpsSection, StringComparison.Ordinal);
        var powerSection = source.IndexOf("key: \"profile-power-section\"", cpuSection, StringComparison.Ordinal);
        Assert.True(profileStart >= 0 && tdpSection > profileStart && fpsSection > tdpSection && cpuSection > fpsSection && powerSection > cpuSection);
        var profileLayout = source[profileStart..];

        Assert.Contains("const profilePowerControls", profileLayout);
        Assert.DoesNotContain("Resolution", profileLayout, StringComparison.Ordinal);
        Assert.Contains("key: \"profile-power-section\"", profileLayout);
        Assert.Contains("key: \"profile-cpu-section\"", profileLayout);
        Assert.DoesNotContain("key: \"profile-power-section\", title:", profileLayout);
        Assert.DoesNotContain("key: \"profile-cpu-section\", title:", profileLayout);
        Assert.DoesNotContain("key: \"profile-tdp-section\", title:", source);
        var cpuLayout = profileLayout[profileLayout.IndexOf("key: \"profile-cpu-section\"", StringComparison.Ordinal)..profileLayout.IndexOf("key: \"profile-power-section\"", StringComparison.Ordinal)];
        Assert.Contains("profileCpuControls.filter", cpuLayout);
        Assert.DoesNotContain("profilePowerControls.filter", cpuLayout);
    }

    [Fact]
    public void Qam_all_sliders_use_the_shared_trailing_commit_path_while_toggles_stay_immediate()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        // The one shared scheduler mechanism is reused; no JS Device delay constant remains.
        Assert.DoesNotContain("QAM_SLIDER_COMMIT_DELAY_MS", source);
        Assert.Contains("const PROFILE_SLIDER_COMMIT_DELAY_MS = 2000", source);
        Assert.Contains("state.qamSliderCommits", source);
        Assert.Contains("clearTimeout(pending.timer)", source);
        Assert.Contains("cancelQamSliderCommits();", source);
        Assert.Contains("scheduleQamSliderCommit(`profile-fps-${side}`", source);

        // Device slider delay comes from row.commitPolicy; Device toggles commit immediately.
        Assert.Contains("const delayMs = row.commitPolicy?.mode === QS_COMMIT_TRAILING ? Number(row.commitPolicy.delayMilliseconds) : 0;", source);
        var deviceImmediate = source[source.IndexOf("const commitDeviceImmediate", StringComparison.Ordinal)..source.IndexOf("const scheduleDeviceQuickSettingsCommit", StringComparison.Ordinal)];
        Assert.DoesNotContain("scheduleQamSliderCommit", deviceImmediate);
        Assert.Contains("request(\"mutateQuickSetting\"", deviceImmediate);

        // Legacy Profile power scheduler mechanics unchanged.
        var powerSchedule = source[source.IndexOf("const schedulePowerMode", StringComparison.Ordinal)..source.IndexOf("const powerSlider", StringComparison.Ordinal)];
        Assert.Contains("setPowerPreview(current => ({ ...current, [key]: value }))", powerSchedule);
        Assert.True(powerSchedule.IndexOf("setPowerPreview", StringComparison.Ordinal)
            < powerSchedule.IndexOf("scheduleQamSliderCommit", StringComparison.Ordinal));
        Assert.True(powerSchedule.IndexOf("await refresh();", StringComparison.Ordinal)
            < powerSchedule.IndexOf("delete next[key]", StringComparison.Ordinal));
        var powerSlider = source[source.IndexOf("const powerSlider", StringComparison.Ordinal)..source.IndexOf("// --- Generic Device Quick Settings renderer", StringComparison.Ordinal)];
        Assert.Contains("schedulePowerMode", powerSlider);
        Assert.Contains("powerPreview[key] ?? pendingValue ?? value", powerSlider);
        Assert.DoesNotContain("runPowerMutation", source);
        Assert.Contains("setActiveGameFpsLimitEnabled", source);
        Assert.DoesNotContain("250", source);
        Assert.DoesNotContain("275", source);
        Assert.DoesNotContain("300", source);
    }

    [Fact]
    public void Qam_pending_tdp_drafts_restore_after_remount_and_old_commit_responses_cannot_rewind_new_edits()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        // Device: refresh() does NOT touch state.qamSliderCommits, so a pending draft survives a
        // same-page StateInvalidated; the rendered value checks the pending draft before row.value.
        var refresh = source[source.IndexOf("const refresh = React.useCallback(async () => {", StringComparison.Ordinal)..source.IndexOf("const beginMutation", StringComparison.Ordinal)];
        Assert.DoesNotContain("qamSliderCommits.delete", refresh);
        Assert.DoesNotContain("qamSliderCommits.set", refresh);
        Assert.Contains("const deviceRowEffectiveValue = row => deviceQuickSettingsPendingValue(row.rowId) ?? row.value;", source);
        // Legacy Profile pending draft restore is unchanged.
        Assert.Contains("const effectiveProfileDraft = state.qamSliderCommits?.get(\"profile-tdp\")?.draft ?? authoritativeProfileDraft", source);
        Assert.Contains("profileTdpDraftRef.current = effectiveProfileDraft", source);

        var scheduler = source[source.IndexOf("function scheduleQamSliderCommit", StringComparison.Ordinal)..source.IndexOf("// --- Shared Quick Settings Device helpers", StringComparison.Ordinal)];
        Assert.Contains("if (state.qamSliderCommits.get(key)?.token !== token) return;", scheduler);
        Assert.Contains("state.qamSliderCommits.delete(key);", scheduler);
        Assert.True(scheduler.IndexOf("const result = await request(method, payload)", StringComparison.Ordinal)
            < scheduler.IndexOf("state.qamSliderCommits.delete(key);", scheduler.IndexOf("const result = await request(method, payload)", StringComparison.Ordinal), StringComparison.Ordinal));
    }

    [Fact]
    public void Qam_invalidation_keeps_pending_cpu_preview_and_scopes_it_to_the_active_panel()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        var handlerStart = source.IndexOf("const handler = () =>", StringComparison.Ordinal);
        var handler = source[handlerStart..source.IndexOf("state.onStateInvalidated = handler", handlerStart, StringComparison.Ordinal)];
        // The invalidation handler only re-refreshes -- it never erases pending previews/drafts.
        Assert.DoesNotContain("setPreviewAc(null)", handler);
        Assert.DoesNotContain("setPreviewDc(null)", handler);
        Assert.DoesNotContain("setDevicePage(null)", handler);
        Assert.DoesNotContain("cancelQamSliderCommits", handler);
        Assert.Contains("void refresh();", handler);
    }

    [Fact]
    public void Qam_device_mutation_result_page_is_authoritative_on_success_and_failure()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        var apply = source[source.IndexOf("const applyDeviceQuickSettingsResult = result =>", StringComparison.Ordinal)..source.IndexOf("const commitDeviceImmediate", StringComparison.Ordinal)];
        // result.page always wins -- no optimistic rollback to the previous page on a typed failure.
        Assert.Contains("if (result?.page) { setDevicePage(result.page); devicePageRef.current = result.page; }", apply);
        Assert.Contains("setError(!result?.succeeded ? (result?.failureMessage || \"Device update failed\") : null);", apply);
        Assert.DoesNotContain("rollback", source);

        // The immediate path swallows its own self-triggered invalidation like the legacy path did.
        var immediate = source[source.IndexOf("const commitDeviceImmediate", StringComparison.Ordinal)..source.IndexOf("const scheduleDeviceQuickSettingsCommit", StringComparison.Ordinal)];
        Assert.Contains("applyDeviceQuickSettingsResult(result);", immediate);
        Assert.Contains("deferredInvalidationRef.current = false;", immediate);
        Assert.Contains("finally { endMutation(); setBusy(false); }", immediate);
    }

    [Fact]
    public void Qam_cpu_boost_panel_reuses_its_descriptor_and_retires_settled_mode_work()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("if (state.addonTabDescriptor) return state.addonTabDescriptor;", source);
        Assert.Contains("state.addonTabDescriptor = {", source);
        // Device row mutation is gated by shared writability + QAM surface admission, checked live.
        Assert.Contains("const canMutateDeviceRow = row => !unavailable && !!row.available && !!row.writable && !busy;", source);
        Assert.Contains("if (!state.installed || !canMutateDeviceRow(row)) return;", source);
        Assert.Contains("cancelQamSliderCommits();", source);
        Assert.Contains("retireBridgeConsumers", source);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
