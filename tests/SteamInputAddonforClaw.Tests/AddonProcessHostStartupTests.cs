using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostStartupTests
{
    [Fact]
    public async Task InitializeRuntimeAsync_reaches_frontend_ready_while_OEM1_activation_is_pending()
    {
        var oem1Activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimeHost = new AddonRuntimeHost(
            new SteamSessionRuntime(new FakeRoutingPreference()),
            routingRuntime: null,
            new PowerMutationGate(),
            new RecoverySafetyState(RecoverySafety.Safe),
            recoverySafe: true,
            hasIncompleteRecovery: () => false,
            establishBaseline: _ => Task.FromResult(true));
        var runtimeComposition = new AddonRuntimeComposition(
            runtimeHost, null!, "test", null!, oem1Activation.Task);
        var testDataRoot = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw-HostTests", Guid.NewGuid().ToString("N"));
        var host = new AddonProcessHost(null, (_, _) => runtimeComposition, testDataRoot,
            () => $"SteamInputAddonforClaw.Frontend.Test.{Guid.NewGuid():N}");
        host.TestOnly_SetStartupForInitialization(
            new AddonStartupComposition(null!, null!, null!, null!, null!, null!, new CenterMStartupControl(available: false)),
            new StartupResult(true, ControllerEnvironmentMode.Indeterminate, ControllerEnvironmentReadiness.Indeterminate));

        var initialization = host.InitializeRuntimeAsync();

        await initialization.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(oem1Activation.Task.IsCompleted);

        oem1Activation.SetResult();
        await host.DisposeAsync();
    }

    private sealed class FakeRoutingPreference : ISteamInputRoutingPreference
    {
        public bool SteamInputRoutingEnabled => false;
        public event EventHandler? SteamInputRoutingEnabledChanged { add { } remove { } }
    }
}
