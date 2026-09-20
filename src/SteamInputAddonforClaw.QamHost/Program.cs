using SteamInputAddonforClaw.QamHost;
using SteamInputAddonforClaw.FrontendTransport;
using System.Text.Json;


var managed = args.Contains("--managed", StringComparer.OrdinalIgnoreCase);
string? logDirectory = null;
for (var index = 0; index < args.Length - 1; index++)
    if (string.Equals(args[index], "--log-directory", StringComparison.OrdinalIgnoreCase)) logDirectory = args[index + 1];
using var log = new QamHostLogger(logDirectory);
log.Info($"QamHost starting. ManagedMode={managed}. DevToolsEndpoint=http://127.0.0.1:8080");

// The Runtime prepares Steam's .cef-enable-remote-debugging marker during Addon startup. Steam
// consumes that marker when its CEF/steamwebhelper session starts and exposes the loopback DevTools
// endpoint below. QamHost itself remains GamepadUI-session scoped and never starts/stops/restarts Steam.
var devToolsEndpoint = new Uri("http://127.0.0.1:8080");
var frontendPath = Path.Combine(AppContext.BaseDirectory, "Frontend", "qam.js");

if (!File.Exists(frontendPath))
{
    log.Error($"Frontend script not found. Path={frontendPath}");
    return 1;
}

var frontendScript = await File.ReadAllTextAsync(frontendPath);
log.Info($"Frontend script loaded. Path={frontendPath} Bytes={frontendScript.Length}");

