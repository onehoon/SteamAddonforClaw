using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamSessionRuntimeTests
{
    [Fact]
    public void DeveloperTestModeState_transition_is_published_through_the_owned_state_graph()
    {
        using var runtime = new SteamSessionRuntime(new FakeBigPicturePreference());
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
        var runtime = new SteamSessionRuntime(new FakeBigPicturePreference());

        runtime.Dispose();
        runtime.Dispose();
    }

    private sealed class FakeBigPicturePreference : ISteamBigPictureRoutingPreference
    {
        public bool RouteInSteamBigPicture => false;
        public event EventHandler? RouteInSteamBigPictureChanged { add { } remove { } }
    }
}
