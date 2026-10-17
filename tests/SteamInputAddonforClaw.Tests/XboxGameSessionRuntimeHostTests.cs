using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class XboxGameSessionRuntimeHostTests
{
    [Fact]
    public void Runtime_composes_xbox_observer_but_defers_start_and_keeps_pending_cadence_fast()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs").Replace("\r\n", "\n", StringComparison.Ordinal);
        var initialize = Method(host, "internal async Task InitializeRuntimeAsync()", "internal void StartRuntimeEventWatchers()");
        var runtimeCreation = initialize.IndexOf("_xboxGameSessionRuntime = new XboxGameSessionRuntime()", StringComparison.Ordinal);
        var headlessGuard = initialize.IndexOf("if (_headlessUninstallPreparation)\n            return;", StringComparison.Ordinal);
        var frontendCreation = initialize.IndexOf("_frontendControl = new SteamInputAddonforClaw.Frontend.InProcessAddonFrontendControl", StringComparison.Ordinal);
        var activeGameSubscription = initialize.IndexOf("_xboxGameSessionRuntime.ActiveGameChanged += OnActiveXboxGameChanged;", StringComparison.Ordinal);
        var readinessSubscription = initialize.IndexOf("_xboxGameSessionRuntime.ObservationReadinessChanged += OnXboxObservationReadinessChanged;", StringComparison.Ordinal);
        var pendingCadence = initialize.IndexOf("UpdateControllerCadenceDecision(\"StartupObserverPending\", logInitialDecision: true);", StringComparison.Ordinal);
        Assert.True(runtimeCreation > headlessGuard);
        Assert.True(frontendCreation > runtimeCreation);
        Assert.True(activeGameSubscription > runtimeCreation && readinessSubscription > activeGameSubscription);
        Assert.True(pendingCadence > readinessSubscription && frontendCreation > pendingCadence);

        Assert.DoesNotContain("_xboxGameSessionRuntime.StartAsync", initialize, StringComparison.Ordinal);
        Assert.Contains("private int _controllerCadenceRequires250Hz = 1;", host, StringComparison.Ordinal);
        Assert.Contains("|| !xboxCanReliablyReportNoActiveGame", host, StringComparison.Ordinal);
        Assert.DoesNotContain("CenterMStartupState", initialize[runtimeCreation..pendingCadence], StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindow", initialize[runtimeCreation..pendingCadence], StringComparison.Ordinal);
    }

    [Fact]
    public void Deferred_start_runs_xbox_scan_after_controller_attempt_and_before_profile_reconcile()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs").Replace("\r\n", "\n", StringComparison.Ordinal);
        var deferred = Method(host, "internal void StartDeferredRuntimeStartup()", "private void StartClawHudStartup()");
        var controllerAttempt = deferred.IndexOf("await TryStartDisabledModeControllerAsync(", StringComparison.Ordinal);
        var controllerFailure = deferred.IndexOf("Deferred Disabled-mode controller startup threw; Runtime remains available.", StringComparison.Ordinal);
        var admissionCleared = deferred.IndexOf("Volatile.Write(ref _disabledControllerStartupPending, 0);", controllerAttempt, StringComparison.Ordinal);
        var startupCancellationCheck = deferred.IndexOf("cancellationToken.IsCancellationRequested", admissionCleared, StringComparison.Ordinal);
        var clawHud = deferred.IndexOf("StartClawHudStartup();", startupCancellationCheck, StringComparison.Ordinal);
        var powerObservation = deferred.IndexOf("StartPowerObservation();", clawHud, StringComparison.Ordinal);
        var observer = deferred.IndexOf("if (_xboxGameSessionRuntime is { } xboxObserver)", powerObservation, StringComparison.Ordinal);
        var observerStart = deferred.IndexOf("await xboxObserver.StartAsync(cancellationToken).ConfigureAwait(false);", observer, StringComparison.Ordinal);
        var profileReconcile = deferred.IndexOf("ReconcileDeviceProfileStartup();", observerStart, StringComparison.Ordinal);
        var backgroundUpdate = deferred.IndexOf("StartBackgroundUpdate();", profileReconcile, StringComparison.Ordinal);

        Assert.True(controllerAttempt >= 0 && controllerFailure > controllerAttempt && admissionCleared > controllerFailure
            && startupCancellationCheck > admissionCleared && clawHud > startupCancellationCheck
            && powerObservation > clawHud && observer > powerObservation
            && observerStart > observer && profileReconcile > observerStart
            && backgroundUpdate > profileReconcile);
        Assert.Contains("var cancellationToken = _startupCancellationTokenSource.Token;", deferred, StringComparison.Ordinal);
        Assert.DoesNotContain("startupResult.CenterMStartupState", deferred, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)", deferred[observer..profileReconcile], StringComparison.Ordinal);
        Assert.Contains("Production XBOX game-session observer could not start; Runtime will continue without active XBOX detection.", deferred[observer..profileReconcile], StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(deferred, "await xboxObserver.StartAsync(cancellationToken).ConfigureAwait(false);"));
        Assert.DoesNotContain("PublisherStarted", deferred[observer..profileReconcile], StringComparison.Ordinal);

        var postObserverCheck = deferred.IndexOf("Volatile.Read(ref _processShutdownStarted) != 0", observerStart, StringComparison.Ordinal);
        Assert.True(postObserverCheck > observerStart && profileReconcile > postObserverCheck);
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
        Assert.True(handler.IndexOf("UpdateControllerCadenceDecision(", StringComparison.Ordinal)
            < handler.IndexOf("ReconcileActiveXboxGameTransition(", StringComparison.Ordinal));
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
    public void Existing_Steam_BPM_XBOX_and_resume_callbacks_update_the_cached_cadence_decision()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs").Replace("\r\n", "\n", StringComparison.Ordinal);
        var runningAppIdStart = host.IndexOf("private void OnActualRunningAppIdChanged(uint appId)", StringComparison.Ordinal);
        var runningAppIdEnd = host.IndexOf("private void OnActiveXboxGameChanged(", runningAppIdStart, StringComparison.Ordinal);
        var runningAppId = host[runningAppIdStart..runningAppIdEnd];
        var bigPictureStart = host.IndexOf("private void OnBigPictureStateChanged(bool active)", StringComparison.Ordinal);
        var bigPictureEnd = host.IndexOf("private void OnXboxObservationReadinessChanged(", bigPictureStart, StringComparison.Ordinal);
        var bigPicture = host[bigPictureStart..bigPictureEnd];
        var resumeStart = host.IndexOf("private void OnPowerResumeObserved()", StringComparison.Ordinal);
        var resumeEnd = host.IndexOf("internal static async Task ReconcilePerformanceAfterResumeAsync", resumeStart, StringComparison.Ordinal);
        var resume = host[resumeStart..resumeEnd];

        Assert.Contains("UpdateControllerCadenceDecision(\"SteamRunningAppIdChanged\")", runningAppId, StringComparison.Ordinal);
        Assert.Contains("UpdateControllerCadenceDecision(\"BigPictureChanged\")", bigPicture, StringComparison.Ordinal);
        Assert.Contains("UpdateControllerCadenceDecision(\"PowerResume\")", resume, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(host, "requires250Hz: RequiresController250Hz"));
        Assert.Contains("Volatile.Read(ref _controllerCadenceRequires250Hz) != 0", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_and_BigPicture_session_changes_invalidate_frontend_status_without_a_badge_change()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs").Replace("\r\n", "\n", StringComparison.Ordinal);
        var runningAppIdStart = host.IndexOf("private void OnActualRunningAppIdChanged(uint appId)", StringComparison.Ordinal);
        var runningAppIdEnd = host.IndexOf("private void OnActiveXboxGameChanged(", runningAppIdStart, StringComparison.Ordinal);
        var runningAppId = host[runningAppIdStart..runningAppIdEnd];
        var bigPictureStart = host.IndexOf("private void OnBigPictureStateChanged(bool active)", StringComparison.Ordinal);
        var bigPictureEnd = host.IndexOf("private void OnXboxObservationReadinessChanged(", bigPictureStart, StringComparison.Ordinal);
        var bigPicture = host[bigPictureStart..bigPictureEnd];
        var invalidationStart = host.IndexOf("private void NotifySteamSessionStatusInvalidated()", StringComparison.Ordinal);
        var invalidationEnd = host.IndexOf("internal static async Task NotifyControllerBadgeIfChangedAfterAsync", invalidationStart, StringComparison.Ordinal);
        var invalidation = host[invalidationStart..invalidationEnd];

        Assert.Contains("NotifySteamSessionStatusInvalidated();", runningAppId, StringComparison.Ordinal);
        Assert.Contains("NotifySteamSessionStatusInvalidated();", bigPicture, StringComparison.Ordinal);
        Assert.DoesNotContain("NotifyControllerBadgeIfChanged(", runningAppId, StringComparison.Ordinal);
        Assert.DoesNotContain("NotifyControllerBadgeIfChanged(", bigPicture, StringComparison.Ordinal);
        Assert.Contains("NotifyControllerBadgeStateInvalidated()", invalidation, StringComparison.Ordinal);
    }

    [Fact]
    public void Frontend_contract_removes_diagnostic_rpc_and_advances_only_frontend_protocol()
    {
        var wire = ReadSource("src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs");
        var contracts = ReadSource("src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs");
        var server = ReadSource("src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs");
        var client = ReadSource("src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs");
        var control = ReadSource("src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs");

        Assert.Contains("CurrentVersion = 86", wire, StringComparison.Ordinal);
        Assert.Contains("Version 58:", wire, StringComparison.Ordinal);
        Assert.Contains("Version 59:", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureXboxSessionDiagnostic", wire + contracts + server + client + control, StringComparison.Ordinal);
        Assert.DoesNotContain("StartXboxSessionDiagnostic", wire + contracts + server + client + control, StringComparison.Ordinal);
        Assert.DoesNotContain("StopXboxSessionDiagnostic", wire + contracts + server + client + control, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateXboxSessionDiagnosticReport", wire + contracts + server + client + control, StringComparison.Ordinal);
        Assert.Contains("CurrentVersion = 20", ReadSource("src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs"), StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string Method(string source, string startToken, string endToken)
    {
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method start: {startToken}");
        var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find method end: {endToken}");
        return source[start..end];
    }
}
