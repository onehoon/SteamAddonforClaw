using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Work order PR6 section 8/25.1: the raw one-shot Steam/BPM presentation snapshot. It uses
/// only raw RunningAppID + Big Picture facts -- never Developer Test Mode.</summary>
[Collection("AppLog")]
public sealed class SteamPresentationSnapshotTests
{
    [Theory]
    [InlineData(0u, false, false)]  // no game, no BPM -> Xbox360
    [InlineData(1234u, false, true)] // Steam game -> SteamDeck
    public void WantsSteamDeck_is_true_only_when_a_game_or_bpm_is_active(uint appId, bool bpm, bool wantsDeck)
        => Assert.Equal(wantsDeck, new SteamPresentationSnapshot(appId, bpm).WantsSteamDeck);

    [Fact]
    public void BigPicture_active_alone_wants_steamdeck()
        => Assert.True(new SteamPresentationSnapshot(0, BigPictureActive: true).WantsSteamDeck);

    [Fact]
    public void Capture_reads_the_raw_running_app_id()
    {
        var appId = new FakeAppIdSource { AppId = 0 };
        using var runtime = new SteamSessionRuntime(appId);

        Assert.Equal(0u, runtime.CapturePresentationSnapshot().RunningAppId);
        Assert.False(runtime.CapturePresentationSnapshot().WantsSteamDeck);

        appId.AppId = 570;
        var snapshot = runtime.CapturePresentationSnapshot();
        Assert.Equal(570u, snapshot.RunningAppId);
        Assert.True(snapshot.WantsSteamDeck);
    }

    [Fact]
    public void The_production_presentation_snapshot_has_no_developer_test_mode_input()
    {
        // Full1902 Cleanup I: SteamSessionRuntime no longer owns Developer state at all, so the
        // stronger architectural fact is that its source has no Developer coupling.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Steam/SteamSessionRuntime.cs"));

        Assert.DoesNotContain("DeveloperTestModeState", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_testMode", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsEnabled", source, StringComparison.Ordinal);
    }

    private sealed class FakeAppIdSource : IRunningAppIdSource
    {
        public uint AppId { get; set; }
        public uint GetRunningAppId() => AppId;
        public event EventHandler? Changed { add { } remove { } }
    }
}
