using SteamInputAddonforClaw.QamHost;

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
Task stopTask = managed ? lifetime!.StopTask : WaitForConsoleShutdownAsync();
SteamGamepadUiCdpClient? currentClient = null;
var installationSucceeded = false;
var teardownAttempted = false;
try
{
    while (!lifetimeToken.IsCancellationRequested)
    {
        currentClient = new SteamGamepadUiCdpClient(devToolsEndpoint);
        currentClient.AddonQamConsoleMessage += message => log.Info(message);
        var reload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        currentClient.DocumentLoaded += () => reload.TrySetResult();
        CdpTarget? target = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(managed ? 10 : 0);
        try
        {
            while (!lifetimeToken.IsCancellationRequested)
            {
                var targets = await currentClient.ListTargetsAsync(lifetimeToken);
                target = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);
                if (target is not null || !managed || DateTimeOffset.UtcNow >= deadline) break;
                await Task.Delay(250, lifetimeToken);
            }
            if (target is null) { log.Warn("GamepadUI target not found."); break; }
            log.Info($"GamepadUI target acquired. Id={target.Id} Title={target.Title} Url={target.Url}");
            await currentClient.ConnectAsync(target, lifetimeToken);
            log.Info("CDP connected.");
            await InstallAsync(currentClient);
            teardownAttempted = false;
            installationSucceeded = true;

            while (!lifetimeToken.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(stopTask, currentClient.ConnectionEnded, reload.Task);
                if (completed == stopTask) break;
                if (completed == reload.Task)
                {
                    log.Info("GamepadUI document reloaded; reinjecting QAM.");
                    reload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    currentClient.DocumentLoaded += () => reload.TrySetResult();
                    await InstallAsync(currentClient);
                    continue;
                }
                log.Warn("CDP connection lost; starting GamepadUI reacquisition.");
                installationSucceeded = false;
                break;
            }
            if (lifetimeToken.IsCancellationRequested) break;
        }
        catch (Exception ex) when (lifetimeToken.IsCancellationRequested)
        {
            log.Info($"QamHost stop requested. {ex.GetType().Name}: {ex.Message}");
            break;
        }
        catch (Exception ex)
        {
            log.Warn($"QamHost QAM session ended. {ex.GetType().Name}: {ex.Message}");
            installationSucceeded = false;
        }
        finally
        {
            if (installationSucceeded) await TeardownAsync(currentClient);
            await currentClient.DisposeAsync();
            currentClient = null;
        }
        if (!lifetimeToken.IsCancellationRequested) await Task.Delay(250, lifetimeToken);
    }
}
catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
finally
{
    if (currentClient is not null) await currentClient.DisposeAsync();
}
log.Info("managed stop requested; cleanup completed.");
return 0;

async Task InstallAsync(SteamGamepadUiCdpClient client)
{
    var result = CdpEvaluateResult.Parse(await client.EvaluateAsync(frontendScript, lifetimeToken));
    if (!result.Succeeded) throw new InvalidOperationException($"qam.js evaluation exception: {result.ErrorText}");
    if (result.BooleanValue != true) throw new InvalidOperationException("install() returned false.");
    log.Info("QAM injection succeeded.");
}

async Task TeardownAsync(SteamGamepadUiCdpClient client)
{
    if (!installationSucceeded || teardownAttempted) return;
    teardownAttempted = true;
    try
    {
        var result = CdpEvaluateResult.Parse(await client.EvaluateAsync("window.__STEAM_INPUT_ADDON_QAM__?.uninstall?.() ?? false", CancellationToken.None));
        if (result.Succeeded && result.BooleanValue == true) log.Info("cleanup completed.");
    }
    catch (Exception ex) when (ex is InvalidOperationException or System.Net.WebSockets.WebSocketException or IOException)
    { log.Info($"QAM target already closed; explicit uninstall was not available. {ex.GetType().Name}: {ex.Message}"); }
}

static async Task WaitForConsoleShutdownAsync()
{
    var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
    await tcs.Task;
}
