using System.Diagnostics;
using SteamInputAddonforClaw.Processes;
using SteamInputAddonforClaw.WindowsGaming;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UserProcessLauncherTests
{
    [Fact]
    public void Launch_uses_only_the_requested_privilege_mode_and_does_not_retry()
    {
        var requestedModes = new List<bool>();
        var mediumLauncher = new UserProcessLauncher((_, runAsAdministrator) =>
        {
            requestedModes.Add(runAsAdministrator);
            throw new InvalidOperationException("The requested token is unavailable.");
        });
        var startInfo = new ProcessStartInfo(@"C:\Tools\Tool.exe") { UseShellExecute = false };

        Assert.Throws<InvalidOperationException>(() => mediumLauncher.Launch(startInfo));
        Assert.Equal([false], requestedModes);

        var highLauncher = new UserProcessLauncher((_, runAsAdministrator) =>
        {
            requestedModes.Add(runAsAdministrator);
            throw new InvalidOperationException("The requested token is unavailable.");
        });
        Assert.Throws<InvalidOperationException>(() => highLauncher.Launch(startInfo, runAsAdministrator: true));
        Assert.Equal([false, true], requestedModes);
    }

    [Fact]
    public void Shell_uri_and_xbox_activation_are_always_dispatched_as_medium()
    {
        var requests = new List<(ProcessStartInfo StartInfo, bool RunAsAdministrator)>();
        var launcher = new UserProcessLauncher((startInfo, runAsAdministrator) =>
        {
            requests.Add((startInfo, runAsAdministrator));
            return true;
        });

        Assert.True(launcher.LaunchUri("https://example.com/path?q=1"));
        Assert.True(launcher.LaunchUri("steam://open/bigpicture"));
        Assert.True(launcher.LaunchUri("steam://open/main"));
        Assert.True(launcher.LaunchXboxApp());

        Assert.Equal(4, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.False(request.RunAsAdministrator);
            Assert.False(request.StartInfo.UseShellExecute);
        });
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "rundll32.exe"), requests[0].StartInfo.FileName);
        Assert.Equal("url.dll,FileProtocolHandler \"https://example.com/path?q=1\"", requests[0].StartInfo.Arguments);
        Assert.Empty(requests[0].StartInfo.ArgumentList);
        Assert.True(requests[0].StartInfo.CreateNoWindow);
        Assert.All(requests.Skip(1), request =>
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                request.StartInfo.FileName));
        Assert.Equal("steam://open/bigpicture", requests[1].StartInfo.ArgumentList.Single());
        Assert.Equal("steam://open/main", requests[2].StartInfo.ArgumentList.Single());
        Assert.Equal($"shell:AppsFolder\\{XboxGamingHomeAppIdentity.Aumid}", requests[3].StartInfo.ArgumentList.Single());
    }

    [Theory]
    [InlineData("file:///C:/private.txt")]
    [InlineData("steam://open/unsupported")]
    [InlineData("javascript:alert(1)")]
    public void Unsupported_shell_target_is_rejected_without_launch(string target)
    {
        var launchCount = 0;
        var launcher = new UserProcessLauncher((_, _) => { launchCount++; return true; });

        Assert.Throws<InvalidOperationException>(() => launcher.LaunchUri(target));
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public void Direct_launch_rejects_shell_resolution_and_non_exe_targets()
    {
        var launchCount = 0;
        var launcher = new UserProcessLauncher((_, _) => { launchCount++; return true; });

        Assert.Throws<InvalidOperationException>(() => launcher.Launch(new ProcessStartInfo("https://example.com")
        {
            UseShellExecute = false
        }));
        Assert.Throws<InvalidOperationException>(() => launcher.Launch(new ProcessStartInfo(@"C:\Tools\Tool.exe")
        {
            UseShellExecute = true
        }));
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public void Create_process_command_line_quotes_executable_and_argument_list_without_rewriting_raw_arguments()
    {
        var listed = new ProcessStartInfo(@"C:\Program Files\Tool.exe") { UseShellExecute = false };
        listed.ArgumentList.Add("--name");
        listed.ArgumentList.Add("two words");
        listed.ArgumentList.Add("a\"b");
        listed.ArgumentList.Add(string.Empty);

        Assert.Equal("\"C:\\Program Files\\Tool.exe\" \"--name\" \"two words\" \"a\\\"b\" \"\"",
            UserProcessLauncher.BuildCommandLine(listed));

        var raw = new ProcessStartInfo(@"C:\Tools\Tool.exe")
        {
            Arguments = "--flag \"existing value\"",
            UseShellExecute = false
        };
        Assert.Equal("\"C:\\Tools\\Tool.exe\" --flag \"existing value\"", UserProcessLauncher.BuildCommandLine(raw));
    }
}
