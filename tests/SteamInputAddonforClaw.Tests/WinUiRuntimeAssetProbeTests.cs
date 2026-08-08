using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WinUiRuntimeAssetProbeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Inspect_ReportsRequiredAssetsAndTheirSizes()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "Assets"));
        File.WriteAllText(Path.Combine(_directory, "App.xbf"), "app");
        File.WriteAllText(Path.Combine(_directory, "MainWindow.xbf"), "window");
        File.WriteAllText(Path.Combine(_directory, "SteamInputAddonforClaw.pri"), "pri");
        File.WriteAllText(Path.Combine(_directory, "Assets", "AppIcon.ico"), "icon");

        var states = WinUiRuntimeAssetProbe.Inspect(_directory);

        Assert.Collection(states,
            state => Assert.Equal(new WinUiRuntimeAssetState("App.xbf", true, 3), state),
            state => Assert.Equal(new WinUiRuntimeAssetState("MainWindow.xbf", true, 6), state),
            state => Assert.Equal(new WinUiRuntimeAssetState("SteamInputAddonforClaw.pri", true, 3), state),
            state => Assert.Equal(new WinUiRuntimeAssetState("Assets\\AppIcon.ico", true, 4), state));
    }

    [Fact]
    public void Inspect_ReportsMissingAssetsWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);

        var states = WinUiRuntimeAssetProbe.Inspect(_directory);

        Assert.All(states, state =>
        {
            Assert.False(state.Exists);
            Assert.Null(state.SizeBytes);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
