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

await using var client = new SteamGamepadUiCdpClient(devToolsEndpoint);
client.AddonQamConsoleMessage += message => log.Info(message);
using var lifetime = managed ? QamHostManagedLifetime.Start(() => Console.In.ReadLineAsync()) : null;
Task? managedStopTask = null;
if (managed)
{
    managedStopTask = lifetime!.StopTask;
}
var lifetimeToken = lifetime?.Token ?? CancellationToken.None;

CdpTarget? gamepadUiTarget = null;
try
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(managed ? 10 : 0);
    while (true)
    {
        IReadOnlyList<CdpTarget> targets;
        try { targets = await client.ListTargetsAsync(lifetimeToken); }
        catch (Exception ex) when (managed && ex is HttpRequestException or TaskCanceledException && DateTimeOffset.UtcNow < deadline)
        { await Task.Delay(250, lifetimeToken); continue; }

        log.Info($"target count={targets.Count}");
        foreach (var target in targets) log.Info($"Target Type={target.Type} Title={target.Title} Url={target.Url}");
        gamepadUiTarget = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);
        if (gamepadUiTarget is not null || !managed || DateTimeOffset.UtcNow >= deadline) break;
        await Task.Delay(250, lifetimeToken);
    }
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
{
    if (lifetimeToken.IsCancellationRequested)
    {
        log.Info("QamHost stop requested during startup.");
        return 0;
    }
    log.Warn($"DevTools endpoint unavailable. Steam CEF debugging is not active for this Steam session. If the Addon just created .cef-enable-remote-debugging while Steam was already running, restart Steam once. {ex.GetType().Name}: {ex.Message}");
    return 0;
}

if (gamepadUiTarget is null)
{
    log.Warn("GamepadUI target not found.");
    return 0;
}

log.Info($"GamepadUI target selected. Id={gamepadUiTarget.Id} Title={gamepadUiTarget.Title} Url={gamepadUiTarget.Url}");

try
{
    await client.ConnectAsync(gamepadUiTarget, lifetimeToken);
    log.Info("CDP connected.");

    var rawEvalResult = await client.EvaluateAsync(frontendScript, lifetimeToken);
    var evalResult = CdpEvaluateResult.Parse(rawEvalResult);

    if (!evalResult.Succeeded)
    {
        log.Error($"qam.js evaluation exception. {evalResult.ErrorText}");
        return 0;
    }

    if (evalResult.BooleanValue != true)
    {
        log.Warn("install() returned false.");
        return 0;
    }

    if (lifetimeToken.IsCancellationRequested)
    {
        await BestEffortUninstallAsync(client, log);
        return 0;
    }

    log.Info("Frontend evaluation succeeded. QAM hook installed.");

    if (managed)
    {
        await managedStopTask!.ConfigureAwait(false);
    }
    else
    {
        using var shutdown = new ManualResetEventSlim(initialState: false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Set(); };
        shutdown.Wait();
    }

    log.Info("uninstall requested.");
    var rawCleanupResult = await client.EvaluateAsync(
        "window.__STEAM_INPUT_ADDON_QAM__?.uninstall?.() ?? false",
        CancellationToken.None);
    var cleanupResult = CdpEvaluateResult.Parse(rawCleanupResult);

    if (!cleanupResult.Succeeded || cleanupResult.BooleanValue != true)
    {
        log.Error($"QAM cleanup failed: {cleanupResult.ErrorText ?? "uninstall() returned false"}.");
    }
    else
    {
        log.Info("cleanup completed.");
    }
}
catch (Exception ex)
{
    if (lifetimeToken.IsCancellationRequested)
    {
        await BestEffortUninstallAsync(client, log);
        log.Info("QamHost stop requested before installation completed.");
        return 0;
    }
    log.Error($"QamHost error at runtime. {ex.GetType().Name}: {ex.Message}");
    return 0;
}

return 0;

static async Task BestEffortUninstallAsync(SteamGamepadUiCdpClient client, QamHostLogger log)
{
    try
    {
        var raw = await client.EvaluateAsync("window.__STEAM_INPUT_ADDON_QAM__?.uninstall?.() ?? true", CancellationToken.None).ConfigureAwait(false);
        var result = CdpEvaluateResult.Parse(raw);
        if (!result.Succeeded || result.BooleanValue != true)
            log.Warn($"Defensive uninstall did not confirm success: {result.ErrorText ?? "returned false"}");
    }
    catch (Exception exception)
    {
        log.Warn($"Defensive uninstall failed: {exception.GetType().Name}: {exception.Message}");
    }
}
