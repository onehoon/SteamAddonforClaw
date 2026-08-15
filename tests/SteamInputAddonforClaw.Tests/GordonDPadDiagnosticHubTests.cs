using SteamInputAddonforClaw.Diagnostics.GordonDPad;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// Not parallelized with other tests touching GordonDPadDiagnosticHub's process-wide static event.
[Collection("GordonDPadDiagnosticHub")]
public sealed class GordonDPadDiagnosticHubTests : IDisposable
{
    public GordonDPadDiagnosticHubTests() => GordonDPadDiagnosticHub.ResetForTests();
    public void Dispose() => GordonDPadDiagnosticHub.ResetForTests();

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var exception = Record.Exception(() => GordonDPadDiagnosticHub.Publish("Stage=Physical Up=1 Right=0 Left=0 Down=0 Mask=0x01"));
        Assert.Null(exception);
    }

    [Fact]
    public void Publish_DeliversToSubscriber()
    {
        var received = new List<string>();
        GordonDPadDiagnosticHub.LineObserved += received.Add;

        GordonDPadDiagnosticHub.Publish("Stage=Canonical Up=0 Right=1 Left=0 Down=0 Mask=0x02");

        Assert.Single(received);
        Assert.Equal("Stage=Canonical Up=0 Right=1 Left=0 Down=0 Mask=0x02", received[0]);
    }

    [Fact]
    public void Publish_DeliversToMultipleSubscribers()
    {
        var a = new List<string>();
        var b = new List<string>();
        GordonDPadDiagnosticHub.LineObserved += a.Add;
        GordonDPadDiagnosticHub.LineObserved += b.Add;

        GordonDPadDiagnosticHub.Publish("Stage=ABIDecoded Up=1 Right=0 Left=0 Down=0 Mask=0x01");

        Assert.Single(a);
        Assert.Single(b);
    }

    [Fact]
    public void Publish_OneSubscriberThrowingDoesNotAffectOthers()
    {
        var received = new List<string>();
        GordonDPadDiagnosticHub.LineObserved += _ => throw new InvalidOperationException("broken subscriber");
        GordonDPadDiagnosticHub.LineObserved += received.Add;

        var exception = Record.Exception(() => GordonDPadDiagnosticHub.Publish("Stage=GordonReport Byte9=0x08 DPadMask=0x08"));

        Assert.Null(exception);
        Assert.Single(received);
    }

    [Fact]
    public void Publish_ArbitraryFutureStageIsDeliveredWithoutAnyHubChange()
    {
        // The hub is deliberately generic on content: a brand-new stage name must flow through exactly
        // like any known one, since VIIPER may add stages (e.g. USBIPResponse) without an Addon change.
        var received = new List<string>();
        GordonDPadDiagnosticHub.LineObserved += received.Add;

        GordonDPadDiagnosticHub.Publish("Stage=USBIPResponse Detail=whatever-vIIPER-adds-next");

        Assert.Equal("Stage=USBIPResponse Detail=whatever-vIIPER-adds-next", Assert.Single(received));
    }

    [Fact]
    public void HasSubscribers_ReflectsCurrentSubscriptionState()
    {
        Assert.False(GordonDPadDiagnosticHub.HasSubscribers);
        void Handler(string _) { }
        GordonDPadDiagnosticHub.LineObserved += Handler;
        Assert.True(GordonDPadDiagnosticHub.HasSubscribers);
        GordonDPadDiagnosticHub.LineObserved -= Handler;
        Assert.False(GordonDPadDiagnosticHub.HasSubscribers);
    }

    [Fact]
    public void ResetForTests_ClearsAllSubscribers()
    {
        var received = new List<string>();
        GordonDPadDiagnosticHub.LineObserved += received.Add;

        GordonDPadDiagnosticHub.ResetForTests();
        GordonDPadDiagnosticHub.Publish("Stage=Physical Up=0 Right=0 Left=0 Down=0 Mask=0x00");

        Assert.Empty(received);
    }
}
