using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMProcessScopeTests
{
    private const string ExpectedPath = @"C:\Program Files\WindowsApps\9426MICRO-STARINTERNATION.64797CC12EF8E_3.0.60630.0_x64__kzh8wxbdkxb8p\MSI Center M\MSI Center M.exe";

    [Fact]
    public void Foreign_session_process_is_excluded_without_being_treated_as_uncertain()
    {
        var inner = new FakeProcessSnapshotSource([new ProcessSnapshotEntry(4242, "MSI Center M", ExpectedPath)]);
        var scope = new FakeScopeInspector(new() { [4242] = ProcessScopeProbeStatus.Foreign });
        var scoped = new ScopedCenterMProcessSnapshotSource(inner, scope);

        var result = scoped.GetProcessesByName("MSI Center M");

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Same_session_same_user_process_is_kept()
    {
        var inner = new FakeProcessSnapshotSource([new ProcessSnapshotEntry(4242, "MSI Center M", ExpectedPath)]);
        var scope = new FakeScopeInspector(new() { [4242] = ProcessScopeProbeStatus.Match });
        var scoped = new ScopedCenterMProcessSnapshotSource(inner, scope);

        var result = scoped.GetProcessesByName("MSI Center M");

        Assert.NotNull(result);
        Assert.Equal([new ProcessSnapshotEntry(4242, "MSI Center M", ExpectedPath)], result);
    }

    [Fact]
    public void Current_session_candidate_plus_foreign_session_candidate_keeps_only_the_current_one()
    {
        var inner = new FakeProcessSnapshotSource(
        [
            new ProcessSnapshotEntry(4242, "MSI Center M", ExpectedPath),
            new ProcessSnapshotEntry(9999, "MSI Center M", ExpectedPath)
        ]);
        var scope = new FakeScopeInspector(new()
        {
            [4242] = ProcessScopeProbeStatus.Match,
            [9999] = ProcessScopeProbeStatus.Foreign
        });
        var scoped = new ScopedCenterMProcessSnapshotSource(inner, scope);

        var result = scoped.GetProcessesByName("MSI Center M");

        Assert.NotNull(result);
        Assert.Equal([new ProcessSnapshotEntry(4242, "MSI Center M", ExpectedPath)], result);
    }

    [Fact]
    public void Exited_candidate_is_excluded_like_foreign()
    {
        var inner = new FakeProcessSnapshotSource([new ProcessSnapshotEntry(4242, "MSI Center M", ExpectedPath)]);
        var scope = new FakeScopeInspector(new() { [4242] = ProcessScopeProbeStatus.Exited });
        var scoped = new ScopedCenterMProcessSnapshotSource(inner, scope);

        var result = scoped.GetProcessesByName("MSI Center M");

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Uncertain_scope_for_any_candidate_fails_the_entire_snapshot_closed()
    {
        var inner = new FakeProcessSnapshotSource(
        [
            new ProcessSnapshotEntry(4242, "MSI Center M", ExpectedPath),
            new ProcessSnapshotEntry(9999, "MSI Center M", ExpectedPath)
        ]);
        // The first candidate is a clean Match, but the second's scope cannot be established --
        // the entire result must become uncertain rather than silently excluding/including the
        // ambiguous one.
        var scope = new FakeScopeInspector(new()
        {
            [4242] = ProcessScopeProbeStatus.Match,
            [9999] = ProcessScopeProbeStatus.Uncertain
        });
        var scoped = new ScopedCenterMProcessSnapshotSource(inner, scope);

        var result = scoped.GetProcessesByName("MSI Center M");

        Assert.Null(result);
    }

    [Fact]
    public void Inner_enumeration_uncertainty_propagates_without_ever_calling_the_scope_inspector()
    {
        var inner = new FakeProcessSnapshotSource(null);
        var scope = new FakeScopeInspector(new());
        var scoped = new ScopedCenterMProcessSnapshotSource(inner, scope);

        var result = scoped.GetProcessesByName("MSI Center M");

        Assert.Null(result);
        Assert.Equal(0, scope.CallCount);
    }

    [Fact]
    public void Retained_scope_inspector_classifies_current_process_as_match()
    {
        // Exercises the REAL Win32CenterMRetainedProcessScopeInspector (not a fake) against a real
        // handle to this test process itself -- the current process trivially matches its own
        // session/user, so this deterministically catches P/Invoke ABI regressions (e.g. an
        // incorrect GetProcessId signature) that a fake-based test cannot.
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        const uint SYNCHRONIZE = 0x00100000;
        using var handle = new Win32ProcessHandleOpener().Open(Environment.ProcessId, PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE);
        Assert.False(handle.IsInvalid);

        var inspector = new Win32CenterMRetainedProcessScopeInspector();
        var result = inspector.Inspect(handle);

        Assert.Equal(ProcessScopeProbeStatus.Match, result);
    }

    private sealed class FakeProcessSnapshotSource(IReadOnlyList<ProcessSnapshotEntry>? entries) : IProcessSnapshotSource
    {
        public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName) => entries;
    }

    private sealed class FakeScopeInspector(Dictionary<int, ProcessScopeProbeStatus> results) : ICenterMProcessScopeInspector
    {
        internal int CallCount { get; private set; }

        public ProcessScopeProbeStatus Inspect(int processId)
        {
            CallCount++;
            return results.GetValueOrDefault(processId, ProcessScopeProbeStatus.Uncertain);
        }
    }
}
