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
    public void Qam_bridge_has_one_device_read_path_through_the_shared_aggregate()
    {
        var bridge = ReadSource("src", "SteamInputAddonforClaw.QamHost", "QamFrontendBridge.cs");

        Assert.Contains("\"captureDeviceQuickSettings\" => await _client.CaptureDeviceQuickSettingsAsync(token),", bridge);
        Assert.DoesNotContain("\"captureCpuBoost\"", bridge);
        Assert.DoesNotContain("\"captureTdp\"", bridge);
        Assert.DoesNotContain("\"capturePowerMode\"", bridge);
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
    public void Qam_enabled_mutation_is_retired_with_the_installed_panel_and_clears_mode_previews()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");
        var enabledIndex = source.IndexOf("const setEnabled = async value =>", StringComparison.Ordinal);
        Assert.True(enabledIndex >= 0);
        var enabledPath = source[enabledIndex..];

        Assert.Contains("cancelQamSliderCommits(key => key.startsWith(\"device-cpu-\"));", enabledPath);
        Assert.Contains("setPreviewAc(null); setPreviewDc(null);", enabledPath);
        Assert.Contains("if (!state.installed) return;", enabledPath);
        Assert.Contains("request(\"setDeviceCpuBoostEnabled\"", enabledPath);
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
    public void Qam_cpu_boost_panel_uses_the_existing_seven_mode_contract_and_bridge_allowlist()
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
        Assert.Contains("[0, \"Disabled\"]", source);
        Assert.Contains("[1, \"Enabled\"]", source);
        Assert.Contains("[2, \"Aggressive\"]", source);
        Assert.Contains("[3, \"Efficient Enabled\"]", source);
        Assert.Contains("[4, \"Efficient Aggressive\"]", source);
        Assert.Contains("[5, \"Aggressive At Guaranteed\"]", source);
        Assert.Contains("[6, \"Efficient Aggressive At Guaranteed\"]", source);
        Assert.Contains("Plugged in", source);
        Assert.Contains("On battery", source);
        Assert.DoesNotContain("AC Mode", source);
        Assert.DoesNotContain("DC Mode", source);
        Assert.Contains("request(\"captureStatus\")", source);
        Assert.Contains("request(\"captureDeviceQuickSettings\")", source);
        Assert.DoesNotContain("request(\"captureCpuBoost\")", source);
        Assert.Contains("request(\"setDeviceCpuBoostEnabled\"", source);
        Assert.Contains("scheduleQamSliderCommit(`device-cpu-${key}`", source);
        var cpuModeMutation = source[source.IndexOf("const scheduleMode", StringComparison.Ordinal)..source.IndexOf("const setEnabled", StringComparison.Ordinal)];
        Assert.DoesNotContain("setBusy(true)", cpuModeMutation);
        Assert.DoesNotContain("setBusy(false)", cpuModeMutation);
        Assert.Contains("scheduleQamSliderCommit", cpuModeMutation);
        Assert.Contains("setPreviewAc", cpuModeMutation);
        Assert.Contains("setPreviewDc", cpuModeMutation);
        Assert.Contains("const QAM_SLIDER_COMMIT_DELAY_MS = 2000", source);
        Assert.Contains("function scheduleQamSliderCommit", source);
        Assert.Contains("setTimeout(async () =>", source);
        Assert.Contains("state.onStateInvalidated", source);
        Assert.DoesNotContain("setInterval", source);
        Assert.DoesNotContain("request(\"captureTdp\")", source);
        Assert.DoesNotContain("request(\"capturePowerMode\")", source);
        Assert.Contains("cancelQamSliderCommits", source);
        Assert.Contains("setPreviewAc(null); setPreviewDc(null);", source);
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
        Assert.DoesNotContain("ToggleField\\\\\\\",", source);
        Assert.DoesNotContain("SliderField\\\\\\\",", source);
        Assert.DoesNotContain("findNativeComponent", source);
        Assert.DoesNotContain("findUniqueNativeComponent", source);
        Assert.DoesNotContain("requiredProps", source);
        Assert.Contains("Steam CommonUIModule unavailable.", source);
        Assert.Contains("native ToggleField unavailable", source);
        Assert.Contains("native SliderField unavailable", source);
        Assert.Contains("state.installFailureKind = \"native-components\"", source);
        Assert.Contains("native.ToggleField", source);
        Assert.Contains("native.SliderField", source);
        Assert.Contains("notchCount: modes.length", source);
        Assert.Contains("notchTicksVisible: true", source);
        Assert.DoesNotContain("numericNotches", source);
        Assert.DoesNotContain("notchLabels", source);
        var cpuSliderStart = source.LastIndexOf("const slider =", StringComparison.Ordinal);
        var cpuSlider = source[cpuSliderStart..source.IndexOf("const controls =", cpuSliderStart, StringComparison.Ordinal)];
        Assert.DoesNotContain("showValue: true", cpuSlider);
        Assert.DoesNotContain("description: labelFor(value)", source);
        Assert.DoesNotContain("description: `${value} W`", source);
        Assert.Contains("mutationDepthRef", source);
        Assert.Contains("deferredInvalidationRef", source);
        Assert.Contains("beginMutation", source);
        Assert.Contains("endMutation", source);
        Assert.Contains("key: \"cpu-plugged-in\"", source);
        Assert.Contains("key: \"cpu-on-battery\"", source);
        Assert.Contains("key: \"tdp-ac-pl1\"", source);
        Assert.Contains("key: \"tdp-dc-pl2\"", source);
        Assert.DoesNotContain("cpu-row-${index}", source);
        Assert.DoesNotContain("tdp-row-${index}", source);
        Assert.Contains("bottomSeparator: cpu?.enabled ? \"none\" : \"standard\"", source);
        Assert.Contains("bottomSeparator,", source);
        Assert.Contains("\"standard\")", source);
        Assert.Contains("if (cpu?.enabled)", source);
        Assert.DoesNotContain("type: \"checkbox\"", source);
        Assert.DoesNotContain("type: \"range\"", source);
        Assert.DoesNotContain("setInterval", source);
        Assert.DoesNotContain("fontFamily: \"sans-serif\"", source);
        Assert.Contains("const mutationAvailable", source);
        Assert.Contains("const modeWritable = mutationAvailable && cpu.enabled", source);
        Assert.Contains("disabled: !modeWritable", source);
        Assert.DoesNotContain("value: value == null ? 0 : value", source);
        Assert.Contains("const failClosed", source);
        Assert.Contains("setStatus(null); setCpu(null); setPowerMode(null); setTdp(null); setProfile(null); profileTdpDraftRef.current = null", source);
        Assert.Contains("cpu.lastFailure", source);
        Assert.Contains("CPU Boost settings could not be loaded, so changes are disabled.", source);
        Assert.Contains("QAM required native controls/layout unavailable", source);
        Assert.Contains("const powerWritable", source);
        Assert.Contains("const powerInitialized = powerMode?.ac?.desired != null && powerMode?.dc?.desired != null;", source);
        Assert.Contains("powerInitialized && !status?.steam?.appId", source);
        Assert.Contains("const runPowerMutation", source);
        Assert.Contains("Power Mode update failed", source);
        Assert.Contains("Best power efficiency", source);
        Assert.Contains("Best performance", source);
    }

    [Fact]
    public void Qam_tdp_panel_projects_existing_device_contract_without_new_policy_or_polling()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("label: \"TDP Control\"", source);
        Assert.Contains("scheduleQamSliderCommit(\"device-tdp\"", source);
        Assert.Contains("request(\"setDeviceTdpEnabled\", { enabled })", source);
        Assert.Contains("const tdpLimits = tdp?.limits", source);
        Assert.Contains("max: label === \"PL1\" ? limit.pl2MaximumWatts : limit.pl2MaximumWatts", source);
        Assert.Contains("step: 1", source);
        Assert.Contains("const adjustTdpPair", source);
        Assert.Contains("scheduleQamSliderCommit(\"device-tdp\"", source);
        Assert.Contains("scheduleQamSliderCommit(\"profile-tdp\"", source);
        Assert.Contains("QAM_SLIDER_COMMIT_DELAY_MS", source);
        Assert.Contains("if (tdpDraft?.enabled && tdpLimits)", source);
        Assert.DoesNotContain("setTdpAcPl1", source);
        Assert.DoesNotContain("setTdpAcPl2", source);
        Assert.DoesNotContain("setTdpDcPl1", source);
        Assert.DoesNotContain("setTdpDcPl2", source);
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

        Assert.Contains("const nextDevice = activeGame ? null : await request(\"captureDeviceQuickSettings\");", refresh);
        Assert.Contains("const nextCpu = nextDevice?.cpuBoost ?? null;", refresh);
        Assert.Contains("const nextPowerMode = nextDevice?.powerMode ?? null;", refresh);
        Assert.Contains("const nextTdp = nextDevice?.tdp ?? null;", refresh);
        Assert.DoesNotContain("captureCpuBoost", refresh);
        Assert.DoesNotContain("capturePowerMode", refresh);
        Assert.DoesNotContain("\"captureTdp\"", refresh);
        // Status/active Profile stay their own separate reads (work order section 11.3).
        Assert.Contains("await request(\"captureStatus\")", refresh);
        Assert.Contains("await request(\"captureActiveGameProfile\")", refresh);
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

        Assert.Contains("const QAM_SLIDER_COMMIT_DELAY_MS = 2000", source);
        Assert.Contains("state.qamSliderCommits", source);
        Assert.Contains("clearTimeout(pending.timer)", source);
        Assert.Contains("cancelQamSliderCommits();", source);
        Assert.Contains("scheduleQamSliderCommit(`profile-fps-${side}`", source);
        Assert.Contains("\"device-power-ac\"", source);
        var powerSchedule = source[source.IndexOf("const schedulePowerMode", StringComparison.Ordinal)..source.IndexOf("const powerSlider", StringComparison.Ordinal)];
        Assert.Contains("setPowerPreview(current => ({ ...current, [key]: value }))", powerSchedule);
        Assert.True(powerSchedule.IndexOf("setPowerPreview", StringComparison.Ordinal)
            < powerSchedule.IndexOf("scheduleQamSliderCommit", StringComparison.Ordinal));
        var powerSlider = source[source.IndexOf("const powerSlider", StringComparison.Ordinal)..source.IndexOf("const powerControls", StringComparison.Ordinal)];
        Assert.Contains("schedulePowerMode", powerSlider);
        Assert.Contains("powerPreview[key] ?? pendingValue ?? value", powerSlider);
        Assert.DoesNotContain("runPowerMutation", powerSlider);
        Assert.True(powerSchedule.IndexOf("await refresh();", StringComparison.Ordinal)
            < powerSchedule.IndexOf("delete next[key]", StringComparison.Ordinal));
        Assert.Contains("setDevicePowerModeEnabled", source);
        Assert.Contains("setActiveGameFpsLimitEnabled", source);
        Assert.DoesNotContain("250", source);
        Assert.DoesNotContain("275", source);
        Assert.DoesNotContain("300", source);
    }

    [Fact]
    public void Qam_pending_tdp_drafts_restore_after_remount_and_old_commit_responses_cannot_rewind_new_edits()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("const effectiveDeviceDraft = state.qamSliderCommits?.get(\"device-tdp\")?.draft ?? nextDraft", source);
        Assert.Contains("const effectiveProfileDraft = state.qamSliderCommits?.get(\"profile-tdp\")?.draft ?? authoritativeProfileDraft", source);
        Assert.Contains("tdpDraftRef.current = effectiveDeviceDraft", source);
        Assert.Contains("profileTdpDraftRef.current = effectiveProfileDraft", source);

        var scheduler = source[source.IndexOf("function scheduleQamSliderCommit", StringComparison.Ordinal)..source.IndexOf("function receiveBridgeResponse", StringComparison.Ordinal)];
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
        Assert.DoesNotContain("setPreviewAc(null)", handler);
        Assert.DoesNotContain("setPreviewDc(null)", handler);
        Assert.Contains("const cpuScope = activeGame ? \"profile\" : \"device\"", source);
        Assert.Contains("`${cpuScope}-cpu-ac`", source);
        Assert.Contains("`${cpuScope}-cpu-dc`", source);
    }

    [Fact]
    public void Qam_power_mutation_refresh_preserves_explicit_failure()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        var mutationStart = source.IndexOf("const runPowerMutation", StringComparison.Ordinal);
        var mutation = source[mutationStart..source.IndexOf("React.useEffect", mutationStart, StringComparison.Ordinal)];

        Assert.Contains("const failure = !result?.succeeded", mutation);
        Assert.Contains("await refresh();", mutation);
        Assert.Contains("deferredInvalidationRef.current = false;", mutation);
        Assert.Contains("if (failure) setError(failure);", mutation);
        Assert.True(mutation.IndexOf("await refresh();", StringComparison.Ordinal)
            < mutation.IndexOf("if (failure) setError(failure);", StringComparison.Ordinal));
    }

    [Fact]
    public void Qam_cpu_boost_panel_reuses_its_descriptor_and_retires_settled_mode_work()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("if (state.addonTabDescriptor) return state.addonTabDescriptor;", source);
        Assert.Contains("state.addonTabDescriptor = {", source);
        Assert.Contains("const modeWritableRef = React.useRef(false);", source);
        Assert.Contains("modeWritableRef.current = modeWritable;", source);
        Assert.Contains("if (!state.installed || !modeWritableRef.current) return;", source);
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
