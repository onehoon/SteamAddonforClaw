using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class XboxCatalogDiagnosticFrontendTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamAddon.XboxCatalogFrontend.{Guid.NewGuid():N}");

    [Fact]
    public async Task In_process_frontend_delegates_one_scan_with_the_request_cancellation_token()
    {
        var expected = FrontendXboxCatalogDiagnosticResult.Unavailable("Synthetic result.");
        var callCount = 0;
        CancellationToken observedToken = default;
        var control = CreateControl(token =>
        {
            callCount++;
            observedToken = token;
            return Task.FromResult(expected);
        });
        using var cancellation = new CancellationTokenSource();

        var result = await control.RunXboxCatalogDiagnosticAsync(cancellation.Token);

        Assert.Same(expected, result);
        Assert.Equal(1, callCount);
        Assert.Equal(cancellation.Token, observedToken);
    }

    [Fact]
    public async Task In_process_frontend_returns_deterministic_unavailable_when_not_wired()
    {
        var control = CreateControl();

        var result = await control.RunXboxCatalogDiagnosticAsync();

        Assert.Equal(FrontendXboxCatalogDiagnosticOutcome.Unavailable, result.Outcome);
        Assert.Equal("XBOX catalog diagnostic is unavailable.", result.Status);
        Assert.Empty(result.Games);
    }

    [Fact]
    public async Task In_process_frontend_honors_cancellation_before_delegating()
    {
        var callCount = 0;
        var control = CreateControl(_ =>
        {
            callCount++;
            return Task.FromResult(FrontendXboxCatalogDiagnosticResult.Unavailable("Unexpected call."));
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control.RunXboxCatalogDiagnosticAsync(cancellation.Token));
        Assert.Equal(0, callCount);
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private InProcessAddonFrontendControl CreateControl(Func<CancellationToken, Task<FrontendXboxCatalogDiagnosticResult>>? runner = null)
    {
        AppLog.DirectoryOverride = Path.Combine(_directory, "logs");
        var settings = new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(Path.Combine(_directory, "settings.json")), new NoOpStartupManager());
        return new InProcessAddonFrontendControl(settings, new ThrowingSystemStatusProvider(), null, runXboxCatalogDiagnostic: runner);
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
