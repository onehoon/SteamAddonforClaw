// Privileged MSI Center M startup Enable/Disable helper (work order PR1 / PR1 Addendum A).
//
// Scope, deliberately tiny:
//   * enable/disable Scheduled Task  MSI_Center_M_Server
//   * enable/disable Scheduled Task  MSI_Center_M_Updater
//   * set service  "MSI Foundation Service"  startup type to Automatic or Disabled
// It never stops/starts tasks, services, or processes; it changes startup configuration only. The
// running Center M session is intentionally left alone -- the clean baseline begins after reboot
// (work order PR1 sections 1/2/12). No decoy behaviour, no controller-suppression, no watchdog.
//
// Packaging mirrors SteamInputAddonforClaw.TdpHelper: requireAdministrator manifest, launched with
// Verb="runas" by the Runtime, one named-pipe request, one JSON result line, then exit.
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.Json;

const string ServerTaskName = "MSI_Center_M_Server";
const string UpdaterTaskName = "MSI_Center_M_Updater";
const string FoundationServiceName = "MSI Foundation Service";

if (args.Length != 1) return;

using var pipe = new NamedPipeClientStream(".", args[0], PipeDirection.InOut, PipeOptions.Asynchronous);
using (var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
{
    try { await pipe.ConnectAsync(connectTimeout.Token); }
    catch (OperationCanceledException) when (connectTimeout.IsCancellationRequested) { return; }
}
using var reader = new StreamReader(pipe);
using var writer = new StreamWriter(pipe) { AutoFlush = true };

var line = await reader.ReadLineAsync();
if (line is null) return;

Response response;
try
{
    var request = JsonSerializer.Deserialize<Request>(line) ?? throw new InvalidOperationException("Empty request.");
    response = request.Operation switch
    {
        "Capture" => Capture(),
        "SetEnabled" => SetEnabled(request.Enabled),
        _ => Failure("UnknownOperation"),
    };
}
catch (Exception exception)
{
    response = Failure(exception.GetType().Name);
}
await writer.WriteLineAsync(JsonSerializer.Serialize(response));

static Response Capture()
{
    var server = ReadTaskEnabled(ServerTaskName);
    var updater = ReadTaskEnabled(UpdaterTaskName);
    var service = ReadServiceEnabled();
    var missing = new List<string>();
    if (server is null) missing.Add(ServerTaskName);
    if (updater is null) missing.Add(UpdaterTaskName);
    if (service is null) missing.Add(FoundationServiceName);
    return new Response(
        missing.Count == 0,
        server ?? false, updater ?? false, service ?? false,
        missing.Count == 0 ? null : "Unreadable: " + string.Join(", ", missing));
}

static Response SetEnabled(bool enabled)
{
    var failures = new List<string>();
    if (!TrySetTaskEnabled(ServerTaskName, enabled)) failures.Add(ServerTaskName);
    if (!TrySetTaskEnabled(UpdaterTaskName, enabled)) failures.Add(UpdaterTaskName);
    if (!TrySetServiceStartup(enabled)) failures.Add(FoundationServiceName);

    // Section 8 / Addendum F: success is decided by reading the resulting Windows state back, never
    // by whether the setter calls threw.
    var server = ReadTaskEnabled(ServerTaskName);
    var updater = ReadTaskEnabled(UpdaterTaskName);
    var service = ReadServiceEnabled();
    var verified = server == enabled && updater == enabled && service == enabled;
    var message = verified
        ? null
        : failures.Count > 0
            ? "Write failed: " + string.Join(", ", failures)
            : "Read-back did not match the requested configuration.";
    return new Response(verified, server ?? false, updater ?? false, service ?? false, message);
}

// ---- Scheduled Tasks (Task Scheduler COM, matched by name anywhere in the tree) ----
static bool? ReadTaskEnabled(string taskName)
{
    try
    {
        var task = FindTask(taskName);
        return task is null ? null : (bool)task.Enabled;
    }
    catch (COMException) { return null; }
    catch (UnauthorizedAccessException) { return null; }
}

static bool TrySetTaskEnabled(string taskName, bool enabled)
{
    try
    {
        var task = FindTask(taskName);
        if (task is null) return false;
        task.Enabled = enabled;
        return true;
    }
    catch (COMException) { return false; }
    catch (UnauthorizedAccessException) { return false; }
}

static dynamic? FindTask(string taskName)
{
    var serviceType = Type.GetTypeFromProgID("Schedule.Service")
        ?? throw new InvalidOperationException("Task Scheduler is not available.");
    dynamic service = Activator.CreateInstance(serviceType)
        ?? throw new InvalidOperationException("Task Scheduler could not be created.");
    service.Connect();
    return SearchFolder(service.GetFolder("\\"), taskName);
}

static dynamic? SearchFolder(dynamic folder, string taskName)
{
    const int TASK_ENUM_HIDDEN = 1;
    foreach (dynamic task in folder.GetTasks(TASK_ENUM_HIDDEN))
        if (string.Equals((string)task.Name, taskName, StringComparison.OrdinalIgnoreCase))
            return task;
    foreach (dynamic child in folder.GetFolders(0))
    {
        var found = SearchFolder(child, taskName);
        if (found is not null) return found;
    }
    return null;
}

// ---- MSI Foundation Service startup type ----
static bool? ReadServiceEnabled()
{
    try
    {
        using var controller = new ServiceController(FoundationServiceName);
        // Configured start type is the authority, never Running/Stopped (Addendum F).
        return controller.StartType != ServiceStartMode.Disabled;
    }
    catch (InvalidOperationException) { return null; }
    catch (Win32Exception) { return null; }
}

static bool TrySetServiceStartup(bool enabled)
{
    const uint SERVICE_ALL_ACCESS = 0xF01FF;
    const uint SC_MANAGER_CONNECT = 0x0001;
    const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    const uint SERVICE_AUTO_START = 0x00000002;
    const uint SERVICE_DISABLED = 0x00000004;

    var manager = OpenSCManager(null, null, SC_MANAGER_CONNECT);
    if (manager == IntPtr.Zero) return false;
    try
    {
        var service = OpenService(manager, FoundationServiceName, SERVICE_ALL_ACCESS);
        if (service == IntPtr.Zero) return false;
        try
        {
            return ChangeServiceConfig(service, SERVICE_NO_CHANGE,
                enabled ? SERVICE_AUTO_START : SERVICE_DISABLED,
                SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null);
        }
        finally { CloseServiceHandle(service); }
    }
    finally { CloseServiceHandle(manager); }
}

static Response Failure(string reason) => new(false, false, false, false, reason);

[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint access);

[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern bool ChangeServiceConfig(IntPtr service, uint serviceType, uint startType, uint errorControl,
    string? binaryPathName, string? loadOrderGroup, IntPtr tagId, string? dependencies,
    string? serviceStartName, string? password, string? displayName);

[DllImport("advapi32.dll", SetLastError = true)]
static extern bool CloseServiceHandle(IntPtr handle);

internal sealed record Request(string Operation, bool Enabled = false);
internal sealed record Response(bool Ok, bool ServerTaskEnabled, bool UpdaterTaskEnabled, bool FoundationServiceEnabled, string? Error);