using var lifetime = managed ? QamHostManagedLifetime.Start(() => Console.In.ReadLineAsync()) : null;
var lifetimeToken = lifetime?.Token ?? CancellationToken.None;
await using var frontendBridge = new QamFrontendBridge();
using var bridgeConnectCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
async Task ConnectRuntimeBridgeBoundedAsync(CancellationToken token)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
    while (!token.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
        attempt.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            await frontendBridge.ConnectAsync(attempt.Token).ConfigureAwait(false);
            log.Info("QAM frontend transport connected.");
            return;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or FrontendTransportException)
        {
            if (token.IsCancellationRequested || DateTimeOffset.UtcNow >= deadline) return;
            try { await Task.Delay(250, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
    }
    if (!token.IsCancellationRequested)
        log.Warn("QAM frontend transport unavailable after bounded startup acquisition; CDP integration remains active.");
}
var bridgeConnectTask = ConnectRuntimeBridgeBoundedAsync(bridgeConnectCts.Token);
Task stopTask = managed ? lifetime!.StopTask : WaitForConsoleShutdownAsync();
SteamGamepadUiCdpClient? currentClient = null;
var installationSucceeded = false;
var installMayExist = false;
var teardownAttempted = false;
var stopRequested = false;
DateTimeOffset? recoveryDeadline = managed ? DateTimeOffset.UtcNow.AddSeconds(10) : null;
try
{
    while (!lifetimeToken.IsCancellationRequested &&
           QamHostRecovery.IsOpen(DateTimeOffset.UtcNow, recoveryDeadline))
    {
        var sessionClient = new SteamGamepadUiCdpClient(devToolsEndpoint);
        currentClient = sessionClient;
        long documentGeneration = 0;
        var sessionDiagnosticsCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        var targetSnapshotGate = new SemaphoreSlim(1, 1);
        var geometryDiagnosticGate = new SemaphoreSlim(1, 1);
        var hostGeometryDiagnosticGate = new SemaphoreSlim(1, 1);
        var hostTransformGate = new SemaphoreSlim(1, 1);
        SteamGamepadUiCdpClient? hostTransformClient = null;
        string? hostTransformTargetId = null;
        string? hostTransformViewPlaceholderClass = null;
        async Task LogTargetSnapshotAsync(string reason, CancellationToken token)
        {
            try
            {
                await targetSnapshotGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var snapshotTargets = await sessionClient.ListTargetsAsync(token).ConfigureAwait(false);
                    log.Info(CdpTargetSnapshotFormatter.Format(reason, snapshotTargets));
                }
                finally { targetSnapshotGate.Release(); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                log.Warn($"QAM CDP target snapshot unavailable. Reason={reason}. {exception.GetType().Name}: {exception.Message}");
            }
        }
        async Task LogQuickAccessGeometrySnapshotsAsync(string reason, CancellationToken token)
        {
            try
            {
                await geometryDiagnosticGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var classNamesResult = CdpEvaluateResult.Parse(await sessionClient.EvaluateAsync(
                        "JSON.stringify(window.__STEAM_INPUT_ADDON_QAM__?.__getQamGeometryClassNames?.() ?? null)",
                        token).ConfigureAwait(false));
                    if (!classNamesResult.Succeeded || string.IsNullOrWhiteSpace(classNamesResult.StringValue) || classNamesResult.StringValue == "null")
                    {
                        log.Info($"QAM QuickAccess geometry unavailable. Reason={reason} Semantic class names were not exposed by the current QAM document.");
                        return;
                    }

                    var classNames = JsonSerializer.Deserialize<QamGeometryClassNames>(classNamesResult.StringValue);
                    if (classNames is null || string.IsNullOrWhiteSpace(classNames.PanelOuterNav) || string.IsNullOrWhiteSpace(classNames.TabGroupPanel))
                    {
                        log.Info($"QAM QuickAccess geometry unavailable. Reason={reason} Semantic class names were invalid.");
                        return;
                    }

                    var targets = QuickAccessTargetSelector.SelectQuickAccessTargets(await sessionClient.ListTargetsAsync(token).ConfigureAwait(false));
                    if (targets.Count == 0)
                    {
                        log.Info($"QAM QuickAccess geometry unavailable. Reason={reason} No usable QuickAccess_uid target was present.");
                        return;
                    }

                    foreach (var target in targets)
                    {
                        await using var diagnosticClient = new SteamGamepadUiCdpClient(devToolsEndpoint);
                        try
                        {
                            await diagnosticClient.ConnectReadOnlyAsync(target, token).ConfigureAwait(false);
                            var result = CdpEvaluateResult.Parse(await diagnosticClient.EvaluateAsync(
                                QuickAccessGeometryDiagnostic.CreateExpression(classNames), token).ConfigureAwait(false));
                            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StringValue))
                            {
                                log.Warn($"QAM QuickAccess geometry target evaluation failed. Reason={reason} TargetTitle={target.Title} TargetId={target.Id} Error={result.ErrorText ?? "empty result"}");
                                continue;
                            }

                            log.Info($"QAM QuickAccess geometry snapshot. Reason={reason} TargetTitle={target.Title} TargetId={target.Id} Snapshot={result.StringValue}");
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception exception)
                        {
                            log.Warn($"QAM QuickAccess geometry target unavailable. Reason={reason} TargetTitle={target.Title} TargetId={target.Id} {exception.GetType().Name}: {exception.Message}");
                        }
                    }
                }
                finally { geometryDiagnosticGate.Release(); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                log.Warn($"QAM QuickAccess geometry diagnostic unavailable. Reason={reason}. {exception.GetType().Name}: {exception.Message}");
            }
        }
        async Task LogQamHostGeometrySnapshotsAsync(string reason, CancellationToken token)
        {
            try
            {
                await hostGeometryDiagnosticGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var classNamesResult = CdpEvaluateResult.Parse(await sessionClient.EvaluateAsync(
                        "JSON.stringify(window.__STEAM_INPUT_ADDON_QAM__?.__getQamGeometryClassNames?.() ?? null)",
                        token).ConfigureAwait(false));
                    if (!classNamesResult.Succeeded || string.IsNullOrWhiteSpace(classNamesResult.StringValue) || classNamesResult.StringValue == "null")
                    {
                        log.Info($"QAM host geometry unavailable. Reason={reason} Semantic class names were not exposed by the current QAM document.");
                        return;
                    }

                    var classNames = JsonSerializer.Deserialize<QamGeometryClassNames>(classNamesResult.StringValue);
                    if (classNames is null || string.IsNullOrWhiteSpace(classNames.ViewPlaceholder))
                    {
                        log.Info($"QAM host geometry unavailable. Reason={reason} ViewPlaceholder semantic class was not resolved.");
                        return;
                    }

                    var targets = QamHostTargetSelector.SelectQamHostTargets(await sessionClient.ListTargetsAsync(token).ConfigureAwait(false));
                    if (targets.Count == 0)
                    {
                        log.Info($"QAM host geometry unavailable. Reason={reason} No bounded Steam QAM host target was present.");
                        return;
                    }

                    foreach (var target in targets)
                    {
                        await using var diagnosticClient = new SteamGamepadUiCdpClient(devToolsEndpoint);
                        try
                        {
                            await diagnosticClient.ConnectReadOnlyAsync(target, token).ConfigureAwait(false);
                            var result = CdpEvaluateResult.Parse(await diagnosticClient.EvaluateAsync(
                                QamHostGeometryDiagnostic.CreateExpression(classNames), token).ConfigureAwait(false));
                            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StringValue))
                            {
                                log.Warn($"QAM host geometry target evaluation failed. Reason={reason} TargetTitle={target.Title} TargetId={target.Id} Error={result.ErrorText ?? "empty result"}");
                                continue;
                            }

                            log.Info($"QAM host geometry snapshot. Reason={reason} TargetTitle={target.Title} TargetId={target.Id} Snapshot={result.StringValue}");
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception exception)
                        {
                            log.Warn($"QAM host geometry target unavailable. Reason={reason} TargetTitle={target.Title} TargetId={target.Id} {exception.GetType().Name}: {exception.Message}");
                        }
                    }
                }
                finally { hostGeometryDiagnosticGate.Release(); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                log.Warn($"QAM host geometry diagnostic unavailable. Reason={reason}. {exception.GetType().Name}: {exception.Message}");
            }
        }

        async Task ApplyQamHostTransformAsync(bool addonSelected, string reason, CancellationToken token)
        {
            try
            {
                await hostTransformGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var classNamesResult = CdpEvaluateResult.Parse(await sessionClient.EvaluateAsync(
                        "JSON.stringify(window.__STEAM_INPUT_ADDON_QAM__?.__getQamGeometryClassNames?.() ?? null)",
                        token).ConfigureAwait(false));
                    if (!classNamesResult.Succeeded || string.IsNullOrWhiteSpace(classNamesResult.StringValue) || classNamesResult.StringValue == "null")
                    {
                        log.Info($"QAM host transform unavailable. Reason={reason} Semantic class names were not exposed by the current QAM document.");
                        return;
                    }

                    var classNames = JsonSerializer.Deserialize<QamGeometryClassNames>(classNamesResult.StringValue);
                    if (classNames is null || string.IsNullOrWhiteSpace(classNames.ViewPlaceholder))
                    {
                        log.Info($"QAM host transform unavailable. Reason={reason} ViewPlaceholder semantic class was not resolved.");
                        return;
                    }

                    hostTransformViewPlaceholderClass = classNames.ViewPlaceholder;
                    var hostTarget = QamHostTargetSelector
                        .SelectQamHostTargets(await sessionClient.ListTargetsAsync(token).ConfigureAwait(false))
                        .FirstOrDefault(target => string.Equals(target.Title, "Steam Big Picture Mode", StringComparison.OrdinalIgnoreCase));
                    if (hostTarget is null)
                    {
                        log.Info($"QAM host transform unavailable. Reason={reason} Steam Big Picture Mode target was not present.");
                        return;
                    }

                    if (hostTransformClient is null ||
                        !string.Equals(hostTransformTargetId, hostTarget.Id, StringComparison.Ordinal) ||
                        hostTransformClient.ConnectionEnded.IsCompleted)
                    {
                        if (hostTransformClient is not null)
                            await hostTransformClient.DisposeAsync().ConfigureAwait(false);
                        hostTransformClient = new SteamGamepadUiCdpClient(devToolsEndpoint);
                        await hostTransformClient.ConnectUnboundAsync(hostTarget, token).ConfigureAwait(false);
                        hostTransformTargetId = hostTarget.Id;
                    }

                    var result = CdpEvaluateResult.Parse(await hostTransformClient.EvaluateAsync(
                        QamHostTransformPatcher.CreateApplyExpression(classNames.ViewPlaceholder, addonSelected), token).ConfigureAwait(false));
                    if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StringValue))
                    {
                        log.Warn($"QAM host transform evaluation failed. Reason={reason} TargetTitle={hostTarget.Title} TargetId={hostTarget.Id} Error={result.ErrorText ?? "empty result"}");
                        return;
                    }

                    log.Info($"QAM host transform selection applied. Reason={reason} AddonSelected={addonSelected} TargetId={hostTarget.Id} Result={result.StringValue}");
                }
                finally { hostTransformGate.Release(); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                log.Warn($"QAM host transform unavailable. Reason={reason}. {exception.GetType().Name}: {exception.Message}");
            }
        }

        async Task UninstallQamHostTransformAsync(CancellationToken token)
        {
            try
            {
                await hostTransformGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (hostTransformClient is not null && !string.IsNullOrWhiteSpace(hostTransformViewPlaceholderClass))
                    {
                        var result = CdpEvaluateResult.Parse(await hostTransformClient.EvaluateAsync(
                            QamHostTransformPatcher.CreateUninstallExpression(hostTransformViewPlaceholderClass), token).ConfigureAwait(false));
                        if (!result.Succeeded)
                            log.Warn($"QAM host transform cleanup failed. Error={result.ErrorText ?? "empty result"}");
                        else
                            log.Info($"QAM host transform cleanup completed. Result={result.StringValue ?? "null"}");
                    }
                }
                finally
                {
                    if (hostTransformClient is not null)
                        await hostTransformClient.DisposeAsync().ConfigureAwait(false);
                    hostTransformClient = null;
                    hostTransformTargetId = null;
                    hostTransformViewPlaceholderClass = null;
                    hostTransformGate.Release();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                log.Info($"QAM host transform cleanup skipped. {exception.GetType().Name}: {exception.Message}");
            }
        }
        // Cleanup ownership belongs to this CDP/GamepadUI session only.
        installationSucceeded = false;
        installMayExist = false;
        teardownAttempted = false;
            sessionClient.AddonQamConsoleMessage += message => log.Info(message);
            async Task DeliverResponseAsync(string payload, long admittedGeneration)
            {
                var response = await frontendBridge.HandleRequestAsync(payload, lifetimeToken);
                if (admittedGeneration != Volatile.Read(ref documentGeneration)) return;
                try { await sessionClient.EvaluateAsync($"window.__STEAM_INPUT_ADDON_QAM__?.__receiveBridgeResponse?.({JsonSerializer.Serialize(response, QamFrontendBridge.BridgeJson)})", lifetimeToken); }
                catch (Exception exception) { log.Info($"QAM bridge response delivery skipped for retired CDP session. {exception.Message}"); }
            }
            void OnBindingCalled(string name, string payload)
            {
                if (string.Equals(name, "__steamInputAddonQamHost", StringComparison.Ordinal))
                {
                    if (TryParseQamHostWidthSelection(payload, out var addonSelected))
                    {
                        var admittedGeneration = Volatile.Read(ref documentGeneration);
                        _ = Task.Run(() => DeliverQamHostSelectionAsync(addonSelected, admittedGeneration), lifetimeToken);
                        return;
                    }
                    var bridgeGeneration = Volatile.Read(ref documentGeneration);
                    _ = Task.Run(() => DeliverResponseAsync(payload, bridgeGeneration), lifetimeToken);
                }
            }
            async Task DeliverInvalidationAsync()
            {
                try { await sessionClient.EvaluateAsync("window.__STEAM_INPUT_ADDON_QAM__?.__receiveBridgeNotification?.('state-invalidated')", lifetimeToken); }
                catch (Exception exception) { log.Info($"QAM invalidation delivery skipped for retired CDP session. {exception.Message}"); }
            }
            async Task DeliverSelectAddonOnNextQuickAccessOpenAsync(long admittedGeneration)
            {
                if (admittedGeneration != Volatile.Read(ref documentGeneration)) return;
                try
                {
                    await sessionClient.EvaluateAsync("window.__STEAM_INPUT_ADDON_QAM__?.__receiveBridgeNotification?.('select-addon-on-next-open')", lifetimeToken);
                    if (admittedGeneration != Volatile.Read(ref documentGeneration)) return;
                    var acknowledged = await frontendBridge.Client.AcknowledgeQamSelectAddonOnNextOpenPreparedAsync(lifetimeToken).ConfigureAwait(false);
                    if (!acknowledged) log.Info("QAM Addon first-tab preparation acknowledgement was not accepted.");
                }
                catch (Exception exception) { log.Info($"QAM Addon first-tab request delivery skipped for retired CDP session. {exception.Message}"); }
            }
            async Task DeliverQamHostSelectionAsync(bool addonSelected, long admittedGeneration)
            {
                if (admittedGeneration != Volatile.Read(ref documentGeneration)) return;
                await ApplyQamHostTransformAsync(addonSelected, "active-tab-notification", lifetimeToken).ConfigureAwait(false);
            }
            void OnStateInvalidated(object? _, EventArgs __) => _ = Task.Run(DeliverInvalidationAsync, lifetimeToken);
            void OnSelectAddonOnNextQuickAccessOpen(object? _, EventArgs __)
            {
                var admittedGeneration = Volatile.Read(ref documentGeneration);
                _ = Task.Run(() => DeliverSelectAddonOnNextQuickAccessOpenAsync(admittedGeneration), lifetimeToken);
                _ = Task.Run(() => LogTargetSnapshotAsync("select-addon-on-next-open", sessionDiagnosticsCts.Token), sessionDiagnosticsCts.Token);
                _ = Task.Run(() => LogQuickAccessGeometrySnapshotsAsync("select-addon-on-next-open", sessionDiagnosticsCts.Token), sessionDiagnosticsCts.Token);
                _ = Task.Run(() => LogQamHostGeometrySnapshotsAsync("select-addon-on-next-open", sessionDiagnosticsCts.Token), sessionDiagnosticsCts.Token);
            }
            sessionClient.BindingCalled += OnBindingCalled;
            frontendBridge.StateInvalidated += OnStateInvalidated;
            frontendBridge.SelectAddonOnNextQuickAccessOpenRequested += OnSelectAddonOnNextQuickAccessOpen;
        var reload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDocumentLoaded() { Interlocked.Increment(ref documentGeneration); reload.TrySetResult(); }
        currentClient.DocumentLoaded += OnDocumentLoaded;
        CdpTarget? target = null;
        try
        {
            var initialTargetSnapshotLogged = false;
            while (!lifetimeToken.IsCancellationRequested)
            {
                var targets = await currentClient.ListTargetsAsync(lifetimeToken);
                if (!initialTargetSnapshotLogged)
                {
                    log.Info(CdpTargetSnapshotFormatter.Format("initial-acquisition", targets));
                    initialTargetSnapshotLogged = true;
                }
                target = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);
                if (target is not null || !managed || !QamHostRecovery.IsOpen(DateTimeOffset.UtcNow, recoveryDeadline)) break;
                await Task.Delay(250, lifetimeToken);
            }
            if (target is null)
            {
                if (managed && QamHostRecovery.IsOpen(DateTimeOffset.UtcNow, recoveryDeadline)) continue;
                log.Warn("GamepadUI recovery window expired; QAM remains unavailable for this GamepadUI session.");
                break;
            }
            log.Info($"GamepadUI target acquired. Id={target.Id} Title={target.Title} Url={target.Url}");
            await currentClient.ConnectAsync(target, lifetimeToken);
            log.Info("CDP connected.");
            installationSucceeded = true; // cleanup is eligible once the remote install may execute
            await InstallForCurrentDocumentAsync(currentClient);
            await ApplyQamHostTransformAsync(false, "gamepad-ui-install", lifetimeToken);
            recoveryDeadline = null;

            while (!lifetimeToken.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(stopTask, currentClient.ConnectionEnded, reload.Task);
                if (completed == stopTask)
                {
                    stopRequested = true;
                    break;
                }
                if (completed == reload.Task)
                {
                    log.Info("GamepadUI document reloaded; reinjecting QAM.");
                    reload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    await InstallForCurrentDocumentAsync(currentClient);
                    await ApplyQamHostTransformAsync(false, "gamepad-ui-reload", lifetimeToken);
                    continue;
                }
                log.Warn("CDP connection lost.");
                installationSucceeded = false;
                if (!managed)
                {
                    log.Warn("Non-managed QAM session ended; reconnect recovery is disabled.");
                    stopRequested = true;
                    break;
                }
                log.Warn("Starting bounded GamepadUI reacquisition.");
                recoveryDeadline = QamHostRecovery.BeginAfterSessionFailure(managed, recoveryDeadline, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
                break;
            }
            if (stopRequested || lifetimeToken.IsCancellationRequested) break;
        }
        catch (Exception ex) when (lifetimeToken.IsCancellationRequested)
        {
            log.Info($"QamHost stop requested. {ex.GetType().Name}: {ex.Message}");
            break;
        }
        catch (Exception ex)
        {
            recoveryDeadline = QamHostRecovery.BeginAfterSessionFailure(managed, recoveryDeadline, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
            if (!managed || !QamHostRecovery.IsOpen(DateTimeOffset.UtcNow, recoveryDeadline))
                log.Warn($"QamHost QAM session ended. {ex.GetType().Name}: {ex.Message}");
            else
                log.Warn($"QAM session failed; starting bounded GamepadUI recovery. {ex.GetType().Name}: {ex.Message}");
            if (!managed) break;
        }
        finally
        {
            frontendBridge.StateInvalidated -= OnStateInvalidated;
            frontendBridge.SelectAddonOnNextQuickAccessOpenRequested -= OnSelectAddonOnNextQuickAccessOpen;
            sessionClient.BindingCalled -= OnBindingCalled;
            sessionDiagnosticsCts.Cancel();
            sessionDiagnosticsCts.Dispose();
            await UninstallQamHostTransformAsync(CancellationToken.None);
            if (installMayExist) await TeardownAsync(sessionClient);
            await sessionClient.DisposeAsync();
            if (ReferenceEquals(currentClient, sessionClient)) currentClient = null;
        }
        if (!stopRequested && !lifetimeToken.IsCancellationRequested && recoveryDeadline.HasValue)
            await Task.Delay(250, lifetimeToken);
    }
}
catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
finally
{
    bridgeConnectCts.Cancel();
    try { await bridgeConnectTask.ConfigureAwait(false); }
    catch (OperationCanceledException) when (bridgeConnectCts.IsCancellationRequested) { }
    if (currentClient is not null) await currentClient.DisposeAsync();
}
if (!stopRequested && !lifetimeToken.IsCancellationRequested && recoveryDeadline is not null)
    log.Warn("GamepadUI recovery window expired; QAM remains unavailable for this GamepadUI session.");
