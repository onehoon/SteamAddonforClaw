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
// endpoint below. QamHost itself remains BPM-scoped and never starts/stops/restarts Steam.
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
        // Cleanup ownership belongs to this CDP/GamepadUI session only.
        installationSucceeded = false;
        installMayExist = false;
        teardownAttempted = false;
            sessionClient.AddonQamConsoleMessage += message => log.Info(message);
            async Task DeliverResponseAsync(string payload)
            {
                var response = await frontendBridge.HandleRequestAsync(payload, lifetimeToken);
                try { await sessionClient.EvaluateAsync($"window.__STEAM_INPUT_ADDON_QAM__?.__receiveBridgeResponse?.({JsonSerializer.Serialize(response, QamFrontendBridge.BridgeJson)})", lifetimeToken); }
                catch (Exception exception) { log.Info($"QAM bridge response delivery skipped for retired CDP session. {exception.Message}"); }
            }
            void OnBindingCalled(string name, string payload)
            {
                if (string.Equals(name, "__steamInputAddonQamHost", StringComparison.Ordinal))
                    _ = Task.Run(() => DeliverResponseAsync(payload), lifetimeToken);
            }
            async Task DeliverInvalidationAsync()
            {
                try { await sessionClient.EvaluateAsync("window.__STEAM_INPUT_ADDON_QAM__?.__receiveBridgeNotification?.('state-invalidated')", lifetimeToken); }
                catch (Exception exception) { log.Info($"QAM invalidation delivery skipped for retired CDP session. {exception.Message}"); }
            }
            void OnStateInvalidated(object? _, EventArgs __) => _ = Task.Run(DeliverInvalidationAsync, lifetimeToken);
            sessionClient.BindingCalled += OnBindingCalled;
            frontendBridge.StateInvalidated += OnStateInvalidated;
        var reload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDocumentLoaded() => reload.TrySetResult();
        currentClient.DocumentLoaded += OnDocumentLoaded;
        CdpTarget? target = null;
        try
        {
            while (!lifetimeToken.IsCancellationRequested)
            {
                var targets = await currentClient.ListTargetsAsync(lifetimeToken);
                target = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);
                if (target is not null || !managed || !QamHostRecovery.IsOpen(DateTimeOffset.UtcNow, recoveryDeadline)) break;
                await Task.Delay(250, lifetimeToken);
            }
            if (target is null)
            {
                if (managed && QamHostRecovery.IsOpen(DateTimeOffset.UtcNow, recoveryDeadline)) continue;
                log.Warn("GamepadUI recovery window expired; QAM remains unavailable for this BPM session.");
                break;
            }
            log.Info($"GamepadUI target acquired. Id={target.Id} Title={target.Title} Url={target.Url}");
            await currentClient.ConnectAsync(target, lifetimeToken);
            log.Info("CDP connected.");
            installationSucceeded = true; // cleanup is eligible once the remote install may execute
            await InstallAsync(currentClient);
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
                    await InstallAsync(currentClient);
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
            sessionClient.BindingCalled -= OnBindingCalled;
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
    log.Warn("GamepadUI recovery window expired; QAM remains unavailable for this BPM session.");
if (stopRequested || lifetimeToken.IsCancellationRequested)
    log.Info("QamHost stop requested.");
return 0;

async Task InstallAsync(SteamGamepadUiCdpClient client)
{
    // The remote command may execute even when the local await is cancelled.
    installMayExist = true;
    var result = CdpEvaluateResult.Parse(await client.EvaluateAsync(frontendScript, lifetimeToken));
    if (!result.Succeeded) throw new InvalidOperationException($"qam.js evaluation exception: {result.ErrorText}");
    if (result.BooleanValue != true) throw new InvalidOperationException("install() returned false.");
    log.Info("QAM injection succeeded.");
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
