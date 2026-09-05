using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Display;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.FrontendTransport;
using System.Diagnostics;
using Microsoft.Win32;

namespace SteamInputAddonforClaw.Frontend;

internal sealed class InProcessAddonFrontendControl : IAddonFrontendControl
{
    private readonly StartupSettingsCoordinator _settings;
    private readonly ISystemStatusProvider _status;
    private readonly AddonRuntimeHost? _runtime;
    private readonly DeveloperTestModeState _developer;
    private readonly IFrontendPrerequisiteSetupExecutor _setupExecutor;
    private readonly Func<string?> _processPath;
    private readonly bool _frontButtonMappingAvailable;
    private int _shutdownStarted;
    private readonly object _clawSensorProbeGate = new();
    private ClawSensorProbeSession? _clawSensorProbe;
    private readonly object _fanProbeGate = new();
    private FanProbeSession? _fanProbe;

    /// <summary>Wraps the Runtime-owned <see cref="ClawSensorProbeCoordinator"/> for one active
    /// diagnostic session, plus the device identity captured at Open time (so a stale-but-still-open
    /// session keeps reporting the identity it was opened with) and the last operation's error text.</summary>
    private sealed class ClawSensorProbeSession(ClawSensorProbeCoordinator coordinator, string manufacturer, string model, string baseBoard, string resolvedModel)
    {
        public ClawSensorProbeCoordinator Coordinator { get; } = coordinator;
        public string Manufacturer { get; } = manufacturer;
        public string Model { get; } = model;
        public string BaseBoard { get; } = baseBoard;
        public string ResolvedModel { get; } = resolvedModel;
        public string? ErrorMessage { get; set; }
        public required string HardwareStatus { get; init; }
        public required string HardwareFamily { get; init; }
        public required string HardwareModel { get; init; }
        public required string HardwareReason { get; init; }
    }
    private sealed class FanProbeSession(MsiFanHardwareProbe probe, string manufacturer, string model, string board)
    { public MsiFanHardwareProbe Probe { get; } = probe; public string Manufacturer { get; } = manufacturer; public string Model { get; } = model; public string Board { get; } = board; public FrontendFanProbeSnapshot? LastResult { get; set; } }
    // Device/Profile CPU Boost -- a sibling capability, not a member of Routing/OEM1 (work order
    // PR277 section 1): this projection deliberately has NO dependency on _runtime/routing status
    // and must keep working when _runtime is null (no routing composition at all).
    private readonly CpuBoostRuntime? _cpuBoostRuntime;
    private readonly PowerModeRuntime? _powerModeRuntime;
    private readonly IntelFrameLimiterRuntime? _intelFpsRuntime;
    private readonly IMsiClawTdpTransport? _fanProbeTransport;
    private readonly TdpRuntime? _tdpRuntime;
    private readonly GameProfileMutations? _gameProfileMutations;
    private readonly Func<uint> _actualRunningAppIdSource;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ProfileGameCatalogEntry>>> _scanProfileGames;
    private readonly GameDisplayResolutionRuntime? _displayResolutionRuntime;
    // Narrow MSI Center M startup control (work order PR1). Null is a valid passive state -- the
    // capture/mutation just report unavailable, like every other null-runtime fallback here.
    private readonly CenterMStartupControl? _centerMStartup;
    // The reboot-bound MSI Center M controller-authority transition owner (work order PR3). Null is a
    // valid passive state -- the request just reports unavailable, like every other null fallback here.
    private readonly ICenterMRebootAuthorityTransition? _centerMAuthorityTransition;

