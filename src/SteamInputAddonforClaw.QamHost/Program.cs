using SteamInputAddonforClaw.QamHost;

// Development-only bootstrap for PR1: assumes Steam is already running with CEF remote
// debugging enabled on the default DevTools endpoint. This is NOT a production Steam bootstrap
// contract; it will be replaced by a private CDP pipe connection in a later PR. QamHost never
// starts, stops, or patches Steam itself.
var devToolsEndpoint = new Uri("http://127.0.0.1:8080");
var frontendPath = Path.Combine(AppContext.BaseDirectory, "Frontend", "qam.js");

Console.WriteLine("QamHost starting.");

if (!File.Exists(frontendPath))
{
    Console.WriteLine($"QAM frontend script not found at {frontendPath}; exiting.");
    return 1;
}

var frontendScript = await File.ReadAllTextAsync(frontendPath);

await using var client = new SteamGamepadUiCdpClient(devToolsEndpoint);

IReadOnlyList<CdpTarget> targets;
try
{
    targets = await client.ListTargetsAsync(CancellationToken.None);
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
{
    Console.WriteLine($"DevTools endpoint unavailable ({ex.GetType().Name}: {ex.Message}). Is Steam running with remote debugging enabled? Exiting cleanly.");
    return 0;
}

Console.WriteLine($"target count: {targets.Count}");

var gamepadUiTarget = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);
if (gamepadUiTarget is null)
{
    Console.WriteLine("GamepadUI target not found. Exiting cleanly; Steam and the Addon Runtime are unaffected.");
    return 0;
}

Console.WriteLine($"GamepadUI target selected: {gamepadUiTarget.Title} ({gamepadUiTarget.Url})");

try
{
    await client.ConnectAsync(gamepadUiTarget, CancellationToken.None);
    Console.WriteLine("CDP connected.");

    var rawEvalResult = await client.EvaluateAsync(frontendScript, CancellationToken.None);
    var evalResult = CdpEvaluateResult.Parse(rawEvalResult);

    if (!evalResult.Succeeded)
    {
        Console.WriteLine($"frontend injection failed: {evalResult.ErrorText}. Exiting cleanly.");
        return 0;
    }

    if (evalResult.BooleanValue != true)
    {
        Console.WriteLine("QAM integration unavailable (install() reported failure). Exiting cleanly.");
        return 0;
    }

    Console.WriteLine("frontend injection succeeded.");
    Console.WriteLine("QAM hook installed.");

    Console.WriteLine("Press Ctrl+C to stop QamHost and clean up the QAM hook.");
    using var shutdown = new ManualResetEventSlim(initialState: false);
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        shutdown.Set();
    };
    shutdown.Wait();

    Console.WriteLine("cleanup requested.");
    var rawCleanupResult = await client.EvaluateAsync(
        "window.__STEAM_INPUT_ADDON_QAM__?.uninstall?.() ?? false",
        CancellationToken.None);
    var cleanupResult = CdpEvaluateResult.Parse(rawCleanupResult);

    if (!cleanupResult.Succeeded || cleanupResult.BooleanValue != true)
    {
        Console.WriteLine($"QAM cleanup failed: {cleanupResult.ErrorText ?? "uninstall() returned false"}.");
    }
    else
    {
        Console.WriteLine("cleanup completed.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"QamHost error: {ex.GetType().Name}: {ex.Message}. Exiting cleanly.");
    return 0;
}

return 0;
