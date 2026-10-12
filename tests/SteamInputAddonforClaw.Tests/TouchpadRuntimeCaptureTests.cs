using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Touchpad;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class TouchpadRuntimeCaptureTests
{
    [Fact]
    public void BottomRightQuarterUsesPrimaryPhysicalBoundsAndRoundsOddDimensionsDownOnTopLeft()
    {
        var region = TouchpadPixelRect.BottomRightQuarter(new TouchpadPixelRect(0, 0, 1920, 1200));
        Assert.Equal(new TouchpadPixelRect(960, 600, 960, 600), region);

        var odd = TouchpadPixelRect.BottomRightQuarter(new TouchpadPixelRect(10, 20, 1919, 1199));
        Assert.Equal(new TouchpadPixelRect(969, 619, 960, 600), odd);
    }

    [Fact]
    public void PointerTrackerRequiresDownInsideAndNeverSwitchesToAnAdditionalFinger()
    {
        var handoff = new TouchpadContactHandoff();
        var tracker = new TouchpadPointerContactTracker(handoff);
        var region = new TouchpadPixelRect(960, 600, 960, 600);

        Assert.False(tracker.Down(1, region, 959, 800));
        Assert.False(tracker.Move(1, region, 1000, 800));
        Assert.Equal(SteamDeckRightPadContact.Neutral, handoff.ReadForPublisherTick(true));

        Assert.True(tracker.Down(1, region, 1200, 900));
        Assert.False(tracker.Down(2, region, 1500, 1000));
        Assert.False(tracker.Move(2, region, 1900, 1200));
        Assert.Equal(SteamDeckRightPadContact.FromNormalizedPosition(true, 0.25, 0.5), handoff.ReadForPublisherTick(true));

        Assert.True(tracker.Move(1, region, 2500, 1400));
        Assert.Equal(SteamDeckRightPadContact.FromNormalizedPosition(true, 1, 1), handoff.ReadForPublisherTick(true));
        Assert.True(tracker.End(1, canceled: false));
        Assert.Equal(SteamDeckRightPadContact.Neutral, handoff.ReadForPublisherTick(true));
    }

    [Fact]
    public void QuickTapsBetweenPublisherTicksCoalesceToOneTouchedTickThenNeutral()
    {
        var handoff = new TouchpadContactHandoff();
        handoff.Down(0.2, 0.3);
        handoff.Up();
        handoff.Down(0.8, 0.7);
        handoff.Up();

        Assert.Equal(SteamDeckRightPadContact.FromNormalizedPosition(true, 0.8, 0.7), handoff.ReadForPublisherTick(true));
        Assert.Equal(SteamDeckRightPadContact.Neutral, handoff.ReadForPublisherTick(true));
    }

    [Fact]
    public void CancelAndDisarmNeverReplayPendingContact()
    {
        var handoff = new TouchpadContactHandoff();
        handoff.Down(0.4, 0.6);
        handoff.Cancel();
        Assert.Equal(SteamDeckRightPadContact.Neutral, handoff.ReadForPublisherTick(true));

        handoff.Down(0.7, 0.2);
        handoff.Up();
        Assert.Equal(SteamDeckRightPadContact.Neutral, handoff.ReadForPublisherTick(false));
        Assert.Equal(SteamDeckRightPadContact.Neutral, handoff.ReadForPublisherTick(true));
    }

    [Fact]
    public void EligibilityRequiresForegroundSteamGameplayAndEveryExistingOwnerGate()
    {
        var eligible = new TouchpadAdmissionFacts(true, true, true, true, true, true, false, false, false);
        Assert.True(eligible.ShouldArm);

        TouchpadAdmissionFacts[] blocked =
        [
            eligible with { PreferenceEnabled = false },
            eligible with { AddonOwnsController = false },
            eligible with { PhysicalInputLive = false },
            eligible with { SteamGameRunning = false }, // BPM-only home/library
            eligible with { ForegroundIsSteamGame = false }, // background game with BPM/desktop foreground
            eligible with { SteamDeckPresentationLive = false }, // Xbox360 or DualSense Edge
            eligible with { OverlayPausedOrVisible = true },
            eligible with { SuspendPaused = true },
            eligible with { ShuttingDown = true },
        ];
        Assert.All(blocked, facts => Assert.False(facts.ShouldArm));
    }

    [Fact]
    public async Task RuntimeCaptureHasOneWindowAndClearsContactAcrossDisarmAndRearm()
    {
        var windows = new List<FakeCaptureWindow>();
        TouchpadContactHandoff? handoff = null;
        var capture = new TouchpadRuntimeCapture(sharedHandoff =>
        {
            handoff = sharedHandoff;
            var window = new FakeCaptureWindow();
            windows.Add(window);
            return window;
        });

        Assert.True(await capture.SetArmedAsync(true, "Test"));
        Assert.True(capture.IsArmed);
        Assert.Single(windows);
        handoff!.Down(0.3, 0.4);
        Assert.Equal(SteamDeckRightPadContact.FromNormalizedPosition(true, 0.3, 0.4), capture.RightPadProvider());

        Assert.True(await capture.SetArmedAsync(false, "TestOff"));
        Assert.False(capture.IsArmed);
        Assert.Equal(SteamDeckRightPadContact.Neutral, capture.RightPadProvider());
        Assert.Equal(1, windows[0].StopCount);

        Assert.True(await capture.SetArmedAsync(true, "TestRearm"));
        Assert.Equal(2, windows.Count);
        Assert.Equal(SteamDeckRightPadContact.Neutral, capture.RightPadProvider());
        await capture.DisposeAsync();
        Assert.All(windows, window => Assert.False(window.IsVisible));
    }

    [Fact]
    public async Task ConcurrentArmRequestsDoNotCreateDuplicateCaptureWindows()
    {
        var windows = new List<FakeCaptureWindow>();
        var capture = new TouchpadRuntimeCapture(_ =>
        {
            var window = new FakeCaptureWindow();
            windows.Add(window);
            return window;
        });

        var results = await Task.WhenAll(
            capture.SetArmedAsync(true, "First"),
            capture.SetArmedAsync(true, "Second"));

        Assert.All(results, Assert.True);
        Assert.Single(windows);
        await capture.DisposeAsync();
    }

    [Fact]
    public void ForegroundAssociationFailsClosedForBpmDesktopAndOtherSteamGameProcesses()
    {
        var installDirectory = Path.Combine(Path.GetTempPath(), "Steam", "steamapps", "common", "Game");
        var gamePath = Path.Combine(installDirectory, "bin", "game.exe");
        var association = new SteamGameForegroundAssociation(
            _ => installDirectory,
            pid => pid switch
            {
                10 => gamePath,
                20 => Path.Combine(Path.GetTempPath(), "Steam", "steam.exe"),
                30 => Path.Combine(Path.GetTempPath(), "Steam", "steamapps", "common", "OtherGame", "game.exe"),
                40 => Path.Combine(installDirectory + "-Sibling", "game.exe"),
                _ => null,
            });

        Assert.True(association.IsForegroundSteamGameplay(1234, 10));
        Assert.False(association.IsForegroundSteamGameplay(0, 10));
        Assert.False(association.IsForegroundSteamGameplay(1234, 0));
        Assert.False(association.IsForegroundSteamGameplay(1234, 20));
        Assert.False(association.IsForegroundSteamGameplay(1234, 30));
        Assert.False(association.IsForegroundSteamGameplay(1234, 40));
    }

    private sealed class FakeCaptureWindow : ITouchpadCaptureWindow
    {
        public bool IsVisible { get; private set; }
        public TouchpadPixelRect Region => new(960, 600, 960, 600);
        public uint Dpi => 96;
        public int StopCount { get; private set; }

        public Task<bool> StartAsync(CancellationToken cancellationToken)
        {
            IsVisible = true;
            return Task.FromResult(true);
        }

        public Task<bool> StopAsync()
        {
            StopCount++;
            IsVisible = false;
            return Task.FromResult(true);
        }

        public void RequestClose() => IsVisible = false;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
