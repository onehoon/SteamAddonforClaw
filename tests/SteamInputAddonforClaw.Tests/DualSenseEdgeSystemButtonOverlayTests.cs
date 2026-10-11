using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class DualSenseEdgeSystemButtonOverlayTests
{
    private const uint Ps = 0x00010000;
    private const uint Cross = 0x00000020;

    [Fact]
    public void Steam_pulse_asserts_only_PS_for_100ms()
    {
        var time = new FakeTimeProvider();
        var overlay = new DualSenseEdgeSystemButtonOverlay(time);

        overlay.RequestSteamPulse();
        Assert.Equal(Ps, overlay.Apply(default).Buttons);
        time.Advance(TimeSpan.FromMilliseconds(101));
        Assert.Equal(0u, overlay.Apply(default).Buttons);
    }

    [Fact]
    public void Quick_access_adds_PS_and_Cross_then_preserves_physical_Cross_after_expiry()
    {
        var time = new FakeTimeProvider();
        var overlay = new DualSenseEdgeSystemButtonOverlay(time);

        overlay.RequestQuickAccessPulse();
        Assert.Equal(Ps | Cross, overlay.Apply(default).Buttons);
        time.Advance(TimeSpan.FromMilliseconds(101));

        var physicalCross = new DSDeviceState { Buttons = Cross };
        Assert.Equal(Cross, overlay.Apply(physicalCross).Buttons);
    }

    [Fact]
    public void Clear_drops_pending_system_button_pulses()
    {
        var overlay = new DualSenseEdgeSystemButtonOverlay(new FakeTimeProvider());
        overlay.RequestSteamPulse();
        overlay.RequestQuickAccessPulse();

        overlay.Clear();

        Assert.Equal(0u, overlay.Apply(default).Buttons);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now += duration;
    }
}
