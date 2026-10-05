using System.Xml.Linq;
using SteamInputAddonforClaw;
using SteamInputAddonforClaw.Install;
using Xunit;
using SteamInputAddonforClaw.TdpHelper;

namespace SteamInputAddonforClaw.Tests;

public sealed class ElevationConfigurationTests
{
    [Theory]
    [InlineData("SteamInputAddonforClaw", "SteamInputAddonforClaw.app", "asInvoker")]
    [InlineData("SteamInputAddonforClaw.UI", "SteamInputAddonforClaw.UI.app", "asInvoker")]
    [InlineData("SteamInputAddonforClaw.Overlay", "SteamInputAddonforClaw.Overlay.app", "asInvoker")]
    [InlineData("SteamInputAddonforClaw.TdpHelper", "SteamInputAddonforClaw.TdpHelper.app", "asInvoker")]
    [InlineData("SteamInputAddonforClaw.CenterMStartupHelper", "SteamInputAddonforClaw.CenterMStartupHelper.app", "asInvoker")]
    public void Application_manifest_has_expected_execution_level(string project, string assemblyName, string executionLevel)
    {
        var manifest = XDocument.Load(Path.Combine(RepositoryRoot(), "src", project, "app.manifest"));
        var trustInfo = manifest.Root!.Element(XName.Get("trustInfo", "urn:schemas-microsoft-com:asm.v3"));
        var requestedLevel = trustInfo!
            .Element(XName.Get("security", "urn:schemas-microsoft-com:asm.v3"))!
            .Element(XName.Get("requestedPrivileges", "urn:schemas-microsoft-com:asm.v3"))!
            .Element(XName.Get("requestedExecutionLevel", "urn:schemas-microsoft-com:asm.v3"));

        Assert.Equal(assemblyName, manifest.Root.Element(XName.Get("assemblyIdentity", "urn:schemas-microsoft-com:asm.v1"))!.Attribute("name")!.Value);
        Assert.Equal(executionLevel, requestedLevel!.Attribute("level")!.Value);
        Assert.Equal("false", requestedLevel.Attribute("uiAccess")!.Value);
        Assert.Equal("PerMonitorV2", manifest.Descendants(XName.Get("dpiAwareness", "http://schemas.microsoft.com/SMI/2016/WindowsSettings")).Single().Value);
    }

    [Fact]
    public void Tdp_helper_inherits_runtime_token_without_changing_its_fault_containment()
    {
        var root = RepositoryRoot();
        var client = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw", "Devices", "MSI", "Claw", "TdpHelperClient.cs"));
        var helper = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.TdpHelper", "Program.cs"));

        Assert.Contains("UseShellExecute = false", client);
        Assert.Contains("CreateNoWindow = true", client);
        Assert.DoesNotContain("Verb = \"runas\"", client, StringComparison.Ordinal);
        Assert.Contains("PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly", client);
        Assert.Contains("TimeSpan.FromSeconds(15)", client);
        Assert.Contains("TimeSpan.FromSeconds(10)", client);
        Assert.Contains("_process.Kill()", client);
        Assert.Contains("if (_pipe?.IsConnected == true) return;", client);
        Assert.Contains("WaitAsync(TimeSpan.FromSeconds(10))", helper);
        Assert.Contains("principal.IsInRole(WindowsBuiltInRole.Administrator)", helper);
    }

    [Fact]
    public void Center_m_helper_inherits_runtime_token_without_helper_uac_cancellation()
    {
        var root = RepositoryRoot();
        var client = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw", "CenterMStartup", "CenterMStartupHelperClient.cs"));

        Assert.Contains("UseShellExecute = false", client);
        Assert.Contains("CreateNoWindow = true", client);
        Assert.DoesNotContain("Verb = \"runas\"", client, StringComparison.Ordinal);
        Assert.DoesNotContain("CenterMStartupHelperOutcome.Cancelled", client, StringComparison.Ordinal);
        Assert.Contains("PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly", client);
        Assert.Equal(2, CountOccurrences(client, "TimeSpan.FromSeconds(30)"));
        Assert.Contains("process.Kill()", client);
    }

    [Fact]
    public void Startup_task_uses_highest_run_level_and_background_argument()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Install", "StartupRegistration.cs"));

