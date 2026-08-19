using SteamInputAddonforClaw.QamHost;

var managed = args.Contains("--managed", StringComparer.OrdinalIgnoreCase);
string? logDirectory = null;
for (var index = 0; index < args.Length - 1; index++)
    if (string.Equals(args[index], "--log-directory", StringComparison.OrdinalIgnoreCase)) logDirectory = args[index + 1];
using var log = new QamHostLogger(logDirectory);
log.Info($"QamHost starting. ManagedMode={managed}. DevToolsEndpoint=http://127.0.0.1:8080");

// Development-only bootstrap for PR1: assumes Steam is already running with CEF remote
// debugging enabled on the default DevTools endpoint. This is NOT a production Steam bootstrap
// contract; it will be replaced by a private CDP pipe connection in a later PR. QamHost never
// starts, stops, or patches Steam itself.
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

CdpTarget? gamepadUiTarget = null;
try
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(managed ? 10 : 0);
    while (true)
    {
        IReadOnlyList<CdpTarget> targets;
        try { targets = await client.ListTargetsAsync(CancellationToken.None); }
        catch (Exception ex) when (managed && ex is HttpRequestException or TaskCanceledException && DateTimeOffset.UtcNow < deadline)
        { await Task.Delay(250); continue; }

        log.Info($"target count={targets.Count}");
        foreach (var target in targets) log.Info($"Target Type={target.Type} Title={target.Title} Url={target.Url}");
        gamepadUiTarget = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);
        if (gamepadUiTarget is not null || !managed || DateTimeOffset.UtcNow >= deadline) break;
        await Task.Delay(250);
    }
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
{
    log.Warn($"DevTools endpoint unavailable. {ex.GetType().Name}: {ex.Message}");
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
    await client.ConnectAsync(gamepadUiTarget, CancellationToken.None);
    log.Info("CDP connected.");

    var rawEvalResult = await client.EvaluateAsync(frontendScript, CancellationToken.None);
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

    log.Info("Frontend evaluation succeeded. QAM hook installed.");

    if (managed)
    {
        var managedStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            while (await Console.In.ReadLineAsync() is { } line && !string.Equals(line.Trim(), "stop", StringComparison.OrdinalIgnoreCase)) { }
            managedStop.TrySetResult();
        });
        await managedStop.Task;
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
    log.Error($"QamHost error at runtime. {ex.GetType().Name}: {ex.Message}");
    return 0;
}

return 0;
