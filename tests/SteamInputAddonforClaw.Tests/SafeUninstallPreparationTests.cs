using SteamInputAddonforClaw.ClawHud;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.CenterMStartup;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SafeUninstallPreparationTests
{
    [Fact]
    public void Headless_preparation_stops_before_runtime_initialization_when_startup_is_not_ready()
    {
        var calls = new List<string>();

        var result = SafeUninstall.PrepareHeadlessStockSafeState(
            () => { calls.Add("startup"); return AddonProcessStartupOutcome.UnsupportedHardware; },
            () => calls.Add("initialize"),
            () => { calls.Add("stock-proof"); return new(true, "UninstallPrepared"); },
            () => { calls.Add("clawhud-stop"); return new(true, "Stopped"); });

        Assert.False(result.Succeeded);
        Assert.StartsWith("StartupNotReady:", result.Reason, StringComparison.Ordinal);
        Assert.Equal(["startup"], calls);
    }

    [Fact]
    public void Headless_preparation_blocks_ClawHUD_and_cleanup_after_stock_proof_failure()
    {
        var calls = new List<string>();

        var result = SafeUninstall.PrepareHeadlessStockSafeState(
            () => { calls.Add("startup"); return AddonProcessStartupOutcome.RuntimeReady; },
            () => calls.Add("initialize"),
            () => { calls.Add("stock-proof"); return new(false, "StockBaselineUnavailable"); },
            () => { calls.Add("clawhud-stop"); return new(true, "Stopped"); });

        Assert.False(result.Succeeded);
        Assert.Equal("StockSafetyNotProven:StockBaselineUnavailable", result.Reason);
        Assert.Equal(["startup", "initialize", "stock-proof"], calls);
    }

    [Fact]
    public void Headless_preparation_runs_PR12_proof_before_Managed_ClawHUD_shutdown()
    {
        var calls = new List<string>();

        var result = SafeUninstall.PrepareHeadlessStockSafeState(
            () => { calls.Add("startup"); return AddonProcessStartupOutcome.RuntimeReady; },
            () => calls.Add("initialize"),
            () => { calls.Add("stock-proof"); return new(true, "UninstallPrepared"); },
            () => { calls.Add("clawhud-stop"); return new(true, "AdoptedManagedRuntimeStopped"); });

        Assert.True(result.Succeeded);
        Assert.Equal("StockSafetyProvenAndManagedClawHudStopped", result.Reason);
        Assert.Equal(["startup", "initialize", "stock-proof", "clawhud-stop"], calls);
    }

    [Fact]
    public void Headless_preparation_failure_to_confirm_ClawHUD_shutdown_blocks_completion()
    {
        var result = SafeUninstall.PrepareHeadlessStockSafeState(
            () => AddonProcessStartupOutcome.RuntimeReady,
            static () => { },
            () => new(true, "UninstallPrepared"),
            () => new(false, "ManagedShutdownNotConfirmed"));

        Assert.False(result.Succeeded);
        Assert.Equal("ManagedClawHudShutdownNotConfirmed:ManagedShutdownNotConfirmed", result.Reason);
    }
}
