using SteamInputAddonforClaw.Hosting;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostStartupTests
{
    [Fact]
    public async Task Frontend_ready_boundary_does_not_wait_for_pending_OEM1_activation()
    {
        var oem1Activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frontendReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transportStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var initialization = AddonProcessHost.StartFrontendTransportAsync(
            oem1Activation.Task,
            async () =>
            {
                transportStarted.TrySetResult();
                await Task.Yield();
            },
            () => frontendReady.TrySetResult());

        await transportStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await frontendReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await initialization;
        Assert.False(oem1Activation.Task.IsCompleted);

        oem1Activation.SetResult();
    }
}
