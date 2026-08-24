using System.Xml.Linq;
using Xunit;
using SteamInputAddonforClaw.TdpHelper;

namespace SteamInputAddonforClaw.Tests;

public sealed class ElevationConfigurationTests
{
    [Theory]
    [InlineData("SteamInputAddonforClaw", "SteamInputAddonforClaw.app", "asInvoker")]
    [InlineData("SteamInputAddonforClaw.UI", "SteamInputAddonforClaw.UI.app", "asInvoker")]
    [InlineData("SteamInputAddonforClaw.TdpHelper", "SteamInputAddonforClaw.TdpHelper.app", "requireAdministrator")]
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
    public void Startup_task_uses_highest_run_level_and_background_argument()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Install", "StartupRegistration.cs"));

        Assert.Contains("taskDefinition.Principal.LogonType = TaskLogonInteractiveToken;", source);
        Assert.Contains("taskDefinition.Principal.RunLevel = 0;", source);
        Assert.Contains("action.Arguments = \"--background\";", source);
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
        Assert.Contains("obj.InvokeMethod(\"Get_WMI\", null, null)", source);
        Assert.Contains("TdpHelperProtocol.IsSupported", source);
        Assert.Contains("PRE_WMI_PROTOCOL_FAIL", source);
    }

    [Fact]
    public void Fan_diagnostic_helper_reports_elevation_and_bounded_read_allow_list()
    {
        var helper = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw.TdpHelper", "Program.cs"));
        var protocol = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw.TdpHelper", "TdpHelperProtocol.cs"));
        Assert.Contains("WindowsPrincipal", helper);
        Assert.Contains("HelperPid", helper);
        Assert.Contains("GetMethodInventory", helper);
        Assert.Contains("GetThermal", protocol);
        Assert.Contains("index is 152 or 210 or 212", protocol);
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
    public void Runtime_owns_the_medium_integrity_pipe_and_helper_only_connects()
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
}
