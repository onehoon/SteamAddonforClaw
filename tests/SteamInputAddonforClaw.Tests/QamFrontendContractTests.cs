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
        Assert.Contains("title: \"Steam Input Addon\"", source);
        Assert.Contains("fill: \"currentColor\"", source);
        Assert.Contains("[0, \"Disabled\"]", source);
        Assert.Contains("[1, \"Enabled\"]", source);
        Assert.Contains("[2, \"Aggressive\"]", source);
        Assert.Contains("[3, \"Efficient Enabled\"]", source);
        Assert.Contains("[4, \"Efficient Aggressive\"]", source);
        Assert.Contains("[5, \"Aggressive at Guaranteed\"]", source);
        Assert.Contains("[6, \"Efficient Aggressive at Guaranteed\"]", source);
        Assert.Contains("request(\"captureStatus\")", source);
        Assert.Contains("request(\"captureCpuBoost\")", source);
        Assert.Contains("request(\"setDeviceCpuBoostEnabled\"", source);
        Assert.Contains("request(side === \"ac\" ? \"setDeviceCpuBoostAc\" : \"setDeviceCpuBoostDc\"", source);
        Assert.Contains("setTimeout(async () =>", source);
        Assert.Contains("state.onStateInvalidated", source);
        Assert.DoesNotContain("setInterval", source);
        Assert.DoesNotContain("captureTdp", source[source.IndexOf("function buildAddonTab", StringComparison.Ordinal)..]);
        Assert.Contains("cancelModeTimers();", source);
        Assert.Contains("setPreviewAc(null); setPreviewDc(null);", source);
        Assert.Contains("state.onStateInvalidated === handler", source);
        Assert.Contains("style: { width: \"100%\" }", source);
        Assert.Contains("\"aria-hidden\": \"true\"", source);
        Assert.Contains("modes.map(([mode])", source);
        Assert.Contains("const mutationAvailable", source);
        Assert.Contains("const modeWritable = mutationAvailable && cpu.enabled", source);
        Assert.Contains("disabled: !modeWritable || value == null", source);
        Assert.Contains("const failClosed", source);
        Assert.Contains("setStatus(null); setCpu(null); setPreviewAc(null); setPreviewDc(null)", source);
        Assert.Contains("cpu.lastFailure", source);
        Assert.Contains("CPU Boost settings could not be loaded, so changes are disabled.", source);
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
