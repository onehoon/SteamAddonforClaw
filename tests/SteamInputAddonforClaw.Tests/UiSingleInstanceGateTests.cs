using SteamInputAddonforClaw.UI.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UiSingleInstanceGateTests
{
    [Fact]
    public void First_instance_is_primary_and_second_is_secondary()
    {
        var names = CreateNames();
        using var primary = new UiSingleInstanceGate(names.Mutex, names.Event);
        using var secondary = new UiSingleInstanceGate(names.Mutex, names.Event);

        Assert.True(primary.IsPrimaryInstance);
        Assert.False(secondary.IsPrimaryInstance);
    }

    [Fact]
    public void Secondary_activation_signals_primary_once()
    {
        var names = CreateNames();
        using var primary = new UiSingleInstanceGate(names.Mutex, names.Event);
        using var secondary = new UiSingleInstanceGate(names.Mutex, names.Event);
        using var activated = new ManualResetEventSlim();

        primary.RegisterActivation(activated.Set);
        secondary.ActivatePrimaryInstance();

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Secondary_cannot_register_activation_and_duplicate_registration_is_rejected()
    {
        var names = CreateNames();
        using var primary = new UiSingleInstanceGate(names.Mutex, names.Event);
        using var secondary = new UiSingleInstanceGate(names.Mutex, names.Event);

        Assert.Throws<InvalidOperationException>(() => secondary.RegisterActivation(() => { }));
        primary.RegisterActivation(() => { });
        Assert.Throws<InvalidOperationException>(() => primary.RegisterActivation(() => { }));
    }

    [Fact]
    public void Dispose_is_idempotent_and_releases_primary_ownership()
    {
        var names = CreateNames();
        var primary = new UiSingleInstanceGate(names.Mutex, names.Event);
        primary.Dispose();
        primary.Dispose();

        using var restarted = new UiSingleInstanceGate(names.Mutex, names.Event);
        Assert.True(restarted.IsPrimaryInstance);
    }

    private static (string Mutex, string Event) CreateNames()
    {
        var id = Guid.NewGuid().ToString("N");
        return ($@"Local\SteamInputAddonforClaw.UI.Tests.{id}.Mutex", $@"Local\SteamInputAddonforClaw.UI.Tests.{id}.Event");
    }
}
