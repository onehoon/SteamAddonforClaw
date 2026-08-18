using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMBackendProbeTests
{
    [Fact]
    public void Capture_reports_presence_and_pid_for_each_backend_process()
    {
        var processes = new FakeProcessSnapshotSource();
        processes.Set(CenterMProcessNames.Launcher, new ProcessSnapshotEntry(11, CenterMProcessNames.Launcher, null));
        processes.Set(CenterMProcessNames.Server, new ProcessSnapshotEntry(22, CenterMProcessNames.Server, null));
        // ControlMode left absent.

        var snapshot = new CenterMBackendProbe(processes).Capture();

        Assert.True(snapshot.LauncherPresent);
        Assert.Equal(11, snapshot.LauncherProcessId);
        Assert.True(snapshot.ServerPresent);
        Assert.Equal(22, snapshot.ServerProcessId);
        Assert.False(snapshot.ControlModePresent);
        Assert.Null(snapshot.ControlModeProcessId);
    }

    [Fact]
    public void Capture_reports_all_absent_when_no_backend_processes_exist()
    {
        var snapshot = new CenterMBackendProbe(new FakeProcessSnapshotSource()).Capture();

        Assert.False(snapshot.LauncherPresent);
        Assert.False(snapshot.ServerPresent);
        Assert.False(snapshot.ControlModePresent);
    }

    private sealed class FakeProcessSnapshotSource : IProcessSnapshotSource
    {
        private readonly Dictionary<string, ProcessSnapshotEntry> _entries = [];
        internal void Set(string name, ProcessSnapshotEntry entry) => _entries[name] = entry;

        public IReadOnlyList<ProcessSnapshotEntry> GetProcessesByName(string processName) =>
            _entries.TryGetValue(processName, out var entry) ? [entry] : [];
    }
}
