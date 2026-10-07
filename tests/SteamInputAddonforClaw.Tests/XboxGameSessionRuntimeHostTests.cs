using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class XboxGameSessionRuntimeHostTests
{
    [Fact]
    public void Normal_runtime_owns_and_starts_the_xbox_session_after_the_headless_uninstall_guard()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs").Replace("\r\n", "\n", StringComparison.Ordinal);
        var runtimeCreation = host.IndexOf("_xboxGameSessionRuntime = new XboxGameSessionRuntime()", StringComparison.Ordinal);
        var headlessGuard = host.IndexOf("if (_headlessUninstallPreparation)\n            return;", StringComparison.Ordinal);
        var frontendCreation = host.IndexOf("_frontendControl = new SteamInputAddonforClaw.Frontend.InProcessAddonFrontendControl", StringComparison.Ordinal);
        Assert.True(runtimeCreation > headlessGuard);
        Assert.True(frontendCreation > runtimeCreation);

        var sessionStartup = host[runtimeCreation..frontendCreation];
        Assert.Contains("await _xboxGameSessionRuntime.StartAsync(_startupCancellationTokenSource.Token)", sessionStartup, StringComparison.Ordinal);
        Assert.Contains("Production XBOX game-session observer could not start; Runtime will continue", sessionStartup, StringComparison.Ordinal);
        Assert.DoesNotContain("CenterMStartupState", sessionStartup, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindow", sessionStartup, StringComparison.Ordinal);
        Assert.DoesNotContain("Steam", sessionStartup, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_and_shutdown_target_the_production_owner_without_frontend_or_controller_coupling()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs").Replace("\r\n", "\n", StringComparison.Ordinal);
        var resumeStart = host.IndexOf("private void OnPowerResumeObserved()", StringComparison.Ordinal);
        var resumeEnd = host.IndexOf("internal static async Task ReconcilePerformanceAfterResumeAsync", resumeStart, StringComparison.Ordinal);
        var resume = host[resumeStart..resumeEnd];
        Assert.Contains("xboxGameSessionRuntime.ReconcileAfterResumeAsync()", resume, StringComparison.Ordinal);

        var disposeStart = host.IndexOf("public async ValueTask DisposeAsync()", StringComparison.Ordinal);
        var disposeEnd = host.IndexOf("private async Task StopClawHudForProcessShutdownAsync", disposeStart, StringComparison.Ordinal);
        var dispose = host[disposeStart..disposeEnd];
        Assert.Contains("_xboxGameSessionRuntime.DisposeAsync()", dispose, StringComparison.Ordinal);
        Assert.True(dispose.IndexOf("_xboxGameSessionRuntime.DisposeAsync()", StringComparison.Ordinal)
            < dispose.IndexOf("_runtimeHost.DisposeAsync()", StringComparison.Ordinal));
        Assert.DoesNotContain("StopXboxSessionDiagnostic", ReadSource("src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs"), StringComparison.Ordinal);

        var runtime = ReadSource("src/SteamInputAddonforClaw/Xbox/Session/XboxGameSessionRuntime.cs");
        Assert.DoesNotContain("RequestControllerPresentationReconcile", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("CpuBoostRuntime", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("TdpRuntime", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileStore", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Active_XBOX_game_changes_reconcile_mapping_before_the_performance_startup_gate()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs").Replace("\r\n", "\n", StringComparison.Ordinal);
        var handlerStart = host.IndexOf("private void OnActiveXboxGameChanged(", StringComparison.Ordinal);
        var handlerEnd = host.IndexOf("internal static void ReconcileActiveXboxGameTransition(", handlerStart, StringComparison.Ordinal);
        var handler = host[handlerStart..handlerEnd];
        var transitionEnd = host.IndexOf("private bool ReconcileEffectiveBackButtonMapping(", handlerEnd, StringComparison.Ordinal);
        var transition = host[handlerEnd..transitionEnd];

        Assert.Contains("ReconcileActiveXboxGameTransition(", handler, StringComparison.Ordinal);
        Assert.Contains("() => ReconcileEffectiveBackButtonMapping(\"XboxActiveGameChanged\")", handler, StringComparison.Ordinal);
        Assert.Contains("() => ReconcileEffectiveGameProfile(\"ActiveXboxGameChanged\")", handler, StringComparison.Ordinal);
        Assert.True(transition.IndexOf("reconcileBackButtonMapping();", StringComparison.Ordinal)
            < transition.IndexOf("if (!profileRuntimeStartupReady) return;", StringComparison.Ordinal));
        Assert.True(transition.IndexOf("if (!profileRuntimeStartupReady) return;", StringComparison.Ordinal)
            < transition.IndexOf("reconcileGameProfile();", StringComparison.Ordinal));
    }

    [Fact]
    public void Active_XBOX_display_name_enrichment_reads_only_the_matching_live_session()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs");
        var frontend = ReadSource("src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs");

        Assert.Contains("activeXboxDisplayNameSource: key => _xboxGameSessionRuntime?.ActiveGame is { } activeGame", host, StringComparison.Ordinal);
        Assert.Contains("string.Equals(activeGame.Key, key, StringComparison.Ordinal)", host, StringComparison.Ordinal);
        Assert.Contains("? activeGame.DisplayName", host, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrWhiteSpace(displayName))", frontend, StringComparison.Ordinal);
        Assert.Contains("displayName = _activeXboxDisplayNameSource?.Invoke(key);", frontend, StringComparison.Ordinal);
    }

    [Fact]
    public void Frontend_contract_removes_diagnostic_rpc_and_advances_only_frontend_protocol()
    {
        var wire = ReadSource("src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs");
        var contracts = ReadSource("src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs");
        var server = ReadSource("src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs");
        var client = ReadSource("src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs");
        var control = ReadSource("src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs");

        Assert.Contains("CurrentVersion = 61", wire, StringComparison.Ordinal);
        Assert.Contains("Version 58:", wire, StringComparison.Ordinal);
        Assert.Contains("Version 59:", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureXboxSessionDiagnostic", wire + contracts + server + client + control, StringComparison.Ordinal);
        Assert.DoesNotContain("StartXboxSessionDiagnostic", wire + contracts + server + client + control, StringComparison.Ordinal);
        Assert.DoesNotContain("StopXboxSessionDiagnostic", wire + contracts + server + client + control, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateXboxSessionDiagnosticReport", wire + contracts + server + client + control, StringComparison.Ordinal);
        Assert.Contains("CurrentVersion = 17", ReadSource("src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs"), StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
