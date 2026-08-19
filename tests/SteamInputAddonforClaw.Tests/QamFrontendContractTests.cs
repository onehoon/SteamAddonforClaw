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

        // Discovery predicate: only the lifecycle prop gates matching, not typeof candidate.type.
        var patchTabsProducerIndex = source.IndexOf("function patchTabsProducer", StringComparison.Ordinal);
        Assert.True(patchTabsProducerIndex >= 0);
        var findReactNodeCallIndex = source.IndexOf("findReactNode(", patchTabsProducerIndex, StringComparison.Ordinal);
        var predicateEndIndex = source.IndexOf(");", findReactNodeCallIndex, StringComparison.Ordinal);
        var predicateSlice = source[findReactNodeCallIndex..predicateEndIndex];

        Assert.Contains("candidate.props && typeof candidate.props.onFocusNavDeactivated === \"function\"", predicateSlice);
        Assert.DoesNotContain("typeof candidate.type === \"function\"", predicateSlice);
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
