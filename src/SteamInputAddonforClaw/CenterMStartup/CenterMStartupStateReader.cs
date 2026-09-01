using System.Management;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterMStartup;

/// <summary>Configured startup mode of the MSI Foundation Service. The exact mode is preserved --
/// <see cref="Other"/> (e.g. <c>Manual</c>) is deliberately NOT folded into "enabled", because this
/// feature's Enable target is specifically <see cref="Automatic"/> and success is exact read-back
/// verification (PR #430 review). Kept in sync with the helper's own <c>ServiceMode</c> enum, which
/// is serialised by name over the pipe.</summary>
internal enum CenterMFoundationServiceMode { Automatic, Disabled, Other, Unavailable }

/// <summary>Non-elevated, read-only inspection of the three MSI Center M startup roots (work order
/// PR1 section 7). Reading Scheduled Task enabled state and the service's configured start type does
/// not need administrator rights, so the Device page can always render a status without prompting for
/// UAC -- elevation is required only to <em>change</em> them (that path lives in the helper).
///
/// The service authority is the configured <c>StartMode</c>, never whether the service is currently
/// Running (Addendum F).</summary>
internal sealed class CenterMStartupStateReader
{
    internal const string ServerTaskName = "MSI_Center_M_Server";
    internal const string UpdaterTaskName = "MSI_Center_M_Updater";
    internal const string FoundationServiceName = "MSI Foundation Service";

    private readonly Func<string, bool?> _readTaskEnabled;
    private readonly Func<CenterMFoundationServiceMode> _readServiceMode;

    internal CenterMStartupStateReader()
        : this(ReadTaskEnabledViaComObject, ReadFoundationServiceModeViaWmi) { }

    internal CenterMStartupStateReader(Func<string, bool?> readTaskEnabled, Func<CenterMFoundationServiceMode> readServiceMode)
    {
        _readTaskEnabled = readTaskEnabled;
        _readServiceMode = readServiceMode;
    }

    /// <summary>Reads all three roots. Returns false when any one could not be identified/read -- the
    /// caller maps that to <see cref="Contracts.Frontend.FrontendCenterMStartupState.Unavailable"/>,
    /// never to a guessed Enabled/Disabled.</summary>
    internal bool TryRead(out bool serverTask, out bool updaterTask, out CenterMFoundationServiceMode foundationService, out string? failure)
    {
        var server = _readTaskEnabled(ServerTaskName);
        var updater = _readTaskEnabled(UpdaterTaskName);
        foundationService = _readServiceMode();
        serverTask = server ?? false;
        updaterTask = updater ?? false;

        var missing = new List<string>();
        if (server is null) missing.Add(ServerTaskName);
        if (updater is null) missing.Add(UpdaterTaskName);
        if (foundationService == CenterMFoundationServiceMode.Unavailable) missing.Add(FoundationServiceName);
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

    private static CenterMFoundationServiceMode ReadFoundationServiceModeViaWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT StartMode FROM Win32_Service WHERE Name = 'MSI Foundation Service' OR DisplayName = 'MSI Foundation Service'");
            foreach (var item in searcher.Get())
            {
                using var service = (ManagementObject)item;
                return (service["StartMode"] as string) switch
                {
                    "Auto" => CenterMFoundationServiceMode.Automatic,
                    "Disabled" => CenterMFoundationServiceMode.Disabled,
                    null => CenterMFoundationServiceMode.Unavailable,
                    _ => CenterMFoundationServiceMode.Other,
                };
            }
            return CenterMFoundationServiceMode.Unavailable;
        }
        catch (ManagementException) { return CenterMFoundationServiceMode.Unavailable; }
        catch (COMException) { return CenterMFoundationServiceMode.Unavailable; }
        catch (UnauthorizedAccessException) { return CenterMFoundationServiceMode.Unavailable; }
    }
}
