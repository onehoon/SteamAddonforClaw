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

        Assert.Contains("cancelModeTimers();", enabledPath);
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
        Assert.Contains("marginTop: \"-4px\"", source);
        Assert.Contains("key: \"tdp-ac-pl2\", compact: true", source);
        Assert.Contains("key: \"tdp-dc-pl2\", compact: true", source);
        Assert.DoesNotContain("key: \"tdp-ac-pl1\", compact: true", source);
        Assert.DoesNotContain("key: \"tdp-dc-pl1\", compact: true", source);
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
        Assert.Contains("request(\"captureCpuBoost\")", source);
        Assert.Contains("request(\"setDeviceCpuBoostEnabled\"", source);
        Assert.Contains("request(side === \"ac\" ? \"setDeviceCpuBoostAc\" : \"setDeviceCpuBoostDc\"", source);
        var cpuModeMutation = source[source.IndexOf("const scheduleMode", StringComparison.Ordinal)..source.IndexOf("const setEnabled", StringComparison.Ordinal)];
        Assert.DoesNotContain("setBusy(true)", cpuModeMutation);
        Assert.DoesNotContain("setBusy(false)", cpuModeMutation);
        Assert.Contains("setTimeout(async () =>", cpuModeMutation);
        Assert.Contains("setPreviewAc", cpuModeMutation);
        Assert.Contains("setPreviewDc", cpuModeMutation);
        Assert.Contains("const modeEditGeneration = React.useRef({ ac: 0, dc: 0 })", source);
        Assert.Contains("const generation = ++modeEditGeneration.current[key]", source);
        Assert.Contains("generation === modeEditGeneration.current[key]", source);
        Assert.Contains("const modeEditPending", source);
        Assert.Contains("setTimeout(async () =>", source);
        Assert.Contains("state.onStateInvalidated", source);
        Assert.DoesNotContain("setInterval", source);
        Assert.Contains("request(\"captureTdp\")", source);
        Assert.Contains("cancelModeTimers();", source);
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
        Assert.Contains("request(\"setDeviceTdp\", { configuration: draft })", source);
        Assert.Contains("request(\"setDeviceTdpEnabled\", { enabled })", source);
        Assert.Contains("const tdpLimits = tdp?.limits", source);
        Assert.Contains("max: label === \"PL1\" ? limit.pl2MaximumWatts : limit.pl2MaximumWatts", source);
        Assert.Contains("step: 1", source);
        Assert.Contains("const adjustTdpPair", source);
        Assert.Contains("const generation = ++tdpEditGeneration.current", source);
        Assert.Contains("generation === tdpEditGeneration.current", source);
        Assert.Contains("setTimeout(() => { tdpTimer.current = null; void submitTdpDraft(nextDraft, generation); }, 300)", source);
        Assert.Contains("Keep a dirty TDP draft's debounce alive across invalidation", source);
        var tdpSubmit = source[source.IndexOf("const submitTdpDraft", StringComparison.Ordinal)..source.IndexOf("const scheduleTdp", StringComparison.Ordinal)];
        Assert.DoesNotContain("setBusy(true)", tdpSubmit);
        Assert.DoesNotContain("setBusy(false)", tdpSubmit);
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
        Assert.Contains("profileTdpGeneration.current = 0", source);
        Assert.Contains("labelFor(preview ?? value)", source);
        Assert.Contains("profile-tdp-ac-heading", source);
        Assert.Contains("profile-tdp-dc-heading", source);
        Assert.Contains("disabled: !profile.persistenceWritable || !enabled", source);
        Assert.Contains("profile.cpuBoost?.ac", source);
        Assert.Contains("profileTdpDraft?.dc?.pl2Watts", source);
        Assert.Contains("profile-fps-section", source);
        Assert.Contains("Intel FPS Limit", source);
        Assert.Contains("min: 40, max: 120, step: 1", source);
        Assert.Contains("${value} FPS", source);
        Assert.Contains("setActiveGameFpsLimitAc", source);
        Assert.Contains("setActiveGameFpsLimitDc", source);
        Assert.Contains("await runFpsMutation(side === \"ac\" ? \"setActiveGameFpsLimitAc\" : \"setActiveGameFpsLimitDc\"", source);
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
        Assert.True(profileStart >= 0 && tdpSection > profileStart);
        var profileLayout = source[profileStart..tdpSection];

        Assert.Contains("const profilePowerControls", profileLayout);
        Assert.Contains("key: \"profile-power-section\", title: \"Windows Power Mode\"", profileLayout);
        Assert.Contains("key: \"profile-cpu-section\", title: \"CPU Boost\"", profileLayout);
        var cpuSection = profileLayout.IndexOf("key: \"profile-cpu-section\"", StringComparison.Ordinal);
        var powerSection = profileLayout.IndexOf("key: \"profile-power-section\"", StringComparison.Ordinal);
        var cpuLayout = profileLayout[cpuSection..powerSection];
        Assert.Contains("profileCpuControls.filter", cpuLayout);
        Assert.DoesNotContain("profilePowerControls.filter", cpuLayout);
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
        Assert.Contains("const cancelModeTimers = React.useCallback", source);
        Assert.Contains("cancelModeTimers();", source);
        Assert.Contains("settleTimers.current[key] = null;", source);
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
