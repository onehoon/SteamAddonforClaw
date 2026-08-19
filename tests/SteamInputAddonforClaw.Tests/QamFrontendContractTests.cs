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
        Assert.DoesNotContain("QamHost stop requested before installation completed.", program[..program.IndexOf("installationSucceeded = true", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Nested_react_walker_traverses_props_children_child_and_sibling()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.QamHost", "Frontend", "qam.js");

        Assert.Contains("node.props && node.props.children", source);
        Assert.Contains("if (children != null) stack.push(children);", source);
        Assert.Contains("if (node.child != null) stack.push(node.child);", source);
        Assert.Contains("if (node.sibling != null) stack.push(node.sibling);", source);
        Assert.Contains("const visited = new Set();", source);
        Assert.Contains("REACT_WALK_NODE_BUDGET", source);
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
        Assert.Contains("if (record.node?.type === record.patchedType) {", source);
        Assert.Contains("record.node.type = record.originalType;", source);
        Assert.Contains("state.nestedPatches ??= new Map();", source);
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
