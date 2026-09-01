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

static Response Capture() => Build(
    ReadTaskEnabled(ServerTaskName), ReadTaskEnabled(UpdaterTaskName), ReadServiceMode(),
    enabled: null, wrote: null);

static Response SetEnabled(bool enabled)
{
    var failures = new List<string>();
    if (!TrySetTaskEnabled(ServerTaskName, enabled)) failures.Add(ServerTaskName);
    if (!TrySetTaskEnabled(UpdaterTaskName, enabled)) failures.Add(UpdaterTaskName);
    if (!TrySetServiceStartup(enabled)) failures.Add(FoundationServiceName);

    // Section 8 / Addendum F: success is decided by reading the resulting Windows state back (the
    // exact configured mode -- Automatic for Enable, Disabled for Disable), never by whether the
    // setter calls threw.
    return Build(ReadTaskEnabled(ServerTaskName), ReadTaskEnabled(UpdaterTaskName), ReadServiceMode(),
        enabled: enabled, wrote: failures);
}

static Response Build(bool? server, bool? updater, ServiceMode service, bool? enabled, List<string>? wrote)
{
    // "Not observed" (null / Unavailable) is never treated as "observed disabled" (Addendum E).
    var snapshotAvailable = server is not null && updater is not null && service != ServiceMode.Unavailable;

    bool verified;
    string? message;
    if (enabled is bool target)
    {
        var expectedService = target ? ServiceMode.Automatic : ServiceMode.Disabled;
        verified = server == target && updater == target && service == expectedService;
        message = verified ? null
            : wrote is { Count: > 0 } ? "Write failed: " + string.Join(", ", wrote)
            : "Read-back did not match the requested configuration.";
    }
    else
    {
        verified = snapshotAvailable;
        message = snapshotAvailable ? null : "One or more MSI Center M startup components could not be read.";
    }

    return new Response(verified, snapshotAvailable, server ?? false, updater ?? false, service.ToString(), message);
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
// The exact configured mode is preserved -- Manual is NOT "enabled" for this feature, whose Enable
// target is specifically Automatic (review: do not collapse to StartType != Disabled).
static ServiceMode ReadServiceMode()
{
    try
    {
        using var controller = new ServiceController(FoundationServiceName);
        return controller.StartType switch
        {
            ServiceStartMode.Automatic => ServiceMode.Automatic,
            ServiceStartMode.Disabled => ServiceMode.Disabled,
            _ => ServiceMode.Other,
        };
    }
    catch (InvalidOperationException) { return ServiceMode.Unavailable; }
    catch (Win32Exception) { return ServiceMode.Unavailable; }
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

static Response Failure(string reason) => new(false, false, false, false, ServiceMode.Unavailable.ToString(), reason);

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

// Wire-serialised by name; the Runtime keeps a matching enum (CenterMFoundationServiceMode).
internal enum ServiceMode { Automatic, Disabled, Other, Unavailable }
internal sealed record Request(string Operation, bool Enabled = false);
internal sealed record Response(
    bool Ok,
    bool SnapshotAvailable,
    bool ServerTaskEnabled,
    bool UpdaterTaskEnabled,
    string FoundationServiceMode,
    string? Error);
