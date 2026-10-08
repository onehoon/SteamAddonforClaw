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
        Assert.All(requests.Skip(1), request =>
            Assert.True(UserProcessLauncher.IsWithinCreateProcessWithTokenCommandLineLimit(
                UserProcessLauncher.BuildCommandLine(request.StartInfo))));

        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw", "Processes", "UserProcessLauncher.cs"));
        Assert.Contains("LaunchCore(startInfo, runAsAdministrator: false, MediumProcessCreationApi.CreateProcessWithTokenW)", source, StringComparison.Ordinal);
        Assert.Contains("StartWithMediumUserToken(startInfo, mediumProcessCreationApi)", source, StringComparison.Ordinal);
        Assert.Contains("CreateProcessWithTokenW", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_failure_capture_preserves_the_api_stage_and_error_code_immediately()
    {
        var getLastErrorCalls = 0;
        var failure = UserProcessLauncher.CaptureNativeFailure("CreateProcessWithTokenW", () =>
        {
            getLastErrorCalls++;
            return 1314;
        });

        Assert.Equal(1, getLastErrorCalls);
        Assert.Equal("CreateProcessWithTokenW", failure.Stage);
        Assert.Equal(1314, failure.NativeErrorCode);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(0, false)]
    [InlineData(99, false)]
    public void Only_a_primary_linked_token_is_accepted_for_direct_process_creation(
        int tokenType,
        bool expected)
    {
        Assert.Equal(expected, UserProcessLauncher.IsPrimaryTokenType(tokenType));
    }

    [Fact]
    public void Medium_process_creation_uses_the_validated_linked_primary_without_duplicate_token_ex()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "SteamInputAddonforClaw", "Processes", "UserProcessLauncher.cs"));
        var startIndex = source.IndexOf("private static bool StartWithMediumUserToken(", StringComparison.Ordinal);
        var validateIndex = source.IndexOf("private static void ValidateMediumLinkedToken(", startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && validateIndex > startIndex);
        var mediumPath = source[startIndex..validateIndex];

        Assert.Contains("using var linkedToken = GetLinkedToken(currentToken);", mediumPath, StringComparison.Ordinal);
        Assert.Contains("ValidateMediumLinkedToken(currentToken, linkedToken", mediumPath, StringComparison.Ordinal);
        Assert.Contains("var processToken = linkedToken;", mediumPath, StringComparison.Ordinal);
        Assert.Contains("CreateEnvironmentBlock(out var environment, processToken, false)", mediumPath, StringComparison.Ordinal);
        Assert.Contains("CreateProcessWithTokenW(\n                    processToken", mediumPath, StringComparison.Ordinal);
        Assert.Contains("CreateProcessAsUserW(\n                    processToken", mediumPath, StringComparison.Ordinal);
        Assert.DoesNotContain("DuplicateTokenEx", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateProcessWithToken_command_line_limit_does_not_change_the_long_command_line_path()
    {
        var shortShellCommand = new ProcessStartInfo(@"C:\Windows\explorer.exe") { UseShellExecute = false };
        shortShellCommand.ArgumentList.Add("steam://open/main");
        var longPowerShellCommand = new ProcessStartInfo(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")
        {
            UseShellExecute = false
        };
        longPowerShellCommand.ArgumentList.Add(new string('x', 1_100));

        Assert.True(UserProcessLauncher.IsWithinCreateProcessWithTokenCommandLineLimit(
            UserProcessLauncher.BuildCommandLine(shortShellCommand)));
        Assert.False(UserProcessLauncher.IsWithinCreateProcessWithTokenCommandLineLimit(
            UserProcessLauncher.BuildCommandLine(longPowerShellCommand)));
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
