using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostStartupTests
{
    [Fact] // Full1902 A2: OEM1/WING activation no longer gates runtime startup (the front-button owner
           // is composed only on a Disabled boot, after presentation attach) -- a non-Disabled startup
           // just reaches frontend-ready without any OEM1 ordering barrier.
    public async Task InitializeRuntimeAsync_reaches_frontend_ready_without_an_oem1_activation_barrier()
    {
        var runtimeHost = new AddonRuntimeHost(
            new SteamSessionRuntime(),
            new PowerMutationGate(),
            new RecoverySafetyState(RecoverySafety.Safe),
            recoverySafe: true,
            establishBaseline: _ => Task.FromResult(true));
        var runtimeComposition = new AddonRuntimeComposition(
            runtimeHost, null!, null!);
        var testDataRoot = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw-HostTests", Guid.NewGuid().ToString("N"));
        var host = new AddonProcessHost(null, (_, _) => runtimeComposition, testDataRoot,
            () => $"SteamInputAddonforClaw.Frontend.Test.{Guid.NewGuid():N}");
        host.TestOnly_SetStartupForInitialization(
            new AddonStartupComposition(null!, null!, null!, null!, new CenterMStartupControl(available: false)),
            new StartupResult(true));

        var initialization = host.InitializeRuntimeAsync();

        await initialization.WaitAsync(TimeSpan.FromSeconds(5));

        await host.DisposeAsync();
    }
}
