using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Profiles.Performance;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UninstallBootstrapTests
{
    [Fact]
    public void Approved_fast_hook_returns_without_recreating_data_or_logging()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"safe-hook-{Guid.NewGuid():N}");
        var previousDirectory = AppLog.DirectoryOverride;
        var previousLevel = AppLog.MinimumLevelOverride;
        var previousMarker = Environment.GetEnvironmentVariable(UninstallBootstrap.SafeUninstallApprovedEnvironmentVariable);

        try
        {
            AppLog.DirectoryOverride = directory;
            AppLog.MinimumLevelOverride = AppLogLevel.Info;
            Environment.SetEnvironmentVariable(UninstallBootstrap.SafeUninstallApprovedEnvironmentVariable, "1");

            UninstallBootstrap.RunFastCallbackOnly();
            AppLog.DrainForTests();

            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            Environment.SetEnvironmentVariable(UninstallBootstrap.SafeUninstallApprovedEnvironmentVariable, previousMarker);
            AppLog.DirectoryOverride = previousDirectory;
            AppLog.MinimumLevelOverride = previousLevel;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Data_root_is_the_full_reset_root()
    {
        var root = AddonDataPaths.ResolveDataRoot("C:\\Users\\Test\\App");
        Assert.EndsWith("SteamInputAddonforClaw-Data", root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bounded_local_cleanup_preserves_artifacts_when_runtime_release_failed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"uninstall-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var evidencePath = Path.Combine(root, "steam-cef-marker.json");
        File.WriteAllText(evidencePath, "owned-evidence");
        var previousOwnershipPathProvider = SteamCefLegacyMarkerCleanup.OwnershipPathProvider;

        try
        {
            SteamCefLegacyMarkerCleanup.OwnershipPathProvider = () => evidencePath;
            UninstallBootstrap.RunBoundedLocalCleanup(runtimeReleased: false);
            Assert.True(File.Exists(evidencePath));
        }
        finally
        {
            SteamCefLegacyMarkerCleanup.OwnershipPathProvider = previousOwnershipPathProvider;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Single_instance_gate_can_dispose_after_uninstall_handler_registration()
    {
        using var gate = new SingleInstanceGate(
            $"Local\\SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}",
            $"Local\\SteamInputAddonforClaw.Tests.Activate.{Guid.NewGuid():N}");

        if (gate.IsPrimaryInstance)
            gate.RegisterUninstallRequest(static () => { });
    }

    [Fact]
    public void Single_instance_gate_rejects_duplicate_uninstall_handler_registration()
    {
        using var gate = new SingleInstanceGate(
            $"Local\\SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}",
            $"Local\\SteamInputAddonforClaw.Tests.Activate.{Guid.NewGuid():N}");

        if (gate.IsPrimaryInstance)
        {
            gate.RegisterUninstallRequest(static () => { });
            Assert.Throws<InvalidOperationException>(() => gate.RegisterUninstallRequest(static () => { }));
        }
    }

    [Fact]
    public void Safe_uninstall_acquires_the_runtime_gate_when_no_runtime_is_running()
    {
        var (mutexName, eventName) = CreateGateNames();
        SingleInstanceGate CreateGate() => new(mutexName, eventName);

        using var gate = UninstallBootstrap.AcquireRuntimeGateForSafeUninstall(
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), CreateGate,
            requestPrimaryUninstall: static () => throw new Xunit.Sdk.XunitException("No Runtime request should be sent."));

        Assert.NotNull(gate);
        Assert.True(gate.IsPrimaryInstance);
    }

    [Fact]
    public void Safe_uninstall_waits_for_a_running_runtime_to_release_after_preparation_succeeds()
    {
        var (mutexName, eventName) = CreateGateNames();
        using var runtimeReady = new ManualResetEventSlim();
        using var runtimeMayExit = new ManualResetEventSlim();
        var runtimeWasPrimary = false;
        var runtimeThread = new Thread(() =>
        {
            using var runtimeGate = new SingleInstanceGate(mutexName, eventName);
            runtimeWasPrimary = runtimeGate.IsPrimaryInstance;
            runtimeReady.Set();
            runtimeMayExit.Wait();
        }) { IsBackground = true };
        runtimeThread.Start();

        Assert.True(runtimeReady.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(runtimeWasPrimary);
        var requestCount = 0;
        SingleInstanceGate CreateGate() => new(mutexName, eventName);
        SingleInstanceGate? acquired = null;
        try
        {
            acquired = UninstallBootstrap.AcquireRuntimeGateForSafeUninstall(
                TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(10), CreateGate,
                requestPrimaryUninstall: () =>
                {
                    requestCount++;
                    runtimeMayExit.Set();
                    return true;
                });

            Assert.NotNull(acquired);
            Assert.True(acquired.IsPrimaryInstance);
            Assert.Equal(1, requestCount);
        }
        finally
        {
            runtimeMayExit.Set();
            runtimeThread.Join(TimeSpan.FromSeconds(2));
            acquired?.Dispose();
        }
    }

    [Fact]
    public void Safe_uninstall_aborts_when_running_runtime_does_not_release_after_failed_preparation()
    {
        var (mutexName, eventName) = CreateGateNames();
        using var runtimeReady = new ManualResetEventSlim();
        using var runtimeMayExit = new ManualResetEventSlim();
        var runtimeWasPrimary = false;
        var runtimeThread = new Thread(() =>
        {
            using var runtimeGate = new SingleInstanceGate(mutexName, eventName);
            runtimeWasPrimary = runtimeGate.IsPrimaryInstance;
            runtimeReady.Set();
            runtimeMayExit.Wait();
        }) { IsBackground = true };
        runtimeThread.Start();

        Assert.True(runtimeReady.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(runtimeWasPrimary);
        var now = DateTimeOffset.UtcNow;
        var requestCount = 0;
        SingleInstanceGate CreateGate() => new(mutexName, eventName);
        try
        {
            var acquired = UninstallBootstrap.AcquireRuntimeGateForSafeUninstall(
                TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(25), CreateGate,
                requestPrimaryUninstall: () => { requestCount++; return true; },
                utcNow: () => now,
                delay: duration => now += duration);

            Assert.Null(acquired);
            Assert.Equal(1, requestCount);
            Assert.True(runtimeThread.IsAlive);
        }
        finally
        {
            runtimeMayExit.Set();
            runtimeThread.Join(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void Stale_fps_marker_failed_cleanup_preserves_ownership_evidence()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"intel-fps-{Guid.NewGuid():N}.json");
        File.WriteAllText(marker, "{\"fps\":60}");
        try
        {
            var limiter = new FakeIntelFrameLimiter { DisableResult = false };
            Assert.False(UninstallBootstrap.TryCleanupOwnedIntelFpsForUninstall(marker, _ => limiter));
            Assert.True(File.Exists(marker));
            Assert.Equal(1, limiter.DisableCalls);
        }
        finally { if (File.Exists(marker)) File.Delete(marker); }
    }

    [Fact]
    public void Stale_fps_marker_successful_cleanup_removes_ownership_evidence()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"intel-fps-{Guid.NewGuid():N}.json");
        File.WriteAllText(marker, "{\"fps\":60}");
        var limiter = new FakeIntelFrameLimiter();
        Assert.True(UninstallBootstrap.TryCleanupOwnedIntelFpsForUninstall(marker, _ => limiter));
        Assert.False(File.Exists(marker));
        Assert.Equal(1, limiter.DisableCalls);
    }

    [Fact]
    public void Stale_fps_marker_cleanup_does_not_require_user_facing_availability()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"intel-fps-{Guid.NewGuid():N}.json");
        File.WriteAllText(marker, "{\"fps\":60}");
        var limiter = new FakeIntelFrameLimiter { AvailableValue = false };
        Assert.True(UninstallBootstrap.TryCleanupOwnedIntelFpsForUninstall(marker, _ => limiter));
        Assert.False(File.Exists(marker));
        Assert.Equal(1, limiter.DisableCalls);
    }

    private sealed class FakeIntelFrameLimiter : IIntelFrameLimiter
    {
        public bool DisableResult { get; init; } = true;
        public int DisableCalls { get; private set; }
        public void Initialize() { }
        public bool Available => AvailableValue;
        public bool AvailableValue { get; init; } = true;
        public string? UnavailableReason => null;
        public IntelFpsCapability? Capability => null;
        public IntelFpsApplyOutcome Enable(int fps, AcDcPowerSource source) => IntelFpsApplyOutcome.Succeeded;
        public bool Disable(AcDcPowerSource? source) { DisableCalls++; return DisableResult; }
        public void Dispose() { }
    }

    private static (string MutexName, string EventName) CreateGateNames()
    {
        var unique = Guid.NewGuid().ToString("N");
        return ($"Local\\SteamInputAddonforClaw.Tests.SafeUninstall.{unique}",
            $"Local\\SteamInputAddonforClaw.Tests.Activate.SafeUninstall.{unique}");
    }
}
