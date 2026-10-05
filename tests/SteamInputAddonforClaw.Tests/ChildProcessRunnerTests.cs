using SteamInputAddonforClaw.Processes;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ChildProcessRunnerTests
{
    [Fact]
    public void Start_info_uses_inherited_security_context_and_preserves_arguments()
    {
        const string executable = @"C:\Program Files\Addon\SteamInputAddonforClaw.exe";
        const string arguments = "--prerequisite-setup-worker";

        var startInfo = ChildProcessRunner.CreateStartInfo(executable, arguments);

        Assert.Equal(executable, startInfo.FileName);
        Assert.Equal(arguments, startInfo.Arguments);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.NotEqual("runas", startInfo.Verb);
    }

    [Fact]
    public async Task Completed_child_returns_its_exit_code()
    {
        var commandProcessor = CommandProcessorPath();
        var runner = new ChildProcessRunner();

        var result = await runner.RunAsync(commandProcessor, "/d /c exit 7", CancellationToken.None);

        Assert.Equal(ChildProcessResultKind.Completed, result.Kind);
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task Missing_child_is_reported_as_failed_to_start()
    {
        var runner = new ChildProcessRunner();

        var result = await runner.RunAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe"), "", CancellationToken.None);

        Assert.Equal(ChildProcessResultKind.FailedToStart, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public async Task Configured_timeout_terminates_child_and_reports_timeout()
    {
        var runner = new ChildProcessRunner(TimeSpan.FromMilliseconds(250));

        var result = await runner.RunAsync(CommandProcessorPath(), "/d /c ping 127.0.0.1 -n 20 > nul", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ChildProcessResultKind.TimedOut, result.Kind);
    }

    private static string CommandProcessorPath()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        return File.Exists(path) ? path : throw new FileNotFoundException("Windows command processor is unavailable.", path);
    }
}