        Assert.Contains("taskDefinition.Principal.LogonType = WindowsTaskSchedulerStartupManager.TaskLogonInteractiveToken;", source);
        Assert.Contains("taskDefinition.Principal.RunLevel = WindowsTaskSchedulerStartupManager.TaskRunLevelHighest;", source);
        Assert.Contains("state.RunLevel == TaskRunLevelHighest", source);
        Assert.Contains("settings.Priority = WindowsTaskSchedulerStartupManager.StartupTaskPriority;", source);
        Assert.Equal(1, WindowsTaskSchedulerStartupManager.TaskRunLevelHighest);
        Assert.Equal(2, WindowsTaskSchedulerStartupManager.StartupTaskPriority);
        Assert.Contains("action.Arguments = \"--background\";", source);
        // PR10 addendum section 15: an already-compliant task is proven read-only, with no rewrite.
        Assert.Contains("if (current is not null && IsCompliant(current, configuration))", source);
        Assert.Contains("StartupTaskWriteOutcome.AccessDenied", source);
        // review [P1]: the mandatory handheld Runtime task must be battery-safe with no execution limit.
        Assert.Contains("settings.DisallowStartIfOnBatteries = false;", source);
        Assert.Contains("settings.StopIfGoingOnBatteries = false;", source);
        Assert.Contains("settings.ExecutionTimeLimit = WindowsTaskSchedulerStartupManager.NoExecutionTimeLimit;", source);
    }

    [Fact]
    public void Elevation_start_info_uses_runas_and_preserves_original_arguments_and_user_sid()
    {
        var args = new[] { "--background", "--restart", "value with spaces" };
        var startInfo = Program.CreateElevationStartInfo("C:\\Apps\\SteamInputAddonforClaw.exe", args, "S-1-5-21-10");

        Assert.Equal("C:\\Apps\\SteamInputAddonforClaw.exe", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb);
        Assert.Equal(AppContext.BaseDirectory, startInfo.WorkingDirectory);
        Assert.Equal(args.Concat([Program.OriginatingUserSidArgument, "S-1-5-21-10"]), startInfo.ArgumentList);
    }

    [Fact]
    public void Originating_user_handoff_is_removed_from_runtime_arguments_and_sid_must_match()
    {
        var args = new[] { "--background", Program.OriginatingUserSidArgument, "S-1-5-21-10", "--restart" };

        Assert.True(Program.TryExtractOriginatingUserSid(args, out var originatingSid, out var runtimeArgs));
        Assert.Equal("S-1-5-21-10", originatingSid);
        Assert.Equal(["--background", "--restart"], runtimeArgs);
        Assert.True(Program.OriginatingUserSidMatches("S-1-5-21-10", "S-1-5-21-10"));
        Assert.False(Program.OriginatingUserSidMatches("S-1-5-21-10", "S-1-5-21-11"));
    }

    [Fact]
    public void Malformed_originating_user_handoff_is_rejected()
    {
        Assert.False(Program.TryExtractOriginatingUserSid([Program.OriginatingUserSidArgument], out _, out _));
        Assert.False(Program.TryExtractOriginatingUserSid([Program.OriginatingUserSidArgument, " "], out _, out _));
        Assert.False(Program.TryExtractOriginatingUserSid(
            [Program.OriginatingUserSidArgument, "S-1-5-21-10", Program.OriginatingUserSidArgument, "S-1-5-21-10"], out _, out _));
    }

    [Fact]
    public void Elevation_gate_precedes_uninstall_workers_and_single_instance()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Program.cs"));
        var velopack = source.IndexOf("VelopackApp.Build()", StringComparison.Ordinal);
        var approvedUninstallFastPath = source.IndexOf("if (UninstallBootstrap.IsSafeUninstallApproved)", StringComparison.Ordinal);
        var elevationGate = source.IndexOf("TryExtractOriginatingUserSid(args", StringComparison.Ordinal);
        var sidValidation = source.IndexOf("OriginatingUserSidMatches(originatingUserSid, currentUserSid)", StringComparison.Ordinal);
        var sidMismatchLog = source.IndexOf("Elevated Runtime user does not match the originating interactive user", StringComparison.Ordinal);
        var sidMismatchExit = sidMismatchLog < 0 ? -1 : source.IndexOf("return;", sidMismatchLog, StringComparison.Ordinal);
        var cancellationLog = source.IndexOf("Runtime elevation was cancelled; normal Runtime startup is blocked.", StringComparison.Ordinal);
        var cancellationExit = cancellationLog < 0 ? -1 : source.IndexOf("return;", cancellationLog, StringComparison.Ordinal);
        var failureLog = source.IndexOf("Runtime elevation failed; normal Runtime startup is blocked.", StringComparison.Ordinal);
        var failureExit = failureLog < 0 ? -1 : source.IndexOf("return;", failureLog, StringComparison.Ordinal);
        var elevationCheck = source.IndexOf("if (!IsCurrentProcessElevated())", elevationGate, StringComparison.Ordinal);
        var safeUninstallDispatch = source.IndexOf("Environment.ExitCode = SafeUninstall.Run(", StringComparison.Ordinal);
        var safeUninstallExit = safeUninstallDispatch < 0 ? -1 : source.IndexOf("return;", safeUninstallDispatch, StringComparison.Ordinal);
        var fseWorker = source.IndexOf("SteamFseRegistrationWorker.Run()", StringComparison.Ordinal);
        var prerequisiteWorker = source.IndexOf("PrerequisiteSetupWorker.Run()", StringComparison.Ordinal);
        var windowsAppRuntimeWorker = source.IndexOf("WindowsAppRuntimeSetupWorker.Run()", StringComparison.Ordinal);
        var singleInstance = source.IndexOf("SingleInstanceGate singleInstanceGate", StringComparison.Ordinal);
        var pendingUpdate = source.IndexOf("TrySchedulePendingUpdateApply(runtimeArgs)", StringComparison.Ordinal);
        var runtimeEntry = source.IndexOf("new RuntimeProcessApplication(runtimeArgs", StringComparison.Ordinal);

        Assert.True(velopack >= 0 && velopack < approvedUninstallFastPath);
        Assert.True(approvedUninstallFastPath < elevationGate);
        Assert.True(elevationGate < sidValidation && sidValidation < sidMismatchLog && sidMismatchLog < sidMismatchExit);
        Assert.True(cancellationLog < cancellationExit && cancellationExit < safeUninstallDispatch);
        Assert.True(failureLog < failureExit && failureExit < safeUninstallDispatch);
        Assert.True(elevationCheck < safeUninstallDispatch && safeUninstallDispatch < safeUninstallExit);
        Assert.True(safeUninstallExit < fseWorker && fseWorker < prerequisiteWorker && prerequisiteWorker < windowsAppRuntimeWorker);
        Assert.True(windowsAppRuntimeWorker < singleInstance);
        Assert.True(elevationGate < singleInstance);
        Assert.True(singleInstance < pendingUpdate);
        Assert.True(pendingUpdate < runtimeEntry);
        Assert.Contains("exception.NativeErrorCode == ElevationCancelledErrorCode", source);
        Assert.Contains("Runtime elevation was cancelled; normal Runtime startup is blocked.", source);
        Assert.Contains("Runtime elevation failed; normal Runtime startup is blocked.", source);
        Assert.DoesNotContain("--ensure-startup-task", source, StringComparison.Ordinal);
        Assert.DoesNotContain("--uninstall-owned-prerequisites", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ElevatedStartupTaskSetup", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Safe_uninstall_cleans_owned_prerequisites_in_process_after_stock_safety_proof()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Install", "SafeUninstall.cs"));
        var stockSafetyProof = source.IndexOf("Independent PR12 stock-safety proof", StringComparison.Ordinal);
        var ownedCleanup = source.IndexOf("new OwnedPrerequisiteUninstall().Execute()", StringComparison.Ordinal);
        var localCleanup = source.IndexOf("UninstallBootstrap.RunBoundedLocalCleanup", StringComparison.Ordinal);
        var finalHandoff = source.IndexOf("PreserveUserDataAndLaunchVeloPack(", StringComparison.Ordinal);

        Assert.True(stockSafetyProof >= 0 && stockSafetyProof < ownedCleanup);
        Assert.True(ownedCleanup < localCleanup && localCleanup < finalHandoff);
        Assert.DoesNotContain("Verb = \"runas\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ElevatedOwnedPrerequisiteUninstallEntry", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_priority_is_task_scheduler_only_and_not_a_runtime_priority_mutation()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Install", "StartupRegistration.cs"));

        Assert.DoesNotContain("ProcessPriorityClass", source);
        Assert.DoesNotContain("SetPriorityClass", source);
    }

    [Fact]
    public void Wmi_fallback_diagnostics_are_preserved_on_the_production_helper_path()
    {
        var helper = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw.TdpHelper", "Program.cs"));
        var client = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Devices", "MSI", "Claw", "TdpHelperClient.cs"));
        Assert.Contains("WMI_INVOKE_FAIL", helper);
        Assert.Contains("UsedFallback", helper);
        Assert.Contains("fallbackCause", helper);
        Assert.Contains("GetMethodParameters", helper);
        Assert.Contains("Profiles.Tdp.Wmi", client);
        Assert.Contains("GetWmiFallback", client);
        Assert.Contains("response.ExceptionType", client);
        Assert.Contains("response.ManagementStatus", client);
    }

    [Fact]
    public void Tdp_helper_preserves_the_wmi_compatibility_fallback_and_narrow_protocol()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw.TdpHelper", "Program.cs"));
        Assert.Contains("Invoke(\"Get_WMI\", block: 1", source);
        Assert.Contains("TdpHelperProtocol.IsSupported", source);
        Assert.Contains("PRE_WMI_PROTOCOL_FAIL", source);
    }

    [Fact]
    public void Fan_diagnostic_helper_reports_elevation_and_bounded_read_allow_list()
    {
        var helper = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw.TdpHelper", "Program.cs"));
        var protocol = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw.TdpHelper", "TdpHelperProtocol.cs"));
        var frontend = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Frontend", "InProcessAddonFrontendControl.cs"));
        Assert.Contains("WindowsPrincipal", helper);
        Assert.Contains("HelperPid", helper);
        Assert.Contains("GetMethodInventory", helper);
        Assert.Contains("GetThermal", protocol);
        Assert.Contains("index is 152 or 210 or 212 or 215", protocol);
        Assert.Contains("GetWmiVersion\" => index == 1", protocol);
        Assert.Contains("BIOSVersion", frontend);
        Assert.Contains("EC: unavailable", frontend);
        Assert.Contains("WMI_INVOKE_FAIL", helper);
    }

    [Theory]
    [InlineData("GetAp", "Get_AP")]
    [InlineData("SetData", "Set_Data")]
    public void Tdp_helper_maps_ipc_operations_to_the_real_wmi_methods(string operation, string wmiMethod) =>
        Assert.Equal(wmiMethod, TdpHelperProtocol.GetWmiMethod(operation));

    [Theory]
    [InlineData("GetAp", 0, true)]
    [InlineData("GetAp", 80, false)]
    [InlineData("SetData", 80, true)]
    [InlineData("SetData", 81, true)]
    [InlineData("SetData", 210, true)]
    [InlineData("SetData", 215, true)]
    [InlineData("GetData", 215, true)]
    [InlineData("GetData", 214, false)]
    [InlineData("GetData", 216, false)]
    [InlineData("SetData", 214, false)]
    [InlineData("SetData", 216, false)]
    [InlineData("SetData", 1, false)]
    public void Tdp_helper_rejects_unsupported_privileged_blocks(string operation, int index, bool expected) =>
        Assert.Equal(expected, TdpHelperProtocol.IsSupported(operation, index));

    [Fact]
    public void Runtime_uses_one_owned_helper_transport_for_the_tdp_runtime()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs"));
        Assert.Contains("_tdpTransport = new();", source);
        Assert.Contains("new MsiClawTdpHardware(_tdpTransport)", source);
        Assert.Contains("await _tdpTransport.DisposeAsync()", source);
    }

    [Fact]
    public void Tdp_helper_request_and_response_paths_are_time_bounded()
    {
        var client = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Devices", "MSI", "Claw", "TdpHelperClient.cs"));
        var helper = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw.TdpHelper", "Program.cs"));
        Assert.Contains("ReadLineAsync(responseTimeout.Token)", client);
        Assert.Contains("WaitAsync(TimeSpan.FromSeconds(10))", helper);
        Assert.Contains("return;", helper);
    }

    [Fact]
    public void Runtime_owns_the_pipe_and_helper_only_connects()
    {
        var client = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Devices", "MSI", "Claw", "TdpHelperClient.cs"));
        var helper = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw.TdpHelper", "Program.cs"));
        var normalizedClient = client.ReplaceLineEndings("\n");
        Assert.Contains("new NamedPipeServerStream", client);
        Assert.Contains("PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly", client);
        Assert.Contains("new NamedPipeClientStream", helper);
        Assert.Contains("await server.ConnectAsync(connectTimeout.Token)", helper);
        Assert.DoesNotContain("new NamedPipeServerStream", helper);
        Assert.True(client.IndexOf("new NamedPipeServerStream", StringComparison.Ordinal)
            < client.IndexOf("Process.Start", StringComparison.Ordinal));
        Assert.Contains("catch\n        {\n            CloseUnderLock();\n            throw;", normalizedClient);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static int CountOccurrences(string value, string match)
    {
        var count = 0;
        for (var offset = 0; (offset = value.IndexOf(match, offset, StringComparison.Ordinal)) >= 0; offset += match.Length)
            count++;
        return count;
    }
}
