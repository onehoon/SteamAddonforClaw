using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawManualRestoreHostContractTests
{
    [Fact]
    public void Manual_restore_reuses_the_owned_input_poll_worker_without_acquiring_or_switching_mode()
    {
        var host = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");
        var method = Method(host, "private async Task<FrontendMsiBackButtonManualRestoreResult> RunMsiBackButtonManualRestoreAsync(");

        Assert.Contains("var physical = _physicalOwnership;", method, StringComparison.Ordinal);
        Assert.Contains("var ownedInput = physical?.LiveInputSource;", method, StringComparison.Ordinal);
        Assert.Contains("ownedInput.RunRawDirectInputButtonTransitionDiagnosticAsync(cancellationToken)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("new MsiClawInputSource", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadState(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AcquireAsync(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SwitchModeAsync(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("0x24", method, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts])).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Method(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find source marker: {marker}");
        var end = source.IndexOf("\n    private ", start + marker.Length, StringComparison.Ordinal);
        if (end < 0) end = source.Length;
        return source[start..end];
    }
}
