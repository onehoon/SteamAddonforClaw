using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Unit coverage for <see cref="SteamDeckSystemButtonOverlay"/>: no-request neutrality, pulse
/// activation, expiry, refresh, and immediate <c>Clear()</c>. Uses an injected fake
/// <see cref="TimeProvider"/> throughout -- no sleeps.
/// </summary>
public sealed class SteamDeckSystemButtonOverlayTests
{
    private static SteamDeckDeviceState SampleMappedState() => new()
    {
        A = 1,
        DPadUp = 1,
        LStickX = 12345,
        RStickY = -6789,
        LTrigger = 4000,
        Menu = 1,
        QuickAccess = 0,
    };

    [Fact]
    public void No_request_leaves_state_unchanged()
    {
        var overlay = new SteamDeckSystemButtonOverlay(new FakeTimeProvider());
        var input = SampleMappedState();

        var result = overlay.Apply(input);

        Assert.Equal((byte)0, result.QuickAccess);
        AssertOnlyQuickAccessDiffers(input, result);
    }

    [Fact]
    public void Requested_pulse_sets_QuickAccess_and_preserves_other_fields()
    {
        var overlay = new SteamDeckSystemButtonOverlay(new FakeTimeProvider());
        var input = SampleMappedState();

        overlay.RequestQuickAccessPulse();
        var result = overlay.Apply(input);

        Assert.Equal((byte)1, result.QuickAccess);
        AssertOnlyQuickAccessDiffers(input, result);
    }

    [Fact]
    public void Pulse_expires_after_duration()
    {
        var time = new FakeTimeProvider();
        var overlay = new SteamDeckSystemButtonOverlay(time);

        overlay.RequestQuickAccessPulse();
        Assert.Equal((byte)1, overlay.Apply(SampleMappedState()).QuickAccess);

        time.Advance(TimeSpan.FromMilliseconds(51));
        Assert.Equal((byte)0, overlay.Apply(SampleMappedState()).QuickAccess);
    }

    [Fact]
    public void Repeated_request_refreshes_pulse_from_latest_call()
    {
        var time = new FakeTimeProvider();
        var overlay = new SteamDeckSystemButtonOverlay(time);

        overlay.RequestQuickAccessPulse();
        time.Advance(TimeSpan.FromMilliseconds(30));
        overlay.RequestQuickAccessPulse(); // restarts the 50ms window from here

        time.Advance(TimeSpan.FromMilliseconds(30)); // 30ms after the 2nd request, 60ms after the 1st
        Assert.Equal((byte)1, overlay.Apply(SampleMappedState()).QuickAccess);
    }

    [Fact]
    public void Clear_immediately_removes_active_pulse()
    {
        var overlay = new SteamDeckSystemButtonOverlay(new FakeTimeProvider());

        overlay.RequestQuickAccessPulse();
        overlay.Clear();

        Assert.Equal((byte)0, overlay.Apply(SampleMappedState()).QuickAccess);
    }

    [Fact]
    public void Steam_and_quick_access_pulses_have_independent_lifetimes()
    {
        var time = new FakeTimeProvider();
        var overlay = new SteamDeckSystemButtonOverlay(time);
        overlay.RequestSteamPulse();
        time.Advance(TimeSpan.FromMilliseconds(25));
        overlay.RequestQuickAccessPulse();

        var both = overlay.Apply(SampleMappedState());
        Assert.Equal((byte)1, both.Steam);
        Assert.Equal((byte)1, both.QuickAccess);

        time.Advance(TimeSpan.FromMilliseconds(30));
        var quickOnly = overlay.Apply(SampleMappedState());
        Assert.Equal((byte)0, quickOnly.Steam);
        Assert.Equal((byte)1, quickOnly.QuickAccess);
    }

    private static void AssertOnlyQuickAccessDiffers(SteamDeckDeviceState input, SteamDeckDeviceState result)
    {
        var expected = input;
        expected.QuickAccess = result.QuickAccess;
        Assert.Equal(expected, result);
    }

    /// <summary>Minimal deterministic <see cref="TimeProvider"/> stub -- avoids pulling in a testing
    /// package for a single advance-able clock.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