    /// <param name="frontButtonMappingAvailable">The startup hardware-support result
    /// (<see cref="Startup.StartupResult.HardwareSupported"/>), reported verbatim on bootstrap so the
    /// UI gates the front-button mapping feature on the SAME fact the front-button runtime gates on.
    /// Defaults to false so any construction path that never established hardware support reports the
    /// feature unavailable rather than offering it.</param>
    /// <param name="cpuBoostRuntime">The Device/Profile CPU Boost Runtime authority (owned by
    /// <c>AddonProcessHost</c>, independent of <paramref name="runtime"/>). Null is a valid, passive
    /// state -- CPU Boost frontend operations simply report unavailable, exactly like every other
    /// null-runtime fallback on this class.</param>
    internal InProcessAddonFrontendControl(StartupSettingsCoordinator settings, ISystemStatusProvider status, AddonRuntimeHost? runtime, DeveloperTestModeState developer, IFrontendPrerequisiteSetupExecutor? setupExecutor = null, Func<string?>? processPath = null, bool frontButtonMappingAvailable = false, CpuBoostRuntime? cpuBoostRuntime = null, TdpRuntime? tdpRuntime = null, GameProfileMutations? gameProfileMutations = null, Func<uint>? actualRunningAppIdSource = null, Func<CancellationToken, Task<IReadOnlyList<ProfileGameCatalogEntry>>>? scanProfileGames = null, GameDisplayResolutionRuntime? displayResolutionRuntime = null, PowerModeRuntime? powerModeRuntime = null, IntelFrameLimiterRuntime? intelFpsRuntime = null, IMsiClawTdpTransport? fanProbeTransport = null, CenterMStartupControl? centerMStartup = null, ICenterMRebootAuthorityTransition? centerMAuthorityTransition = null)
    {
        _frontButtonMappingAvailable = frontButtonMappingAvailable;
        _centerMStartup = centerMStartup;
        _centerMAuthorityTransition = centerMAuthorityTransition;
        _cpuBoostRuntime = cpuBoostRuntime;
        _powerModeRuntime = powerModeRuntime;
        _intelFpsRuntime = intelFpsRuntime;
        _tdpRuntime = tdpRuntime;
        _gameProfileMutations = gameProfileMutations;
        _actualRunningAppIdSource = actualRunningAppIdSource ?? (() => _runtime?.ActualRunningAppId ?? 0);
        _scanProfileGames = scanProfileGames ?? (token => new ProfileGameCatalogScanner().ScanAsync(token));
        _displayResolutionRuntime = displayResolutionRuntime;
        _fanProbeTransport = fanProbeTransport;
        _settings = settings;
        _status = status;
        _runtime = runtime;
        if (_runtime is not null) _runtime.PowerResumeObserved += OnPowerResumeObserved;
        _developer = developer;
        _setupExecutor = setupExecutor ?? new FrontendPrerequisiteSetupExecutor();
        _processPath = processPath ?? (() => Environment.ProcessPath);
        if (_runtime is not null)
        {
            _runtime.ActualRunningAppIdChanged += _ => StateInvalidated?.Invoke(this, EventArgs.Empty);
            _runtime.StatusRefreshRequested += (_, _) => StateInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? StateInvalidated;

    public async Task<IReadOnlyList<FrontendProfileGameCatalogEntry>> ScanProfileGamesAsync(CancellationToken cancellationToken = default)
    {
        var favorites = _gameProfileMutations?.CaptureFavoriteAppIds() ?? new HashSet<uint>();
        return (await _scanProfileGames(cancellationToken).ConfigureAwait(false)).Select(x => new FrontendProfileGameCatalogEntry(x.AppId, x.Name, x.Source == ProfileGameSource.Steam ? FrontendProfileGameSource.Steam : FrontendProfileGameSource.NonSteam, favorites.Contains(x.AppId))).ToArray();
    }

    public Task<FrontendGameProfileMutationResult> SetGameProfileFavoriteAsync(uint appId, bool favorite, string? displayName, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var outcome = _gameProfileMutations?.SetFavorite(appId, favorite, displayName) ?? GameProfileMutations.MutationOutcome.Unavailable;
        return Task.FromResult(MutateGame(appId, outcome, cpu: false, tdp: false));
    }

    public Task<FrontendGameProfileSnapshot> CaptureGameProfileAsync(uint appId, CancellationToken cancellationToken = default) => Task.FromResult(CaptureGameProfile(appId));
    public async Task<FrontendGameProfileSnapshot> CaptureActiveGameProfileAsync(CancellationToken cancellationToken = default)
    {
        var appId = _actualRunningAppIdSource();
        if (appId == 0) return UnavailableGameProfile(0);
        var snapshot = CaptureGameProfile(appId);
        if (!string.IsNullOrWhiteSpace(snapshot.DisplayName)) return snapshot;
        var game = (await _scanProfileGames(cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.AppId == appId);
        return game is null ? snapshot : snapshot with { DisplayName = game.Name };
    }

    public Task<FrontendGameProfileMutationResult> SetGameProfileEnabledAsync(uint appId, bool enabled, string? displayName, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var outcome = _gameProfileMutations?.SetEnabled(appId, enabled, displayName) ?? GameProfileMutations.MutationOutcome.Unavailable;
        if (outcome != GameProfileMutations.MutationOutcome.Succeeded)
            return Task.FromResult(MutateGame(appId, outcome, cpu: true, tdp: true));

        // Keep the existing CPU/TDP reconcile behavior, then give each active-profile
        // sibling a chance to converge independently without rolling back persistence.
        ReconcileGame(appId, cpu: true, tdp: true);
        string? applyFailure = null;
        if (appId == _actualRunningAppIdSource())
        {
            if (_powerModeRuntime is { } powerModeRuntime)
            {
                var applied = powerModeRuntime.ReconcileWithResult(appId);
                if (!applied.Succeeded) applyFailure = applied.FailureMessage ?? "Power Mode apply failed.";
            }
            if (_intelFpsRuntime is { } fpsRuntime)
            {
                var applied = fpsRuntime.ReconcileWithResult(appId);
                if (!applied && applyFailure is null) applyFailure = "Intel FPS Limit apply failed.";
            }
        }
        StateInvalidated?.Invoke(this, EventArgs.Empty);

        return Task.FromResult(new FrontendGameProfileMutationResult(
            applyFailure is null ? FrontendGameProfileMutationOutcome.Succeeded : FrontendGameProfileMutationOutcome.ApplyFailed,
            applyFailure,
            CaptureGameProfile(appId)));
    }

    public Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostEnabledAsync(uint appId, bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var outcome = _gameProfileMutations?.SetCpuBoostEnabled(appId, enabled) ?? GameProfileMutations.MutationOutcome.Unavailable;
        if (outcome != GameProfileMutations.MutationOutcome.Succeeded)
            return Task.FromResult(MutateGame(appId, outcome, cpu: false, tdp: false));

        if (appId == _actualRunningAppIdSource() && _cpuBoostRuntime is { } runtime)
        {
            var applied = runtime.ReconcileWithResult(appId);
            StateInvalidated?.Invoke(this, EventArgs.Empty);
            if (!applied.Succeeded)
                return Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.ApplyFailed, applied.FailureMessage ?? "CPU Boost apply failed.", CaptureGameProfile(appId)));
        }
        else
        {
            StateInvalidated?.Invoke(this, EventArgs.Empty);
        }

        return Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Succeeded, null, CaptureGameProfile(appId)));
    }

    public Task<FrontendGameProfileMutationResult> SetGameProfileTdpEnabledAsync(uint appId, bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        return Task.FromResult(MutateGame(appId, _gameProfileMutations?.SetTdpEnabled(appId, enabled) ?? GameProfileMutations.MutationOutcome.Unavailable, cpu: false, tdp: true));
    }

    public Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeEnabledAsync(uint appId, bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var outcome = _gameProfileMutations?.SetPowerModeEnabled(appId, enabled) ?? GameProfileMutations.MutationOutcome.Unavailable;
        if (outcome != GameProfileMutations.MutationOutcome.Succeeded)
            return Task.FromResult(MutateGame(appId, outcome, cpu: false, tdp: false));

        if (appId == _actualRunningAppIdSource() && _powerModeRuntime is { } runtime)
        {
            var applied = runtime.ReconcileWithResult(appId);
            StateInvalidated?.Invoke(this, EventArgs.Empty);
            if (!applied.Succeeded)
                return Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.ApplyFailed, applied.FailureMessage ?? "Power Mode apply failed.", CaptureGameProfile(appId)));
        }
        else
        {
            StateInvalidated?.Invoke(this, EventArgs.Empty);
        }

        return Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Succeeded, null, CaptureGameProfile(appId)));
    }

    public Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostAcAsync(uint appId, CpuBoostMode mode, CancellationToken cancellationToken = default) =>
        MutateCpuBoostAfterShutdownCheck(appId, static (mutations, id, value) => mutations.SetCpuBoostAc(id, value), mode);

    public Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostDcAsync(uint appId, CpuBoostMode mode, CancellationToken cancellationToken = default) =>
        MutateCpuBoostAfterShutdownCheck(appId, static (mutations, id, value) => mutations.SetCpuBoostDc(id, value), mode);
    public Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeAcAsync(uint appId, WindowsPowerMode mode, CancellationToken cancellationToken = default) =>
        MutatePowerModeAfterShutdownCheck(appId, mode, ac: true);
    public Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeDcAsync(uint appId, WindowsPowerMode mode, CancellationToken cancellationToken = default) =>
        MutatePowerModeAfterShutdownCheck(appId, mode, ac: false);
    public Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitEnabledAsync(uint appId, bool enabled, CancellationToken cancellationToken = default) => MutateFps(appId, m => m.SetFpsLimitEnabled(appId, enabled));
    public Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitAcAsync(uint appId, int fps, CancellationToken cancellationToken = default) => MutateFps(appId, m => m.SetFpsLimitAc(appId, fps));
    public Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitDcAsync(uint appId, int fps, CancellationToken cancellationToken = default) => MutateFps(appId, m => m.SetFpsLimitDc(appId, fps));
    private Task<FrontendGameProfileMutationResult> MutateFps(uint appId, Func<GameProfileMutations, GameProfileMutations.MutationOutcome> mutation)
    {
        ThrowIfShuttingDown(); var outcome = _gameProfileMutations is { } m ? mutation(m) : GameProfileMutations.MutationOutcome.Unavailable;
        if (outcome != GameProfileMutations.MutationOutcome.Succeeded) return Task.FromResult(MutateGame(appId, outcome, false, false));
        if (appId == _actualRunningAppIdSource() && _intelFpsRuntime is { } runtime)
        { var applied = runtime.ReconcileWithResult(appId); StateInvalidated?.Invoke(this, EventArgs.Empty); if (!applied) return Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.ApplyFailed, "Intel FPS Limit apply failed.", CaptureGameProfile(appId))); }
        else StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Succeeded, null, CaptureGameProfile(appId)));
    }
    private Task<FrontendGameProfileMutationResult> MutatePowerModeAfterShutdownCheck(uint appId, WindowsPowerMode mode, bool ac)
    {
        ThrowIfShuttingDown();
        var outcome = _gameProfileMutations is { } mutations
            ? ac ? mutations.SetPowerModeAc(appId, mode) : mutations.SetPowerModeDc(appId, mode)
            : GameProfileMutations.MutationOutcome.Unavailable;
        if (outcome == GameProfileMutations.MutationOutcome.Succeeded && appId == _actualRunningAppIdSource() && _powerModeRuntime is { } runtime)
        {
            var applied = runtime.ReconcileWithResult(appId);
            StateInvalidated?.Invoke(this, EventArgs.Empty);
            if (!applied.Succeeded)
                return Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.ApplyFailed, applied.FailureMessage, CaptureGameProfile(appId)));
            return Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Succeeded, null, CaptureGameProfile(appId)));
        }
        return Task.FromResult(MutateGame(appId, outcome, cpu: false, tdp: false, power: true));
    }

    public Task<FrontendGameProfileMutationResult> SetGameProfileTdpAsync(uint appId, FrontendGameTdpConfiguration configuration, CancellationToken cancellationToken = default) =>
        MutateTdpAfterShutdownCheck(appId, configuration);

    public Task<FrontendGameProfileMutationResult> SetGameProfileResolutionAsync(uint appId, FrontendGameResolution? resolution, string? displayName, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var target = resolution is null ? null : new GameDisplayResolution { Width = resolution.Width, Height = resolution.Height };
        return Task.FromResult(MutateGame(appId, _gameProfileMutations?.SetResolution(appId, target, displayName) ?? GameProfileMutations.MutationOutcome.Unavailable, cpu: false, tdp: false, display: true));
    }

    private Task<FrontendGameProfileMutationResult> MutateCpuBoostAfterShutdownCheck(uint appId, Func<GameProfileMutations, uint, CpuBoostMode, GameProfileMutations.MutationOutcome> mutation, CpuBoostMode mode)
    {
        ThrowIfShuttingDown();
        return Task.FromResult(MutateGame(appId, _gameProfileMutations is { } mutations ? mutation(mutations, appId, mode) : GameProfileMutations.MutationOutcome.Unavailable, cpu: true, tdp: false));
    }

    private Task<FrontendGameProfileMutationResult> MutateTdpAfterShutdownCheck(uint appId, FrontendGameTdpConfiguration configuration)
    {
        ThrowIfShuttingDown();
        return Task.FromResult(MutateGame(appId, _gameProfileMutations?.SetTdp(appId, new() { Pl1Watts = configuration.Ac.Pl1Watts, Pl2Watts = configuration.Ac.Pl2Watts }, new() { Pl1Watts = configuration.Dc.Pl1Watts, Pl2Watts = configuration.Dc.Pl2Watts }) ?? GameProfileMutations.MutationOutcome.Unavailable, cpu: false, tdp: true));
    }

    private FrontendGameProfileSnapshot CaptureGameProfile(uint appId)
    {
        var captured = _gameProfileMutations?.CaptureProfile(appId);
        if (captured is null) return UnavailableGameProfile(appId);
        var profile = captured.Profile;
        var limits = _tdpRuntime?.CaptureSnapshot().Policy is { } policy ? new FrontendTdpLimits(policy.Pl1MinimumWatts, policy.Pl1MaximumWatts, policy.Pl2MinimumWatts, policy.Pl2MaximumWatts) : null;
        return new(appId, profile.DisplayName, captured.Exists, captured.Exists && profile.Enabled,
            new(profile.Performance.CpuBoost!.Enabled, profile.Performance.CpuBoost.Ac, profile.Performance.CpuBoost.Dc),
            new(profile.Performance.Tdp!.Enabled, new(profile.Performance.Tdp.Ac.Pl1Watts, profile.Performance.Tdp.Ac.Pl2Watts), new(profile.Performance.Tdp.Dc.Pl1Watts, profile.Performance.Tdp.Dc.Pl2Watts)), captured.PersistenceWritable, limits,
            profile.Display.Resolution is { } resolution ? new(resolution.Width, resolution.Height) : null,
            profile.Performance.PowerMode is { } power ? new(power.Enabled, power.Ac, power.Dc) : null,
            new(profile.Performance.FpsLimit?.Enabled == true, profile.Performance.FpsLimit?.AcFps ?? 60, profile.Performance.FpsLimit?.DcFps ?? 60, _intelFpsRuntime?.Available == true, _intelFpsRuntime?.UnavailableReason));
    }

    private FrontendGameProfileMutationResult MutateGame(uint appId, GameProfileMutations.MutationOutcome outcome, bool cpu, bool tdp, bool display = false, bool power = false)
    {
        var mapped = outcome switch { GameProfileMutations.MutationOutcome.Succeeded => FrontendGameProfileMutationOutcome.Succeeded, GameProfileMutations.MutationOutcome.InvalidTarget => FrontendGameProfileMutationOutcome.InvalidTarget, GameProfileMutations.MutationOutcome.PersistenceFailed => FrontendGameProfileMutationOutcome.PersistenceFailed, _ => FrontendGameProfileMutationOutcome.Unavailable };
        if (outcome == GameProfileMutations.MutationOutcome.Succeeded)
        {
            ReconcileGame(appId, cpu, tdp, display, power); StateInvalidated?.Invoke(this, EventArgs.Empty);
        }
        return new(mapped, mapped == FrontendGameProfileMutationOutcome.Succeeded ? null : "Game Profile mutation failed.", CaptureGameProfile(appId));
    }

    private void ReconcileGame(uint appId, bool cpu, bool tdp, bool display = false, bool power = false)
    {
        if (appId != _actualRunningAppIdSource()) return;
        if (cpu) try { _cpuBoostRuntime?.Reconcile(appId); } catch (Exception ex) { AppLog.Error("Profiles.CpuBoost", "Game Profile CPU reconcile failed.", ex); }
        if (tdp) try { _tdpRuntime?.ReconcileCurrent(true, false, "GameProfileMutation"); } catch (Exception ex) { AppLog.Error("Profiles.Tdp", "Game Profile TDP reconcile failed.", ex); }
        if (display) try { _displayResolutionRuntime?.Reconcile(appId); } catch (Exception ex) { AppLog.Error("Profiles.Display", "Game Profile display reconcile failed.", ex); }
        if (power) try { _powerModeRuntime?.Reconcile(appId); } catch (Exception ex) { AppLog.Error("Profiles.PowerMode", "Game Profile Power Mode reconcile failed.", ex); }
    }

    private static FrontendGameProfileSnapshot UnavailableGameProfile(uint appId) => new(appId, null, false, false, new(false, CpuBoostMode.Enabled, CpuBoostMode.Enabled), new(false, new(20, 22), new(20, 22)), false, null, FpsLimit: new(false, 60, 60, false, "Intel FPS Limit is unavailable."));
    private FrontendGameProfileMutationResult UnavailableMutation(uint appId, string message) => new(FrontendGameProfileMutationOutcome.Unavailable, message, CaptureGameProfile(appId));

    public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken cancellationToken = default) => Task.FromResult(new FrontendBootstrapSnapshot(MapSettings(), new(_developer.IsEnabled), AppLog.DirectoryPath, _frontButtonMappingAvailable));

    public async Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var setup = _setupExecutor.Evaluate(snapshot);
        return FrontendSnapshotMapper.ApplySetup(FrontendSnapshotMapper.Map(snapshot), setup);
    }

    public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        _settings.ChangeLogLevel(level switch { FrontendLogLevel.Debug => AppLogPreference.Debug, FrontendLogLevel.Info => AppLogPreference.Info, _ => AppLogPreference.Off });
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendSettingsSnapshot> SetFrontButtonMappingAsync(Contracts.FrontButtons.FrontButtonMappingSettings mapping, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ThrowIfShuttingDown();
        // Invalid candidates are rejected inside ChangeFrontButtonMapping (no write, no publish); the
        // snapshot below then just reflects the unchanged persisted state.
        _settings.ChangeFrontButtonMapping(mapping);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        _settings.SuppressDeveloperMenuWarningPermanently();
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        _developer.SetEnabled(enabled);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendDeveloperSnapshot(_developer.IsEnabled));
    }

    public async Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var current = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var setup = _setupExecutor.Evaluate(current);
        AppLog.Info("PrerequisiteSetup", "Prerequisite setup requested.",
            ("HidHideStatus", current.Prerequisites.HidHide.Status),
            ("UsbIpWin2Status", current.Prerequisites.UsbIpWin2.Status),
            ("SteamActive", current.Steam.IsActive),
            ("RecoverySafe", current.RecoverySafe),
            ("SetupStatus", setup.Status));
        var mapped = FrontendSnapshotMapper.ApplySetup(FrontendSnapshotMapper.Map(current), setup);
        if (!PrerequisiteSetupPromptPolicy.IsInstallable(setup))
            return new(FrontendPrerequisiteSetupResultKind.NotInstallable, mapped);
        ThrowIfShuttingDown();
        var executable = _processPath() ?? throw new InvalidOperationException("The executable path is unavailable.");
        var result = await _setupExecutor.RunAsync(setup, executable, cancellationToken).ConfigureAwait(false);
        // RunIfInstallableAsync returns null only when its safety policy declines to launch.
        // Preserve that distinction from an elevated helper that actually returns Blocked.
        if (result is null) return new(FrontendPrerequisiteSetupResultKind.NotInstallable, mapped);
        var resultKind = MapResultKind(ElevatedPrerequisiteSetup.TranslateExitCode(result));
        // No OEM1 reconcile here: HidHide/usbip setup no longer mutates any OEM1 prerequisite. OEM1
        // arming is owned entirely by the mapping-change/startup lifecycle plus the coordinator's own
        // environment/Launcher/Server/process/helper reconciliation.
        FrontendStatusSnapshot? postStatus = null;
        try
        {
            postStatus = await CaptureStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Warn("PrerequisiteSetup", "Post-setup status refresh failed.", exception, ("Result", resultKind));
        }
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return new(resultKind, postStatus);
    }

    private static FrontendPrerequisiteSetupResultKind MapResultKind(ElevatedPrerequisiteSetup.ResultKind kind) => kind switch
    {
        ElevatedPrerequisiteSetup.ResultKind.Ready => FrontendPrerequisiteSetupResultKind.Ready,
        ElevatedPrerequisiteSetup.ResultKind.Installed => FrontendPrerequisiteSetupResultKind.Installed,
        ElevatedPrerequisiteSetup.ResultKind.RebootRequired => FrontendPrerequisiteSetupResultKind.RebootRequired,
        ElevatedPrerequisiteSetup.ResultKind.Cancelled => FrontendPrerequisiteSetupResultKind.Cancelled,
        ElevatedPrerequisiteSetup.ResultKind.Blocked => FrontendPrerequisiteSetupResultKind.Blocked,
        ElevatedPrerequisiteSetup.ResultKind.AlreadyInProgress => FrontendPrerequisiteSetupResultKind.AlreadyInProgress,
        _ => FrontendPrerequisiteSetupResultKind.Failed
    };

    internal void BeginProcessShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;
        if (_runtime is not null) _runtime.PowerResumeObserved -= OnPowerResumeObserved;
        FanProbeSession? fanProbe;
        lock (_fanProbeGate) fanProbe = _fanProbe;
        if (fanProbe is not null)
        {
            try
            {
                fanProbe.Probe.RequestShutdownCleanup();
                var cleanup = fanProbe.Probe.CancelSuspendResumeIfArmed();
                if (cleanup is { Succeeded: false }) AppLog.Warn("MsiFanProbe", "Armed fan probe shutdown hand-back failed.");
                if (!fanProbe.Probe.WaitForShutdownCleanup(TimeSpan.FromSeconds(5))) AppLog.Warn("MsiFanProbe", "Timed out waiting for active fan-probe shutdown cleanup.");
            }
            catch (Exception exception) { AppLog.Warn("MsiFanProbe", "Armed fan probe shutdown cleanup failed.", exception); }
        }
        ClawSensorProbeSession? probe;
        lock (_clawSensorProbeGate) { probe = _clawSensorProbe; _clawSensorProbe = null; }
        if (probe is not null)
        {
            try { probe.Coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception exception) { AppLog.Warn("ClawSensorProbe", "Probe shutdown cleanup failed.", exception); }
        }
    }

    // ---- MSI Fan Probe (developer-only bounded hardware diagnostic) ----
    public async Task<FrontendFanProbeSnapshot> OpenFanProbeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        if (_fanProbeTransport is null) return FrontendFanProbeSnapshot.Unavailable;
        var status = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var board = status.Device.BaseBoardProduct;
        var model = FanProbeModelMap.Resolve(board);
        if (model == FanProbeModel.Unsupported) return new(false, FrontendFanProbeState.Unavailable, "Unsupported MSI board.", status.Device.Manufacturer, status.Device.Model, board, model.ToString(), null, false, "The authoritative board identity is unsupported.");
        lock (_fanProbeGate) _fanProbe ??= new(new MsiFanHardwareProbe(_fanProbeTransport, AppLog.DirectoryPath), status.Device.Manufacturer, status.Device.Model, board);
        return _fanProbe.LastResult ?? MapFanProbe(FrontendFanProbeState.Ready, "Ready", null);
    }

    public async Task<FrontendFanProbeSnapshot> RunFanProbeAsync(FrontendFanProbeOperation operation, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown(); cancellationToken.ThrowIfCancellationRequested();
        FanProbeSession? session; lock (_fanProbeGate) session = _fanProbe;
        if (session is null) return FrontendFanProbeSnapshot.Unavailable;
        var result = await Task.Run(() => operation switch
        {
            FrontendFanProbeOperation.Capture => session.Probe.Capture(session.Model, session.Board, ReadFanProbeFirmwareIdentity()),
            FrontendFanProbeOperation.AutomaticTest => session.Probe.AutomaticTest(session.Model, session.Board, ReadFanProbeFirmwareIdentity()),
            FrontendFanProbeOperation.RestoreAuto => session.Probe.RestoreAuto(session.Model, session.Board, ReadFanProbeFirmwareIdentity()),
            FrontendFanProbeOperation.PhysicalResponse => session.Probe.PhysicalResponse(session.Model, session.Board, ReadFanProbeFirmwareIdentity()),
            _ => session.Probe.ArmSuspendResume(session.Model, session.Board, ReadFanProbeFirmwareIdentity())
        }, CancellationToken.None).ConfigureAwait(false);
        var snapshot = MapFanProbe(result.Succeeded ? FrontendFanProbeState.Completed : FrontendFanProbeState.Failed, result.Status, result.ReportPath);
        session.LastResult = snapshot;
        return snapshot;
    }

    private void OnPowerResumeObserved()
    {
        FanProbeSession? session; lock (_fanProbeGate) session = _fanProbe;
        if (session is null) return;
        _ = Task.Run(() =>
        {
            var result = session.Probe.CompleteSuspendResumeAfterResume();
            if (result is null) return;
            session.LastResult = MapFanProbe(result.Succeeded ? FrontendFanProbeState.Completed : FrontendFanProbeState.Failed, result.Status, result.ReportPath);
        });
    }

    private static string ReadFanProbeFirmwareIdentity()
    {
        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var version = bios?.GetValue("BIOSVersion") switch
            {
                string value when !string.IsNullOrWhiteSpace(value) => value,
                string[] values when values.Length > 0 => string.Join("; ", values),
                _ => "unavailable"
            };
            return $"BIOS: {version}; EC: unavailable";
        }
        catch (Exception exception)
        {
            AppLog.Debug("MsiFanProbe", "Unable to read BIOS version.", ("Exception", exception.GetType().Name));
            return "BIOS: unavailable; EC: unavailable";
        }
    }

    private FrontendFanProbeSnapshot MapFanProbe(FrontendFanProbeState state, string status, string? path)
    {
        FanProbeSession? session; lock (_fanProbeGate) session = _fanProbe;
        if (session is null) return FrontendFanProbeSnapshot.Unavailable;
        return new(true, state, status, session.Manufacturer, session.Model, session.Board, FanProbeModelMap.Resolve(session.Board).ToString(), path, !string.IsNullOrWhiteSpace(path), state == FrontendFanProbeState.Failed ? status : null);
    }

    // ---- Claw Sensor Probe (developer-only gyro/accelerometer diagnostic) ----

    public async Task<FrontendClawSensorProbeSnapshot> OpenClawSensorProbeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        ClawSensorProbeSession? existing;
        lock (_clawSensorProbeGate) existing = _clawSensorProbe;
        if (existing is not null && existing.Coordinator.State is not (ClawSensorProbeState.Completed or ClawSensorProbeState.Failed))
            return MapClawSensorProbeSnapshot(existing);

        var status = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var hardware = status.HardwareCompatibility;
        if (!ClawSensorProbeCoordinator.AllowsReadOnlyDiagnostic(hardware))
            return FrontendClawSensorProbeSnapshot.Unavailable with
            {
                Manufacturer = status.Device.Manufacturer,
                Model = status.Device.Model,
                BaseBoard = status.Device.BaseBoardProduct,
                ErrorMessage = "This diagnostic is available only on an identified MSI Claw device."
            };

        // A previous session that Completed/Failed is disposed and replaced with a fresh one -- the
        // old report/output directory remains on disk, but the next Open starts a clean session.
        if (existing is not null)
            await existing.Coordinator.DisposeAsync().ConfigureAwait(false);

        var resolvedModel = hardware.DeviceModel?.Value ?? "Unknown / unresolved";
        var coordinator = new ClawSensorProbeCoordinator();
        coordinator.Prepare();
        // Identity/compatibility are captured now but NOT written yet: ClawSensorProbeCoordinator's
        // SetDeviceIdentity/SetHardwareCompatibility write through the session writer, which Start()
        // does not create until StartClawSensorProbeAsync() runs. Writing here would silently no-op
        // and drop this metadata from the finalized report (review finding #1 on PR #290).
        var session = new ClawSensorProbeSession(coordinator, status.Device.Manufacturer, status.Device.Model, status.Device.BaseBoardProduct, resolvedModel)
        {
            HardwareStatus = hardware.Status.ToString(),
            HardwareFamily = hardware.DeviceFamily?.Value ?? "Unavailable",
            HardwareModel = hardware.DeviceModel?.Value ?? "Unavailable",
            HardwareReason = hardware.Reason,
        };

        // The initial ThrowIfShuttingDown() above only covers the time before the awaited
        // _status.CaptureAsync() call: BeginProcessShutdown() can run its one-time session
        // detach/dispose pass while this request is suspended there, and the named-pipe server isn't
        // torn down until later in process disposal, so a request already past that first check could
        // otherwise resume and commit a brand-new coordinator after shutdown began. Re-check the flag
        // atomically with the commit under the same gate used by BeginProcessShutdown/Close, and
        // dispose a rejected candidate outside the lock (PR #290 re-review).
        bool rejectForShutdown;
        lock (_clawSensorProbeGate)
        {
            rejectForShutdown = Volatile.Read(ref _shutdownStarted) != 0;
            if (!rejectForShutdown) _clawSensorProbe = session;
        }
        if (rejectForShutdown)
        {
            await coordinator.DisposeAsync().ConfigureAwait(false);
            throw new FrontendProtocolException("Runtime is shutting down.");
        }

        return MapClawSensorProbeSnapshot(session);
    }

    public async Task<FrontendClawSensorProbeSnapshot> StartClawSensorProbeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var session = CurrentClawSensorProbeSession();
        if (session is null) return FrontendClawSensorProbeSnapshot.Unavailable;
        try
        {
            session.Coordinator.Start();
            session.Coordinator.SetDeviceIdentity(session.Manufacturer, session.Model, session.BaseBoard, session.ResolvedModel);
            session.Coordinator.SetHardwareCompatibility(session.HardwareStatus, session.HardwareFamily, session.HardwareModel, session.HardwareReason);

            // Link the RPC's own token with the coordinator's lifecycle token so a Runtime shutdown
            // (BeginProcessShutdown -> coordinator disposal) promptly cancels an in-flight countdown
            // instead of letting it run to BeginRecording() against an already-disposed coordinator
            // (review finding #2 on PR #290).
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Coordinator.LifecycleCancellation);
            await session.Coordinator.StartCaptureAsync(linked.Token).ConfigureAwait(false);
            await session.Coordinator.CountdownAsync(_ => Task.CompletedTask, ClawSensorProbePhaseLabel, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            session.Coordinator.BeginRecording();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            session.ErrorMessage = exception.Message;
            try { await session.Coordinator.FailAsync(exception.Message, CancellationToken.None).ConfigureAwait(false); } catch { /* best-effort */ }
        }
        return MapClawSensorProbeSnapshot(session);
    }

    public async Task<FrontendClawSensorProbeSnapshot> CaptureClawSensorProbeAsync(CancellationToken cancellationToken = default)
    {
        // Obtain the session AND its lifecycle token together, under the same gate
        // BeginProcessShutdown()/Close() use to detach+dispose the coordinator: reading
        // session.Coordinator.LifecycleCancellation outside the lock (after only checking the
        // session reference was non-null) leaves a window where shutdown can dispose the coordinator
        // -- and therefore the CancellationTokenSource backing LifecycleCancellation -- in between,
        // turning an ordinary in-flight poll into an unexpected ObjectDisposedException instead of a
        // graceful Unavailable (PR #290 re-review).
        ClawSensorProbeSession? session;
        CancellationToken lifecycle;
        lock (_clawSensorProbeGate)
        {
            if (Volatile.Read(ref _shutdownStarted) != 0) return FrontendClawSensorProbeSnapshot.Unavailable;
            session = _clawSensorProbe;
            if (session is null) return FrontendClawSensorProbeSnapshot.Unavailable;
            lifecycle = session.Coordinator.LifecycleCancellation;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The old page's 200ms UI timer explicitly promoted a dead sensor reader to a finalized
        // Failed diagnostic (ClawSensorProbeUiTimer_Tick -> FailOnReaderFaultAsync). The restored
        // page polls this method at the same ~200ms cadence, so run the same reconciliation here --
        // FailOnReaderFaultAsync no-ops when there is no fault, so this preserves the old behavior
        // without introducing a second health authority (PR #290 re-review finding #1).
        //
        // Deliberately NOT linked to Coordinator.LifecycleCancellation: FailAsync() cancels that same
        // token as part of entering terminal failure, so a linked token here would self-cancel
        // ShutdownReadersAndApiAsync mid-teardown and skip FinalizeAsync() (PR #290 re-review, fixed
        // at the coordinator level too). Once lifecycle cancellation has already fired (Runtime
        // shutdown/dispose in flight), skip reconciliation entirely and report the session's last
        // known snapshot instead of racing that teardown.
        try
        {
            if (!lifecycle.IsCancellationRequested)
                await session.Coordinator.FailOnReaderFaultAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _shutdownStarted) != 0) { return FrontendClawSensorProbeSnapshot.Unavailable; }

        if (Volatile.Read(ref _shutdownStarted) != 0) return FrontendClawSensorProbeSnapshot.Unavailable;
        return MapClawSensorProbeSnapshot(session);
    }

    public Task<FrontendClawSensorProbeSnapshot> NextClawSensorProbePhaseAsync(CancellationToken cancellationToken = default) =>
        AdvanceClawSensorProbeAsync(forward: true, cancellationToken);

    public Task<FrontendClawSensorProbeSnapshot> PreviousClawSensorProbePhaseAsync(CancellationToken cancellationToken = default) =>
        AdvanceClawSensorProbeAsync(forward: false, cancellationToken);

    private async Task<FrontendClawSensorProbeSnapshot> AdvanceClawSensorProbeAsync(bool forward, CancellationToken cancellationToken)
    {
        ThrowIfShuttingDown();
        var session = CurrentClawSensorProbeSession();
        if (session is null) return FrontendClawSensorProbeSnapshot.Unavailable;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Coordinator.LifecycleCancellation);
            if (forward)
                await session.Coordinator.AdvancePhaseAsync(_ => Task.CompletedTask, ClawSensorProbePhaseLabel, () => { }, linked.Token).ConfigureAwait(false);
            else
                await session.Coordinator.RevisitPreviousPhaseAsync(_ => Task.CompletedTask, ClawSensorProbePhaseLabel, () => { }, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (session.Coordinator.State == ClawSensorProbeState.Completed)
                await session.Coordinator.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            session.ErrorMessage = exception.Message;
            try { await session.Coordinator.FailAsync(exception.Message, CancellationToken.None).ConfigureAwait(false); } catch { /* best-effort */ }
        }
        return MapClawSensorProbeSnapshot(session);
    }

    public async Task<FrontendClawSensorProbeSnapshot> StopClawSensorProbeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var session = CurrentClawSensorProbeSession();
        if (session is null) return FrontendClawSensorProbeSnapshot.Unavailable;
        try { await session.Coordinator.StopAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { session.ErrorMessage = exception.Message; }
        return MapClawSensorProbeSnapshot(session);
    }

    public async Task<FrontendClawSensorProbeSnapshot> CloseClawSensorProbeAsync(CancellationToken cancellationToken = default)
    {
        ClawSensorProbeSession? session;
        lock (_clawSensorProbeGate) { session = _clawSensorProbe; _clawSensorProbe = null; }
        if (session is null) return FrontendClawSensorProbeSnapshot.Unavailable;
        try
        {
            if (session.Coordinator.State is ClawSensorProbeState.Starting or ClawSensorProbeState.Countdown or ClawSensorProbeState.RecordingPhase)
                await session.Coordinator.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) { AppLog.Warn("ClawSensorProbe", "Probe stop-on-close failed.", exception); }
        finally { await session.Coordinator.DisposeAsync().ConfigureAwait(false); }
        return FrontendClawSensorProbeSnapshot.Unavailable;
    }

    private ClawSensorProbeSession? CurrentClawSensorProbeSession() { lock (_clawSensorProbeGate) return _clawSensorProbe; }

    private static string ClawSensorProbePhaseLabel(ClawSensorProbePhase phase) => phase switch
    {
        ClawSensorProbePhase.REST => "Keep Still",
        ClawSensorProbePhase.ROLL_LEFT => "Roll Left",
        ClawSensorProbePhase.ROLL_RIGHT => "Roll Right",
        ClawSensorProbePhase.PITCH_UP => "Pitch Up",
        ClawSensorProbePhase.PITCH_DOWN => "Pitch Down",
        ClawSensorProbePhase.YAW_LEFT => "Yaw Left",
        _ => "Yaw Right"
    };

    private static FrontendClawSensorProbeSnapshot MapClawSensorProbeSnapshot(ClawSensorProbeSession session)
    {
        var coordinator = session.Coordinator;
        var workflow = coordinator.Workflow;
        var phase = workflow.CurrentIndex >= 0 ? workflow.Visits[^1].Phase : ClawSensorProbePhase.REST;
        var gyro = coordinator.LiveSnapshot?.Gyro;
        var accel = coordinator.LiveSnapshot?.Accel;
        return new FrontendClawSensorProbeSnapshot(
            Available: true,
            State: MapClawSensorProbeState(coordinator.State),
            Phase: MapClawSensorProbePhase(phase),
            PhaseIndex: workflow.CurrentIndex,
            PhaseCount: ClawSensorProbeWorkflow.Phases.Count,
            Discovery: MapClawSensorProbeDiscovery(coordinator.Discovery),
            Gyro: gyro is { } g ? new(g.X, g.Y, g.Z, g.Hz, g.Count) : FrontendClawSensorProbeAxisSnapshot.Empty,
            Accel: accel is { } a ? new(a.X, a.Y, a.Z, a.Hz, a.Count) : FrontendClawSensorProbeAxisSnapshot.Empty,
            GyroscopeSummary: MapClawSensorProbeStatistics(coordinator.GyroscopeSummary),
            AccelerometerSummary: MapClawSensorProbeStatistics(coordinator.AccelerometerSummary),
            DroppedSampleCount: coordinator.DroppedSampleCount,
            DroppedGyroscopeCount: coordinator.DroppedGyroscopeCount,
            DroppedAccelerometerCount: coordinator.DroppedAccelerometerCount,
            ReaderErrors: coordinator.ReaderErrors,
            OutputDirectory: coordinator.OutputDirectory,
            HasReport: coordinator.HasReport,
            ErrorMessage: session.ErrorMessage,
            Manufacturer: session.Manufacturer,
            Model: session.Model,
            BaseBoard: session.BaseBoard,
            ResolvedModel: session.ResolvedModel);
    }

    private static FrontendClawSensorProbeState MapClawSensorProbeState(ClawSensorProbeState state) => state switch
    {
        ClawSensorProbeState.Idle => FrontendClawSensorProbeState.Idle,
        ClawSensorProbeState.Discovering => FrontendClawSensorProbeState.Discovering,
        ClawSensorProbeState.Ready => FrontendClawSensorProbeState.Ready,
        ClawSensorProbeState.Starting => FrontendClawSensorProbeState.Starting,
        ClawSensorProbeState.Countdown => FrontendClawSensorProbeState.Countdown,
        ClawSensorProbeState.RecordingPhase => FrontendClawSensorProbeState.RecordingPhase,
        ClawSensorProbeState.Stopping => FrontendClawSensorProbeState.Stopping,
        ClawSensorProbeState.Completed => FrontendClawSensorProbeState.Completed,
        _ => FrontendClawSensorProbeState.Failed
    };

    private static FrontendClawSensorProbePhase MapClawSensorProbePhase(ClawSensorProbePhase phase) => phase switch
    {
        ClawSensorProbePhase.REST => FrontendClawSensorProbePhase.Rest,
        ClawSensorProbePhase.ROLL_LEFT => FrontendClawSensorProbePhase.RollLeft,
        ClawSensorProbePhase.ROLL_RIGHT => FrontendClawSensorProbePhase.RollRight,
        ClawSensorProbePhase.PITCH_UP => FrontendClawSensorProbePhase.PitchUp,
        ClawSensorProbePhase.PITCH_DOWN => FrontendClawSensorProbePhase.PitchDown,
        ClawSensorProbePhase.YAW_LEFT => FrontendClawSensorProbePhase.YawLeft,
        _ => FrontendClawSensorProbePhase.YawRight
    };

    private static FrontendClawSensorProbeDiscovery? MapClawSensorProbeDiscovery(ClawSensorDiscovery? discovery)
    {
        if (discovery is null) return null;
        return new FrontendClawSensorProbeDiscovery(
            [.. discovery.Sensors.Select(MapClawSensorProbeCandidate)],
            discovery.Gyroscope is { } gyro ? MapClawSensorProbeCandidate(gyro) : null,
            discovery.Accelerometer is { } accel ? MapClawSensorProbeCandidate(accel) : null,
            discovery.Errors,
            discovery.IsValid);
    }

    private static FrontendClawSensorProbeCandidate MapClawSensorProbeCandidate(ClawSensorProbeCandidate candidate) => new(
        candidate.FriendlyName, candidate.SensorId, candidate.TypeGuid, candidate.CategoryGuid,
        candidate.Manufacturer, candidate.Model, candidate.PersistentUniqueId, candidate.MinimumReportInterval, candidate.CustomUsage);

    private static FrontendClawSensorProbeStatistics? MapClawSensorProbeStatistics(ClawSensorProbeStatistics? statistics) => statistics is null
        ? null
        : new(statistics.SampleCount, statistics.DroppedSampleCount, statistics.DurationMs, statistics.AverageIntervalMs, statistics.MinimumIntervalMs, statistics.MaximumIntervalMs, statistics.EffectiveHz);

    private void ThrowIfShuttingDown()
    {
        if (Volatile.Read(ref _shutdownStarted) != 0)
            throw new FrontendProtocolException("Runtime is shutting down.");
    }

    public async Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await new EnvironmentDiscoveryReportGenerator(new WindowsEnvironmentDiscoverySnapshotSource(), new EnvironmentDiscoveryReportStore(AppLog.DirectoryPath), new EnvironmentDiscoveryReportWriter()).GenerateAsync().ConfigureAwait(false);
            return new(true, null);
        }
        catch (Exception exception)
        {
            AppLog.Warn("EnvironmentDiscovery", "Environment discovery report generation failed.", exception, ("Reason", exception.GetType().Name));
            return new(false, exception.Message);
        }
    }

    private FrontendSettingsSnapshot MapSettings() => new FrontendSettingsSnapshot(_settings.Settings.LogLevel switch { AppLogPreference.Debug => FrontendLogLevel.Debug, AppLogPreference.Info => FrontendLogLevel.Info, _ => FrontendLogLevel.Off }, _settings.SuppressDeveloperMenuWarning, _settings.FrontButtonMapping) with { DeveloperMenuEnabled = _settings.Settings.DeveloperMenuEnabled };

    // ---- Device/Profile CPU Boost (work order PR277) -- deliberately independent of Routing/OEM1:
    // none of these three methods reads _runtime, _captureRoutingStatus, or any routing/Steam/OEM1
    // state. Read-only: CaptureCpuBoostAsync never mutates ProfileStore or Windows (section 8/21). ----

    public Task<FrontendCpuBoostSnapshot> CaptureCpuBoostAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_cpuBoostRuntime is null ? FrontendCpuBoostSnapshot.Unavailable : MapCpuBoostSnapshot(_cpuBoostRuntime.Snapshot));

    public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostAcAsync(CpuBoostMode mode, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        return Task.FromResult(MutateCpuBoost(ac: true, mode));
    }

    public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostDcAsync(CpuBoostMode mode, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        return Task.FromResult(MutateCpuBoost(ac: false, mode));
    }

    /// <summary>Device CPU Boost Toggle addendum: turns the Device/global apply path on or off.
    /// Not an application-wide switch, never gates a future Game Profile CPU Boost path.</summary>
    public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        if (_cpuBoostRuntime is null)
            return Task.FromResult(new FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome.PersistenceFailed, "CPU Boost is unavailable.", FrontendCpuBoostSnapshot.Unavailable));

        var result = _cpuBoostRuntime.SetDeviceCpuBoostEnabled(enabled);
        var snapshot = MapCpuBoostSnapshot(_cpuBoostRuntime.Snapshot);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendCpuBoostMutationResult(MapMutationOutcome(result.Outcome), result.FailureMessage, snapshot));
    }

    public Task<FrontendPowerModeSnapshot> CapturePowerModeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_powerModeRuntime is null ? FrontendPowerModeSnapshot.Unavailable : MapPowerModeSnapshot(_powerModeRuntime.Snapshot));
    public Task<FrontendPowerModeMutationResult> SetDevicePowerModeAcAsync(WindowsPowerMode mode, CancellationToken cancellationToken = default) => Task.FromResult(MutatePowerMode(true, mode));
    public Task<FrontendPowerModeMutationResult> SetDevicePowerModeDcAsync(WindowsPowerMode mode, CancellationToken cancellationToken = default) => Task.FromResult(MutatePowerMode(false, mode));
    public Task<FrontendPowerModeMutationResult> SetDevicePowerModeEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        if (_powerModeRuntime is null) return Task.FromResult(new FrontendPowerModeMutationResult(FrontendPowerModeMutationOutcome.PersistenceFailed, "Power Mode is unavailable.", FrontendPowerModeSnapshot.Unavailable));
        var result = _powerModeRuntime.SetEnabled(enabled); StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendPowerModeMutationResult(MapPowerModeOutcome(result.Outcome), result.FailureMessage, MapPowerModeSnapshot(_powerModeRuntime.Snapshot)));
    }
    private FrontendPowerModeMutationResult MutatePowerMode(bool ac, WindowsPowerMode mode)
    {
        ThrowIfShuttingDown();
        if (_powerModeRuntime is null) return new(FrontendPowerModeMutationOutcome.PersistenceFailed, "Power Mode is unavailable.", FrontendPowerModeSnapshot.Unavailable);
        var result = ac ? _powerModeRuntime.SetDeviceAc(mode) : _powerModeRuntime.SetDeviceDc(mode);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return new(MapPowerModeOutcome(result.Outcome), result.FailureMessage, MapPowerModeSnapshot(_powerModeRuntime.Snapshot));
    }
    private static FrontendPowerModeMutationOutcome MapPowerModeOutcome(PowerModeMutationOutcome outcome) => outcome switch { PowerModeMutationOutcome.Succeeded => FrontendPowerModeMutationOutcome.Succeeded, PowerModeMutationOutcome.ApplyFailed => FrontendPowerModeMutationOutcome.ApplyFailed, _ => FrontendPowerModeMutationOutcome.PersistenceFailed };
    private static FrontendPowerModeSnapshot MapPowerModeSnapshot(PowerModeRuntimeSnapshot s) => new(MapPowerModeSide(s.AcCurrent, s.AcDesired), MapPowerModeSide(s.DcCurrent, s.DcDesired), s.Enabled, s.PersistenceWritable, s.LastFailure);
    private static FrontendPowerModeSideSnapshot MapPowerModeSide(PowerModeSideReading r, WindowsPowerMode? desired) => new(r.Status switch { PowerModeReadStatus.Known => FrontendPowerModeReadStatus.Known, PowerModeReadStatus.Unknown => FrontendPowerModeReadStatus.Unknown, _ => FrontendPowerModeReadStatus.Unavailable }, r.Mode, desired);

    private FrontendCpuBoostMutationResult MutateCpuBoost(bool ac, CpuBoostMode mode)
    {
        if (_cpuBoostRuntime is null)
            return new FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome.PersistenceFailed, "CPU Boost is unavailable.", FrontendCpuBoostSnapshot.Unavailable);

        var result = ac ? _cpuBoostRuntime.SetDeviceCpuBoostAc(mode) : _cpuBoostRuntime.SetDeviceCpuBoostDc(mode);
        var snapshot = MapCpuBoostSnapshot(_cpuBoostRuntime.Snapshot);
        // Fire regardless of outcome: PersistenceFailed means the page must refresh/restore to the
        // authoritative (unchanged) snapshot, and ApplyFailed means the NEW desired value is now
        // authoritative -- both are real state changes the page must re-render (work order section 7).
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return new FrontendCpuBoostMutationResult(MapMutationOutcome(result.Outcome), result.FailureMessage, snapshot);
    }

    private static FrontendCpuBoostMutationOutcome MapMutationOutcome(CpuBoostMutationOutcome outcome) => outcome switch
    {
        CpuBoostMutationOutcome.Succeeded => FrontendCpuBoostMutationOutcome.Succeeded,
        CpuBoostMutationOutcome.PersistenceFailed => FrontendCpuBoostMutationOutcome.PersistenceFailed,
        _ => FrontendCpuBoostMutationOutcome.ApplyFailed
    };

    private static FrontendCpuBoostSnapshot MapCpuBoostSnapshot(CpuBoostRuntimeSnapshot snapshot) => new(
        MapCpuBoostSide(snapshot.AcCurrent, snapshot.AcDesired),
        MapCpuBoostSide(snapshot.DcCurrent, snapshot.DcDesired),
        snapshot.Enabled,
        snapshot.PersistenceWritable,
        snapshot.LastFailure);

    private static FrontendCpuBoostSideSnapshot MapCpuBoostSide(CpuBoostSideReading current, CpuBoostMode? desired) => new(
        current.Status switch
        {
            CpuBoostReadStatus.Known => FrontendCpuBoostReadStatus.Known,
            CpuBoostReadStatus.Unknown => FrontendCpuBoostReadStatus.Unknown,
            _ => FrontendCpuBoostReadStatus.Unavailable
        },
        current.Mode,
        desired);

    public Task<FrontendTdpSnapshot> CaptureTdpAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        return Task.FromResult(_tdpRuntime is null ? FrontendTdpSnapshot.Unavailable : MapTdpSnapshot(_tdpRuntime.CaptureSnapshot()));
    }

    public async Task<FrontendTdpMutationResult> SetDeviceTdpAsync(FrontendTdpConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfShuttingDown();
        if (_tdpRuntime is null)
            return new FrontendTdpMutationResult(FrontendTdpMutationOutcome.Unavailable, "TDP is unavailable.", FrontendTdpSnapshot.Unavailable);

        var result = _tdpRuntime.CommitGlobalTdp(new DeviceTdpSettings
        {
            Enabled = configuration.Enabled,
            Ac = new TdpPowerPair { Pl1Watts = configuration.Ac.Pl1Watts, Pl2Watts = configuration.Ac.Pl2Watts },
            Dc = new TdpPowerPair { Pl1Watts = configuration.Dc.Pl1Watts, Pl2Watts = configuration.Dc.Pl2Watts }
        });
        var snapshot = MapTdpSnapshot(_tdpRuntime.CaptureSnapshot());
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        var hardware = result.Completion is null ? null : await result.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new FrontendTdpMutationResult(result.Outcome switch
        {
            TdpCommitOutcome.Succeeded => FrontendTdpMutationOutcome.Succeeded,
            TdpCommitOutcome.InvalidTarget => FrontendTdpMutationOutcome.InvalidTarget,
            TdpCommitOutcome.PersistenceFailed => FrontendTdpMutationOutcome.PersistenceFailed,
            _ => FrontendTdpMutationOutcome.Unavailable
        }, result.FailureMessage, snapshot, hardware is null ? null : new(hardware.Source == TdpPowerSource.AC ? FrontendTdpPowerSource.AC : FrontendTdpPowerSource.DC, hardware.Pl1Watts, hardware.Pl2Watts, hardware.Attempted, hardware.Succeeded));
    }

    public Task<FrontendTdpMutationResult> SetDeviceTdpEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        if (_tdpRuntime is null)
            return Task.FromResult(new FrontendTdpMutationResult(FrontendTdpMutationOutcome.Unavailable, "TDP is unavailable.", FrontendTdpSnapshot.Unavailable));

        var result = _tdpRuntime.SetEnabled(enabled);
        var snapshot = MapTdpSnapshot(_tdpRuntime.CaptureSnapshot());
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendTdpMutationResult(result.Outcome switch
        {
            TdpCommitOutcome.Succeeded => FrontendTdpMutationOutcome.Succeeded,
            TdpCommitOutcome.InvalidTarget => FrontendTdpMutationOutcome.InvalidTarget,
            TdpCommitOutcome.PersistenceFailed => FrontendTdpMutationOutcome.PersistenceFailed,
            _ => FrontendTdpMutationOutcome.Unavailable
        }, result.FailureMessage, snapshot));
    }

    /// <summary>Shared Device Quick Settings aggregate read (Shared Frontend V2, SF-V2-01 section
    /// 8): reuses the existing Runtime authorities/mappers, performs the three reads sequentially
    /// (no cross-feature lock/epoch/parallelization), and isolates a real capture failure to that
    /// child so healthy siblings are still returned. Read-only: never persists, mutates, reconciles,
    /// or raises <see cref="StateInvalidated"/> merely because state was requested.</summary>
    public async Task<FrontendDeviceQuickSettingsSnapshot> CaptureDeviceQuickSettingsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        cancellationToken.ThrowIfCancellationRequested();

        var cpuBoost = FrontendCpuBoostSnapshot.Unavailable;
        try { cpuBoost = await CaptureCpuBoostAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { AppLog.Warn("Device", "CPU Boost snapshot capture failed.", exception, ("Reason", exception.GetType().Name)); }

        var tdp = FrontendTdpSnapshot.Unavailable;
        try { tdp = await CaptureTdpAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { AppLog.Warn("Device", "TDP snapshot capture failed.", exception, ("Reason", exception.GetType().Name)); }

        var powerMode = FrontendPowerModeSnapshot.Unavailable;
        try { powerMode = await CapturePowerModeAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { AppLog.Warn("Device", "Power Mode snapshot capture failed.", exception, ("Reason", exception.GetType().Name)); }

        return new FrontendDeviceQuickSettingsSnapshot(cpuBoost, tdp, powerMode);
    }

    public Task<FrontendCenterMStartupSnapshot> CaptureCenterMStartupAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        return Task.FromResult(_centerMStartup?.Capture() ?? FrontendCenterMStartupSnapshot.Unavailable);
    }

    public async Task<FrontendCenterMStartupMutationResult> RequestCenterMAuthorityTransitionAsync(bool centerMEnabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        if (_centerMAuthorityTransition is null)
            return new FrontendCenterMStartupMutationResult(FrontendCenterMStartupMutationOutcome.Unavailable,
                FrontendCenterMStartupSnapshot.Unavailable, "MSI Center M controller authority control is unavailable.");

        // Deliberately NO StateInvalidated broadcast (PR #430 review): the returned result already
        // carries the authoritative read-back snapshot, this feature has no QAM surface, and a
        // successful transition restarts Windows immediately anyway.
        return await _centerMAuthorityTransition.RequestAsync(centerMEnabled, cancellationToken).ConfigureAwait(false);
    }

    private static FrontendTdpSnapshot MapTdpSnapshot(TdpRuntimeSnapshot snapshot) => new(
        snapshot.Available,
        snapshot.PersistenceWritable,
        snapshot.Configuration is { } configuration ? new(configuration.Enabled,
            new(configuration.Ac.Pl1Watts, configuration.Ac.Pl2Watts),
            new(configuration.Dc.Pl1Watts, configuration.Dc.Pl2Watts)) : null,
        snapshot.Policy is { } policy ? new(policy.Pl1MinimumWatts, policy.Pl1MaximumWatts, policy.Pl2MinimumWatts, policy.Pl2MaximumWatts) : null);

}
