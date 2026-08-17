using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Hosting;

internal enum AddonProcessStartupOutcome
{
    RuntimeReady,
    UpdateRestartScheduled,
    Canceled
}

internal sealed class AddonProcessHost : IAsyncDisposable
{
    private readonly string[]? _updateRestartArguments;
    private readonly CancellationTokenSource _startupCancellationTokenSource = new();
    private AddonStartupComposition? _startupComposition;
    private AddonRuntimeHost? _runtimeHost;
    private AddonProcessStartupOutcome? _startupOutcome;
    private SystemTrayIcon? _systemTrayIcon;
    private NativeTrayHostWindow? _trayHostWindow;
    private int _runtimeInitialized;
    private int _disposed;
    private int _startupStarted;
    private IAddonFrontendControl? _frontendControl;
    private NamedPipeAddonFrontendServer? _frontendServer;

    internal AddonProcessHost(string[]? updateRestartArguments)
    {
        _updateRestartArguments = updateRestartArguments;
    }

    internal bool IsTrayAvailable => _systemTrayIcon?.IsAvailable == true;
    internal IAddonFrontendControl FrontendControl => _frontendControl ?? throw new InvalidOperationException("Frontend control has not been initialized.");

    internal async Task<AddonProcessStartupOutcome> RunStartupAsync()
    {
        if (_startupOutcome is not null) return _startupOutcome.Value;
        if (Interlocked.Exchange(ref _startupStarted, 1) != 0)
            throw new InvalidOperationException("Startup has already been started.");

        AppLog.Info("Startup coordination started.");
        var startupComposition = AddonStartupCompositionFactory.Create(_updateRestartArguments);
        _startupComposition = startupComposition;

        try
        {
            var startupResult = await startupComposition.Coordinator.RunAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
            _startupOutcome = startupResult.ShouldStartRuntime
                ? AddonProcessStartupOutcome.RuntimeReady
                : AddonProcessStartupOutcome.UpdateRestartScheduled;
            _startupResult = startupResult;
            return _startupOutcome.Value;
        }
        catch (OperationCanceledException) when (_startupCancellationTokenSource.IsCancellationRequested)
        {
            _startupOutcome = AddonProcessStartupOutcome.Canceled;
            return _startupOutcome.Value;
        }
        catch (Exception exception)
        {
            AppLog.Error("Startup coordination failed.", exception);
            throw;
        }
    }

    private StartupResult? _startupResult;

    internal async Task InitializeRuntimeAsync()
    {
        if (_startupOutcome != AddonProcessStartupOutcome.RuntimeReady)
            throw new InvalidOperationException("Runtime initialization requires a successful startup.");
        if (Interlocked.Exchange(ref _runtimeInitialized, 1) != 0)
            throw new InvalidOperationException("Runtime has already been initialized.");

        var startupComposition = _startupComposition ?? throw new InvalidOperationException("Startup composition is unavailable.");
        var startupResult = _startupResult ?? throw new InvalidOperationException("Startup result is unavailable.");
        AppLog.Info($"Starting runtime. Environment={startupResult.EnvironmentMode}; Readiness={startupResult.EnvironmentReadiness}.");
        var composition = AddonRuntimeCompositionFactory.Create(
            startupComposition.HandheldDeviceAdapter,
            startupComposition.DeviceRegistry,
            startupComposition.ControllerEnvironmentAssessmentProvider,
            startupComposition.AddonOwnedVirtualDeviceTracker,
            startupComposition.RuntimeRecoveryManager,
            startupComposition.StockCenterMBaseline,
            startupResult.RecoverySafe);

        _runtimeHost = composition.RuntimeHost;
        _frontendControl = new SteamInputAddonforClaw.Frontend.InProcessAddonFrontendControl(
            composition.StartupSettings, composition.StatusProvider, _runtimeHost, _runtimeHost.DeveloperTestModeState, composition.StartupRegistrationMessage);
        _frontendServer = new NamedPipeAddonFrontendServer(
            FrontendPipeEndpoint.CreateForCurrentUserSession(),
            _frontendControl);
        await _frontendServer.StartAsync().ConfigureAwait(false);
        _startupComposition = null;
    }

    internal void StartPowerObservation() => GetRuntimeHost().StartPowerObservation();

    internal Task ReconcileAsync(CancellationToken cancellationToken = default) => GetRuntimeHost().ReconcileAsync(cancellationToken);

    internal UserTerminationDecision EvaluateUserTermination() =>
        _runtimeHost?.EvaluateUserTermination() ?? new(true, UserTerminationBlockReason.None);

    internal bool TryInitializeTray(Action open, Action restart, Action exit)
    {
        try
        {
            _trayHostWindow = new NativeTrayHostWindow();
            _systemTrayIcon = new SystemTrayIcon(_trayHostWindow.Handle, open, restart, exit, EvaluateUserTermination);
            return true;
        }
        catch (Exception exception)
        {
            _systemTrayIcon?.Dispose();
            _systemTrayIcon = null;
            _trayHostWindow?.Dispose();
            _trayHostWindow = null;
            AppLog.Error("Tray", "Tray initialization failed in headless Runtime mode.", exception);
            return false;
        }
    }

    internal void CancelStartup() => _startupCancellationTokenSource.Cancel();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _startupCancellationTokenSource.Cancel();
        _runtimeHost?.PrepareForShutdown();
        if (_frontendServer is not null)
        {
            await _frontendServer.DisposeAsync().ConfigureAwait(false);
            _frontendServer = null;
        }
        _systemTrayIcon?.Dispose();
        _systemTrayIcon = null;
        _trayHostWindow?.Dispose();
        _trayHostWindow = null;
        if (_runtimeHost is not null)
        {
            await _runtimeHost.DisposeAsync().ConfigureAwait(false);
            _runtimeHost = null;
        }
        _startupComposition = null;
    }

    private AddonRuntimeHost GetRuntimeHost() => _runtimeHost ?? throw new InvalidOperationException("Runtime has not been initialized.");
}
