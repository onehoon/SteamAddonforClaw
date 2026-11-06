using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostStartupTests
{
    [Fact]
    public async Task Intel_FPS_runtime_is_not_initialized_without_a_verified_model_capability()
    {
        var testDataRoot = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw-HostTests", Guid.NewGuid().ToString("N"));
        var limiter = new RecordingFrameLimiter();
        await using var host = new AddonProcessHost(
            testOnlyDataRoot: testDataRoot,
            testIntelFrameLimiterFactory: _ => limiter);

        host.ReconcileIntelFpsForOrdinaryStartup();
        host.ReconcileIntelFpsForOrdinaryOperation("Test");

        Assert.Equal(0, limiter.InitializeCalls);
        Assert.Equal(0, limiter.EnableCalls);
        Assert.Equal(0, limiter.DisableCalls);
    }

    [Fact]
    public async Task Recognized_model_id_does_not_enable_Intel_FPS_when_startup_rejected_hardware()
    {
        var testDataRoot = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw-HostTests", Guid.NewGuid().ToString("N"));
        var limiter = new RecordingFrameLimiter();
        await using var host = new AddonProcessHost(
            testOnlyDataRoot: testDataRoot,
            testIntelFrameLimiterFactory: _ => limiter);
        host.TestOnly_SetStartupForInitialization(
            new AddonStartupComposition(null!, null!, null!, null!, new CenterMStartupControl(available: false)),
            new StartupResult(
                true,
                HardwareSupported: false,
                HardwareDeviceModel: MsiClawDeviceModels.Claw8ExAiPlus.Id,
                HardwareStatus: HardwareCompatibilityStatus.Unsupported,
                HardwareDeviceFamily: MsiClawDeviceModels.FamilyId));

        host.ReconcileIntelFpsForOrdinaryStartup();

        Assert.Equal(0, limiter.InitializeCalls);
        Assert.Equal(0, limiter.EnableCalls);
        Assert.Equal(0, limiter.DisableCalls);
    }

    [Fact]
    public async Task Verified_Intel_model_keeps_the_existing_FPS_startup_path()
    {
        var testDataRoot = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw-HostTests", Guid.NewGuid().ToString("N"));
        var limiter = new RecordingFrameLimiter();
        await using var host = new AddonProcessHost(
            testOnlyDataRoot: testDataRoot,
            testIntelFrameLimiterFactory: _ => limiter);
        host.TestOnly_SetStartupForInitialization(
            new AddonStartupComposition(null!, null!, null!, null!, new CenterMStartupControl(available: false)),
            new StartupResult(
                true,
                HardwareSupported: true,
                HardwareDeviceModel: MsiClawDeviceModels.Claw8ExAiPlus.Id,
                HardwareStatus: HardwareCompatibilityStatus.Supported,
                HardwareDeviceFamily: MsiClawDeviceModels.FamilyId));

        host.ReconcileIntelFpsForOrdinaryStartup();

        Assert.Equal(1, limiter.InitializeCalls);
    }

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
        var host = new AddonProcessHost((_, _) => runtimeComposition, testDataRoot,
            () => $"SteamInputAddonforClaw.Frontend.Test.{Guid.NewGuid():N}");
        host.TestOnly_SetStartupForInitialization(
            new AddonStartupComposition(null!, null!, null!, null!, new CenterMStartupControl(available: false)),
            new StartupResult(true));

        var initialization = host.InitializeRuntimeAsync();

        await initialization.WaitAsync(TimeSpan.FromSeconds(5));

        await host.DisposeAsync();
    }

    private sealed class RecordingFrameLimiter : IIntelFrameLimiter
    {
        public int InitializeCalls { get; private set; }
        public int EnableCalls { get; private set; }
        public int DisableCalls { get; private set; }
        public bool Available => true;
        public string? UnavailableReason => null;
        public IntelFpsCapability? Capability => null;
        public void Initialize() => InitializeCalls++;
        public IntelFpsApplyOutcome Enable(int fps, AcDcPowerSource source)
        {
            EnableCalls++;
            return IntelFpsApplyOutcome.Succeeded;
        }
        public bool Disable(AcDcPowerSource? source)
        {
            DisableCalls++;
            return true;
        }
        public void Dispose() { }
    }

}