if (stopRequested || lifetimeToken.IsCancellationRequested)
    log.Info("QamHost stop requested.");
return 0;

async Task InstallAsync(SteamGamepadUiCdpClient client)
{
    // The remote command may execute even when the local await is cancelled.
    installMayExist = true;
    var result = CdpEvaluateResult.Parse(await client.EvaluateAsync(frontendScript, lifetimeToken));
    if (!result.Succeeded) throw new InvalidOperationException($"qam.js evaluation exception: {result.ErrorText}");
    if (result.BooleanValue != true)
    {
        var failure = CdpEvaluateResult.Parse(await client.EvaluateAsync("window.__STEAM_INPUT_ADDON_QAM__?.installFailureKind ?? null", lifetimeToken));
        if (failure.StringValue == "native-components")
            throw new DeterministicQamInstallException("native Steam CommonUI controls were unavailable.");
        throw new InvalidOperationException("install() returned false.");
    }
    log.Info("QAM injection succeeded.");
}

async Task InstallForCurrentDocumentAsync(SteamGamepadUiCdpClient client)
{
    try
    {
        await InstallAsync(client);
    }
    catch (DeterministicQamInstallException ex)
    {
        log.Warn($"QAM installation is unavailable for the current GamepadUI document; waiting for document replacement. {ex.Message}");
    }
}

