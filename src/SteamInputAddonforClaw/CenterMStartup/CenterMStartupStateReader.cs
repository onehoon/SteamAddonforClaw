using System.Management;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterMStartup;

/// <summary>Non-elevated, read-only inspection of the three MSI Center M startup roots (work order
/// PR1 section 7). Reading Scheduled Task enabled state and the service's configured start type does
/// not need administrator rights, so the Device page can always render a status without prompting for
/// UAC -- elevation is required only to <em>change</em> them (that path lives in the helper).
///
/// The service authority is the configured <c>StartMode</c> (<c>Auto</c>/<c>Manual</c> =&gt; enabled,
/// <c>Disabled</c> =&gt; disabled), never whether the service is currently Running (Addendum F).</summary>
internal sealed class CenterMStartupStateReader
{
    internal const string ServerTaskName = "MSI_Center_M_Server";
    internal const string UpdaterTaskName = "MSI_Center_M_Updater";
    internal const string FoundationServiceName = "MSI Foundation Service";

    private readonly Func<string, bool?> _readTaskEnabled;
    private readonly Func<bool?> _readServiceEnabled;

    internal CenterMStartupStateReader()
        : this(ReadTaskEnabledViaComObject, ReadFoundationServiceEnabledViaWmi) { }

    internal CenterMStartupStateReader(Func<string, bool?> readTaskEnabled, Func<bool?> readServiceEnabled)
    {
        _readTaskEnabled = readTaskEnabled;
        _readServiceEnabled = readServiceEnabled;
    }

    /// <summary>Reads all three roots. Returns false when any one could not be identified/read -- the
    /// caller maps that to <see cref="Contracts.Frontend.FrontendCenterMStartupState.Unavailable"/>,
    /// never to a guessed Enabled/Disabled.</summary>
    internal bool TryRead(out bool serverTask, out bool updaterTask, out bool foundationService, out string? failure)
    {
        var server = _readTaskEnabled(ServerTaskName);
        var updater = _readTaskEnabled(UpdaterTaskName);
        var service = _readServiceEnabled();
        serverTask = server ?? false;
        updaterTask = updater ?? false;
        foundationService = service ?? false;

        var missing = new List<string>();
        if (server is null) missing.Add(ServerTaskName);
        if (updater is null) missing.Add(UpdaterTaskName);
        if (service is null) missing.Add(FoundationServiceName);
        failure = missing.Count == 0 ? null
            : "MSI Center M startup components could not be identified: " + string.Join(", ", missing);
        return missing.Count == 0;
    }

    private static bool? ReadTaskEnabledViaComObject(string taskName)
    {
        try
        {
            var serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType is null) return null;
            dynamic? scheduler = Activator.CreateInstance(serviceType);
            if (scheduler is null) return null;
            scheduler.Connect();
            return SearchFolder(scheduler.GetFolder("\\"), taskName);
        }
        catch (COMException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static bool? SearchFolder(dynamic folder, string taskName)
    {
        const int TaskEnumHidden = 1;
        foreach (dynamic task in folder.GetTasks(TaskEnumHidden))
            if (string.Equals((string)task.Name, taskName, StringComparison.OrdinalIgnoreCase))
                return (bool)task.Enabled;
        foreach (dynamic child in folder.GetFolders(0))
        {
            var found = SearchFolder(child, taskName);
            if (found is not null) return found;
        }
        return null;
    }

    private static bool? ReadFoundationServiceEnabledViaWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT StartMode FROM Win32_Service WHERE Name = 'MSI Foundation Service' OR DisplayName = 'MSI Foundation Service'");
            foreach (var item in searcher.Get())
            {
                using var service = (ManagementObject)item;
                return (service["StartMode"] as string) is not "Disabled";
            }
            return null;
        }
        catch (ManagementException) { return null; }
        catch (COMException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
