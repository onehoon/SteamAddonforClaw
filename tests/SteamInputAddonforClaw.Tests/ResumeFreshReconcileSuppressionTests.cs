using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ResumeFreshReconcileSuppressionTests
{
    [Fact]
    public void Explicit_refresh_notification_is_consumed_but_later_state_change_is_deferred_once()
    {
        var suppression = new ResumeFreshReconcileSuppression();

        suppression.Begin();
        suppression.ExecuteExplicitRefresh(() => Assert.True(suppression.TrySuppressStateChange()));

        Assert.True(suppression.TrySuppressStateChange());
        Assert.True(suppression.TrySuppressStateChange());

        Assert.True(suppression.Complete());
        Assert.False(suppression.TrySuppressStateChange());
    }
}