async Task TeardownAsync(SteamGamepadUiCdpClient client)
{
    if (!installationSucceeded || teardownAttempted) return;
    if (!installMayExist) return;
    teardownAttempted = true;
    try
    {
        var result = CdpEvaluateResult.Parse(await client.EvaluateAsync("window.__STEAM_INPUT_ADDON_QAM__?.uninstall?.() ?? false", CancellationToken.None));
        if (!result.Succeeded || result.BooleanValue != true)
            log.Error($"QAM cleanup failed: {result.ErrorText ?? "uninstall() returned false"}.");
        else
            log.Info("cleanup completed.");
    }
    catch (Exception ex) when (ex is InvalidOperationException or System.Net.WebSockets.WebSocketException or IOException)
    { log.Info($"QAM target already closed; explicit uninstall was not available. {ex.GetType().Name}: {ex.Message}"); }
    catch (Exception ex)
    { log.Warn($"QAM cleanup failed unexpectedly. {ex.GetType().Name}: {ex.Message}"); }
}

static async Task WaitForConsoleShutdownAsync()
{
    var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
    await tcs.Task;
}

static bool TryParseQamHostWidthSelection(string payload, out bool addonSelected)
{
    addonSelected = false;
    try
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!root.TryGetProperty("kind", out var kind) ||
            !string.Equals(kind.GetString(), "qam-host-width-selection", StringComparison.Ordinal))
        {
            return false;
        }

        addonSelected = root.TryGetProperty("activeTab", out var activeTab) &&
                        string.Equals(activeTab.GetString(), "steam-input-addon", StringComparison.Ordinal);
        return true;
    }
    catch (JsonException)
    {
        return false;
    }
}
