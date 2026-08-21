using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ElevationConfigurationTests
{
    [Theory]
    [InlineData("SteamInputAddonforClaw", "SteamInputAddonforClaw.app")]
    [InlineData("SteamInputAddonforClaw.UI", "SteamInputAddonforClaw.UI.app")]
    public void Application_manifest_requires_administrator_without_ui_access(string project, string assemblyName)
    {
        var manifest = XDocument.Load(Path.Combine(RepositoryRoot(), "src", project, "app.manifest"));
        var trustInfo = manifest.Root!.Element(XName.Get("trustInfo", "urn:schemas-microsoft-com:asm.v3"));
        var requestedLevel = trustInfo!
            .Element(XName.Get("security", "urn:schemas-microsoft-com:asm.v3"))!
            .Element(XName.Get("requestedPrivileges", "urn:schemas-microsoft-com:asm.v3"))!
            .Element(XName.Get("requestedExecutionLevel", "urn:schemas-microsoft-com:asm.v3"));

        Assert.Equal(assemblyName, manifest.Root.Element(XName.Get("assemblyIdentity", "urn:schemas-microsoft-com:asm.v1"))!.Attribute("name")!.Value);
        Assert.Equal("requireAdministrator", requestedLevel!.Attribute("level")!.Value);
        Assert.Equal("false", requestedLevel.Attribute("uiAccess")!.Value);
        Assert.Equal("PerMonitorV2", manifest.Descendants(XName.Get("dpiAwareness", "http://schemas.microsoft.com/SMI/2016/WindowsSettings")).Single().Value);
    }

    [Fact]
    public void Startup_task_uses_highest_run_level_and_background_argument()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Install", "StartupRegistration.cs"));

        Assert.Contains("taskDefinition.Principal.LogonType = TaskLogonInteractiveToken;", source);
        Assert.Contains("taskDefinition.Principal.RunLevel = 1;", source);
        Assert.Contains("action.Arguments = \"--background\";", source);
    }

    [Fact]
    public void Wmi_fallback_entry_is_not_logged_as_a_method_failure()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Devices", "MSI", "Claw", "MsiClawWmiTdpTransport.cs"));

        Assert.Contains("MSI_ACPI compatibility fallback started", source);
        Assert.Contains("MSI_ACPI compatibility fallback succeeded", source);
        Assert.Contains("LogFailure(method, \"GetWmiFallback\"", source);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
