using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Review fix (MAJOR): Local\MSI Center M.exe is a session-local suppression primitive, so
/// the OEM1 lifecycle's process observation must be scoped to this Addon's own Windows session --
/// a same-name process in a foreign session must never become its real-MainUI authority.</summary>
public sealed class CurrentSessionCenterMProcessSnapshotSourceTests
{
    [Fact]
    public void Same_session_candidates_are_kept()
    {
        var inner = new FakeSnapshotSource([Entry(1), Entry(2)]);
        var scope = new FakeScopeInspector { InCurrentSession = { [1] = true, [2] = true } };
        var source = new CurrentSessionCenterMProcessSnapshotSource(inner, scope);

        var result = source.GetProcessesByName("MSI Center M");

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Foreign_session_candidates_are_dropped()
    {
        var inner = new FakeSnapshotSource([Entry(1), Entry(2)]);
        var scope = new FakeScopeInspector { InCurrentSession = { [1] = true, [2] = false } };
        var source = new CurrentSessionCenterMProcessSnapshotSource(inner, scope);

        var result = source.GetProcessesByName("MSI Center M");

        Assert.NotNull(result);
        Assert.Equal(1, Assert.Single(result).ProcessId);
    }

    [Fact]
    public void Unknown_scope_for_any_candidate_fails_open_to_uncertain()
    {
        var inner = new FakeSnapshotSource([Entry(1), Entry(2)]);
        var scope = new FakeScopeInspector { InCurrentSession = { [1] = true, [2] = null } };
        var source = new CurrentSessionCenterMProcessSnapshotSource(inner, scope);

        var result = source.GetProcessesByName("MSI Center M");

        Assert.Null(result);
    }

    [Fact]
    public void Inner_enumeration_uncertainty_propagates_without_querying_scope()
    {
        var inner = new FakeSnapshotSource(null);
        var scope = new FakeScopeInspector();
        var source = new CurrentSessionCenterMProcessSnapshotSource(inner, scope);

        var result = source.GetProcessesByName("MSI Center M");

        Assert.Null(result);
        Assert.Empty(scope.QueriedProcessIds);
    }

    [Fact]
    public void No_candidates_returns_an_empty_confident_result()
    {
        var inner = new FakeSnapshotSource([]);
        var scope = new FakeScopeInspector();
        var source = new CurrentSessionCenterMProcessSnapshotSource(inner, scope);

        var result = source.GetProcessesByName("MSI Center M");

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    private static ProcessSnapshotEntry Entry(int processId) => new(processId, "MSI Center M", null);

    private sealed class FakeSnapshotSource(IReadOnlyList<ProcessSnapshotEntry>? entries) : IProcessSnapshotSource
    {
        public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName) => entries;
    }

    private sealed class FakeScopeInspector : ICenterMSessionScopeInspector
    {
        internal Dictionary<int, bool?> InCurrentSession { get; } = [];
        internal List<int> QueriedProcessIds { get; } = [];

        public bool? IsInCurrentSession(int processId)
        {
            QueriedProcessIds.Add(processId);
            return InCurrentSession.TryGetValue(processId, out var value) ? value : null;
        }
    }
}
