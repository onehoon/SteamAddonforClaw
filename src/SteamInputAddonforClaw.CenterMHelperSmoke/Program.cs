// CI-only smoke check (see the .csproj header comment): stages the real published helper
// artifact under its final filename, starts it through the real CenterMHelperOwnership Win32
// path, proves it stays alive, then stops it through retained ownership and confirms exit.
// Exit code 0 = pass, 1 = fail. Never touches production startup/composition.
using SteamInputAddonforClaw.CenterM;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: SteamInputAddonforClaw.CenterMHelperSmoke <publish-or-output-root>");
    return 1;
}

var publishRoot = args[0];
var sourceBinary = Path.Combine(publishRoot, "CenterMHelperSource", "CenterMHelper.exe");
if (!File.Exists(sourceBinary))
{
    Console.Error.WriteLine($"FAIL: helper publish output not found at '{sourceBinary}'.");
    return 1;
}

var stagedPath = CenterMHelperStaging.StageFromPublishRoot(publishRoot);
if (stagedPath is null)
{
    Console.Error.WriteLine("FAIL: CenterMHelperStaging.StageFromPublishRoot returned null.");
    return 1;
}
if (Path.GetFileName(stagedPath) != "MSI Center M.exe")
{
    Console.Error.WriteLine($"FAIL: staged artifact has unexpected filename '{Path.GetFileName(stagedPath)}'.");
    return 1;
}
Console.WriteLine($"Staged: {stagedPath}");

using var ownership = new CenterMHelperOwnership();
var startResult = ownership.Start(stagedPath);
if (startResult != HelperStartResult.Started)
{
    Console.Error.WriteLine($"FAIL: Start returned {startResult}.");
    return 1;
}
if (ownership.ProcessId is not { } processId)
{
    Console.Error.WriteLine("FAIL: Start reported Started but ProcessId is null.");
    return 1;
}
Console.WriteLine($"Started: PID {processId}");

bool stayedAlive;
using (var process = System.Diagnostics.Process.GetProcessById(processId))
{
    // Bounded wait, not an immediate check: proves the apphost actually finished its own managed
    // startup (loading the bundled runtime, running Main), not merely that CreateProcess returned
    // success for a process that will fault moments later.
    Thread.Sleep(1000);
    stayedAlive = !process.HasExited;
}

if (!stayedAlive)
{
    Console.Error.WriteLine("FAIL: helper process exited before the liveness check completed.");
    return 1;
}
Console.WriteLine("Alive after 1000ms: OK");

var stopped = ownership.Stop(TimeSpan.FromSeconds(5));
if (!stopped)
{
    Console.Error.WriteLine("FAIL: Stop did not confirm the helper's termination.");
    return 1;
}
if (ownership.IsOwned)
{
    Console.Error.WriteLine("FAIL: ownership still reports IsOwned after Stop.");
    return 1;
}

Console.WriteLine("PASS: staged helper artifact started, stayed alive, and stopped cleanly via retained ownership.");
return 0;
