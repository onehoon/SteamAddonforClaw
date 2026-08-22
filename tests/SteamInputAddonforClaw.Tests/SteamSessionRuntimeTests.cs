using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamSessionRuntimeTests
{
    [Fact]
    public void DeveloperTestModeState_transition_is_published_through_the_owned_state_graph()
    {
        using var runtime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        SteamSessionStateChangedEventArgs? observed = null;
        runtime.StateChanged += (_, args) => observed = args;

        // Use the same DeveloperTestModeState instance the runtime itself exposes -- proving
        // there is exactly one effective-session graph, not a duplicate.
        runtime.DeveloperTestModeState.SetEnabled(true);

        Assert.NotNull(observed);
        Assert.Equal(SteamSessionSource.DeveloperTest, observed.Current.Source);
        Assert.Equal(SteamSessionSource.DeveloperTest, runtime.State.Source);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var runtime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());

        runtime.Dispose();
        runtime.Dispose();
    }

    [Fact]
    public void ActualObservation_tracksAppId_withoutStartingRoutingObservation()
    {
        var source = new FakeRunningAppIdSource();
        using var runtime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference(), source);
        var observed = new List<uint>();
        runtime.ActualRunningAppIdChanged += appId => observed.Add(appId);

        runtime.StartActualObservation();
        source.SetRunningAppId(123);

        Assert.Equal([123u], observed);
        Assert.Equal(123u, runtime.ActualRunningAppId);
        Assert.False(runtime.State.IsActive);
    }

    private sealed class FakeSteamInputRoutingPreference : ISteamInputRoutingPreference
    {
        public bool SteamInputRoutingEnabled => true;
        public event EventHandler? SteamInputRoutingEnabledChanged { add { } remove { } }
    }

    private sealed class FakeRunningAppIdSource : IRunningAppIdSource
    {
        private uint _appId;
        public event EventHandler? Changed;
        public uint GetRunningAppId() => _appId;
        public void SetRunningAppId(uint appId)
        {
            _appId = appId;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
