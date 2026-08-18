namespace SteamInputAddonforClaw.CenterM;

internal readonly record struct CenterMBackendSnapshot(
    bool LauncherPresent, int? LauncherProcessId,
    bool ServerPresent, int? ServerProcessId,
    bool ControlModePresent, int? ControlModeProcessId);

/// <summary>Read-only presence probe for the Center M backend processes (Launcher/Server/
/// ControlMode). Never terminates, suspends, or otherwise mutates these processes -- they are
/// prerequisites this feature observes, not something it owns.</summary>
internal sealed class CenterMBackendProbe(IProcessSnapshotSource? processes = null)
{
    private readonly IProcessSnapshotSource _processes = processes ?? new Win32ProcessSnapshotSource();

    internal CenterMBackendSnapshot Capture()
    {
        // Enumeration uncertainty (null) is treated as "not confidently present" here -- the same
        // fail-closed direction as an empty result, since this snapshot only has a presence/PID
        // shape to report through, and a prerequisite that cannot be confirmed present must not be
        // treated as ready.
        var launcher = _processes.GetProcessesByName(CenterMProcessNames.Launcher) ?? [];
        var server = _processes.GetProcessesByName(CenterMProcessNames.Server) ?? [];
        var controlMode = _processes.GetProcessesByName(CenterMProcessNames.ServerControlMode) ?? [];

        return new CenterMBackendSnapshot(
            launcher.Count > 0, launcher.Count > 0 ? launcher[0].ProcessId : null,
            server.Count > 0, server.Count > 0 ? server[0].ProcessId : null,
            controlMode.Count > 0, controlMode.Count > 0 ? controlMode[0].ProcessId : null);
    }
}
