using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SingleInstanceGateTests
{
    [Fact]
    public void SecondInstance_DetectsExistingPrimaryInstance()
    {
        var names = CreateNames();
        using var primary = new SingleInstanceGate(names.MutexName, names.ActivationEventName);
        using var secondary = new SingleInstanceGate(names.MutexName, names.ActivationEventName);

        Assert.True(primary.IsPrimaryInstance);
        Assert.False(secondary.IsPrimaryInstance);
    }

    [Fact]
    public void SecondInstance_ActivatesPrimaryInstance()
    {
        var names = CreateNames();
        using var primary = new SingleInstanceGate(names.MutexName, names.ActivationEventName);
        using var secondary = new SingleInstanceGate(names.MutexName, names.ActivationEventName);
        using var activated = new ManualResetEventSlim();

        primary.RegisterActivation(activated.Set);
        secondary.ActivatePrimaryInstance();

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void NewInstance_AfterPrimaryDisposes_BecomesPrimaryInstance()
    {
        var names = CreateNames();
        using (var primary = new SingleInstanceGate(names.MutexName, names.ActivationEventName))
        {
            Assert.True(primary.IsPrimaryInstance);
        }

        using var restartedInstance = new SingleInstanceGate(names.MutexName, names.ActivationEventName);

        Assert.True(restartedInstance.IsPrimaryInstance);
    }

    [Fact]
    public void RegisterActivation_WhenCalledTwice_ThrowsWithoutReplacingTheOriginalHandler()
    {
        var names = CreateNames();
        using var primary = new SingleInstanceGate(names.MutexName, names.ActivationEventName);

        primary.RegisterActivation(() => { });

        Assert.Throws<InvalidOperationException>(() => primary.RegisterActivation(() => { }));
    }

    [Fact]
    public void Dispose_PreventsFutureActivationCallbacks()
    {
        var names = CreateNames();
        var activationCount = 0;
        var primary = new SingleInstanceGate(names.MutexName, names.ActivationEventName);
        primary.RegisterActivation(() => Interlocked.Increment(ref activationCount));
        primary.Dispose();

        using var secondary = new SingleInstanceGate(names.MutexName, names.ActivationEventName);
        secondary.ActivatePrimaryInstance();
        Thread.Sleep(50);

        Assert.Equal(0, activationCount);
    }

    private static (string MutexName, string ActivationEventName) CreateNames()
    {
        var identifier = Guid.NewGuid().ToString("N");
        return ($@"Local\SteamInputAddonforClaw.Tests.{identifier}.Mutex", $@"Local\SteamInputAddonforClaw.Tests.{identifier}.Event");
    }
}
