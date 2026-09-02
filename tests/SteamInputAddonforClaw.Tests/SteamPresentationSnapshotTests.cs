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
    public void Developer_test_mode_does_not_make_the_production_snapshot_want_steamdeck()
    {
        var appId = new FakeAppIdSource { AppId = 0 };
        using var runtime = new SteamSessionRuntime(appId);
        runtime.DeveloperTestModeState.SetEnabled(true);

        Assert.False(runtime.CapturePresentationSnapshot().WantsSteamDeck);
    }

    private sealed class FakeAppIdSource : IRunningAppIdSource
    {
        public uint AppId { get; set; }
        public uint GetRunningAppId() => AppId;
        public event EventHandler? Changed { add { } remove { } }
    }
}
