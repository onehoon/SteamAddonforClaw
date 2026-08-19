using System.Diagnostics;
using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class QamHostProcessControllerTests
{
    [Fact]
    public void Launch_uses_managed_mode_hidden_stdin_and_canonical_log_directory()
    {
        var runtime = Path.Combine(Path.GetTempPath(), "qam-test-" + Guid.NewGuid().ToString("N"));
        var qamDirectory = Path.Combine(runtime, "qam");
        Directory.CreateDirectory(qamDirectory);
        var executable = Path.Combine(qamDirectory, "SteamInputAddonforClaw.QamHost.exe");
        File.WriteAllText(executable, string.Empty);
        var starts = new List<ProcessStartInfo>();

        try
        {
            var controller = new QamHostProcessController(runtime, @"C:\logs", info =>
            {
                starts.Add(info);
                return null;
            });

            controller.OnBigPictureStateChanged(true);
            SpinWait.SpinUntil(() => starts.Count == 1, TimeSpan.FromSeconds(1));

            var start = Assert.Single(starts);
            Assert.False(start.UseShellExecute);
            Assert.True(start.CreateNoWindow);
            Assert.True(start.RedirectStandardInput);
            Assert.Equal(["--managed", "--log-directory", @"C:\logs"], start.ArgumentList);
        }
        finally
        {
            Directory.Delete(runtime, recursive: true);
        }
    }
}
