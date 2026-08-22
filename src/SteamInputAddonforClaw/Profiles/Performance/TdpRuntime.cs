using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal enum TdpPowerSource { AC, DC }

internal sealed record TdpApplyCompletion(TdpPowerSource Source, int Pl1Watts, int Pl2Watts, bool Attempted, bool Succeeded);
internal sealed record TdpApplySnapshot(long AuthorityVersion, long ReconcileVersion, TdpPowerSource Source, int Pl1Watts, int Pl2Watts, string Reason, TaskCompletionSource<TdpApplyCompletion>? Completion = null);

internal enum TdpCommitOutcome { Succeeded, InvalidTarget, PersistenceFailed, Unavailable }

internal readonly record struct TdpCommitResult(TdpCommitOutcome Outcome, string? FailureMessage, Task<TdpApplyCompletion>? Completion = null)
{
    public bool Succeeded => Outcome == TdpCommitOutcome.Succeeded;
}

internal sealed record TdpRuntimeSnapshot(bool Available, bool PersistenceWritable, DeviceTdpSettings? Configuration, MsiClawTdpPolicy? Policy)
{
    internal static readonly TdpRuntimeSnapshot Unavailable = new(false, false, null, null);
}

internal sealed class TdpRuntime : IAsyncDisposable
{
    private readonly ProfileStore _profileStore;
    private readonly ProfileMutationGate _mutationGate;
    private readonly HandheldDeviceModelId? _modelId;
    private readonly MsiClawTdpHardware _hardware;
    private readonly Func<TdpPowerSource?> _powerSource;
    private readonly Func<DeviceTdpSettings?> _centerMManualSeed;
    private Func<uint> _actualAppIdSource = static () => 0;
    private readonly Lock _sync = new();
    private Task _tail = Task.CompletedTask;
    private long _authorityVersion;
    private long _reconcileVersion;
    private bool _invalidateHardwareCacheBeforeNextApply;
    private readonly List<string> _pendingCacheInvalidationReasons = [];
    private TdpPowerSource? _lastAdmittedPowerSource;
    private bool _reconcileRequired;
    private bool _accepting = true;

    internal TdpRuntime(ProfileStore profileStore, ProfileMutationGate mutationGate, HandheldDeviceModelId? modelId,
        MsiClawTdpHardware hardware, Func<TdpPowerSource?>? powerSource = null, Func<DeviceTdpSettings?>? centerMManualSeed = null,
        Func<uint>? actualAppIdSource = null)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _mutationGate = mutationGate ?? throw new ArgumentNullException(nameof(mutationGate));
        _modelId = modelId;
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _powerSource = powerSource ?? WindowsTdpPowerSource.Read;
        _centerMManualSeed = centerMManualSeed ?? (() => _modelId is { } id ? WindowsTdpCenterMManualSettings.Read(id) : null);
        _actualAppIdSource = actualAppIdSource ?? (() => 0);
    }

    internal void SetActualAppIdSource(Func<uint> source) => _actualAppIdSource = source ?? throw new ArgumentNullException(nameof(source));

    internal TdpCommitResult SetEnabled(bool enabled)
    {
        DeviceTdpSettings? current;
        lock (_mutationGate.Sync)
        {
            var loaded = _profileStore.Load();
            if (!loaded.CanSafelyReplace)
                return new(TdpCommitOutcome.PersistenceFailed, "Profile state is not safe to replace.");
            current = loaded.Document.Device.Performance.Tdp;
        }

        if (current is not null)
            return CommitGlobalTdp(current with { Enabled = enabled });
        if (!enabled)
            return new(TdpCommitOutcome.Succeeded, null);

        var seed = _centerMManualSeed();
        if (seed is null)
            return new(TdpCommitOutcome.InvalidTarget, "Center M Manual TDP values could not be loaded.");

        AppLog.Debug("Profiles.Tdp", "TDP initial configuration seeded", ("Source", "CenterMManual"),
            ("AcPL1", seed.Ac.Pl1Watts), ("AcPL2", seed.Ac.Pl2Watts),
            ("DcPL1", seed.Dc.Pl1Watts), ("DcPL2", seed.Dc.Pl2Watts));
        return CommitGlobalTdp(seed with { Enabled = true });
    }

    internal void StartupReconcile()
    {
        var startup = CaptureSnapshot();
        var startupEnabled = startup.Configuration?.Enabled == true;
        AppLog.Debug("Profiles.Tdp", startupEnabled ? "TDP startup reconcile scheduled" : "TDP startup passive",
            ("Configured", startup.Configuration is not null), ("Enabled", startupEnabled));
        ReconcileCurrent(forceApply: true, invalidateHardwareCache: false, "Startup");
    }

    internal TdpRuntimeSnapshot CaptureSnapshot()
    {
        if (_modelId is not { } modelId || !MsiClawTdpPolicy.TryResolve(modelId, out var policy))
            return TdpRuntimeSnapshot.Unavailable;

        lock (_mutationGate.Sync)
        {
            var loaded = _profileStore.Load();
            if (!loaded.CanSafelyReplace)
                return new(true, false, null, policy);

            var configuration = loaded.Document.Device.Performance.Tdp;
            if (configuration is not null && (!policy.IsValid(configuration.Ac) || !policy.IsValid(configuration.Dc)))
            {
                AppLog.Warn("Profiles.Tdp", "Persisted TDP configuration is outside the current model ranges; frontend mutation is disabled.");
                return new(true, false, null, policy);
            }

            return new(true, true, configuration, policy);
        }
    }

    internal void ReconcileCurrent(bool forceApply, bool invalidateHardwareCache, string reason)
    {
        if (_modelId is null) return;

        lock (_mutationGate.Sync)
        {
            var loaded = _profileStore.Load();
            if (!loaded.CanSafelyReplace)
                return;

            var tdp = ResolveEffectiveTdp(loaded.Document, _actualAppIdSource());

            lock (_sync)
            {
                if (!_accepting) return;
                if (invalidateHardwareCache)
                    RequestCacheInvalidationUnderLock(reason);
                var source = _powerSource();
                if (source is not { } currentSource)
                {
                    MarkReconcileRequiredUnderLock();
                    AppLog.Warn("Profiles.Tdp", "Current power source is unknown; lifecycle reconcile was not queued.",
                        null, ("Reason", reason), ("Action", "Deferred"), ("Cause", "UnknownPowerSource"));
                    return;
                }

                var realPowerBoundary = _lastAdmittedPowerSource is { } previousSource
                    && previousSource != currentSource;
                if (!forceApply && !_reconcileRequired && !realPowerBoundary)
                    return;

                if (realPowerBoundary)
                    RequestCacheInvalidationUnderLock("PowerSourceBoundary");
                var effectiveInvalidation = _invalidateHardwareCacheBeforeNextApply;
                if (tdp is null)
                    return;

                AppLog.Debug("Profiles.Tdp", "TDP reconcile admitted", ("Reason", reason), ("Source", currentSource), ("PL1", (currentSource == TdpPowerSource.AC ? tdp.Ac : tdp.Dc).Pl1Watts), ("PL2", (currentSource == TdpPowerSource.AC ? tdp.Ac : tdp.Dc).Pl2Watts), ("Force", forceApply), ("Invalidate", effectiveInvalidation));
                EnqueueSnapshotUnderLock(currentSource, tdp, reason);
            }
        }
    }

    internal TdpCommitResult CommitGlobalTdp(DeviceTdpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_modelId is not { } modelId || !MsiClawTdpPolicy.TryResolve(modelId, out var policy)
            || !policy.IsValid(settings.Ac) || !policy.IsValid(settings.Dc))
            return new(TdpCommitOutcome.InvalidTarget, "TDP target is unsupported or outside the model ranges.");

        lock (_mutationGate.Sync)
        {
            var loaded = _profileStore.Load();
            if (!loaded.CanSafelyReplace)
                return new(TdpCommitOutcome.PersistenceFailed, "Profile state is not safe to replace.");

            var previous = loaded.Document.Device.Performance.Tdp;
            var wasEnabled = previous?.Enabled == true;
            var updated = loaded.Document with
            {
                Device = loaded.Document.Device with
                {
                    Performance = loaded.Document.Device.Performance with
                    {
                        Tdp = BuildPersistedTdp(previous, settings)
                    }
                }
            };

            lock (_sync)
            {
                if (!_accepting)
                    return new(TdpCommitOutcome.Unavailable, "TDP runtime is shutting down.");

                try { _profileStore.Save(updated); }
                catch (Exception exception)
                {
                    AppLog.Error("Profiles.Tdp", "Global TDP persistence failed; authority was not changed.", exception);
                    return new(TdpCommitOutcome.PersistenceFailed, exception.Message);
                }

                AppLog.Debug("Profiles.Tdp", "TDP configuration committed",
                    ("Enabled", settings.Enabled), ("AcPL1", settings.Ac.Pl1Watts), ("AcPL2", settings.Ac.Pl2Watts),
                    ("DcPL1", settings.Dc.Pl1Watts), ("DcPL2", settings.Dc.Pl2Watts));

                var effective = ResolveEffectiveTdp(updated, _actualAppIdSource());
                if (effective is null)
                {
                    _authorityVersion++;
                    _reconcileVersion++;
                    _reconcileRequired = false;
                    RequestCacheInvalidationUnderLock("TdpDisabled");
                    AppLog.Debug("Profiles.Tdp", "TDP authority revoked", ("Reason", "TdpDisabled"));
                    if (wasEnabled)
                        AppLog.Info("Profiles.Tdp", "TDP control disabled", ("Action", "StopManaging"), ("Restore", false));
                    return new(TdpCommitOutcome.Succeeded, null);
                }

                if (!wasEnabled)
                    AppLog.Info("Profiles.Tdp", "TDP control enabled", ("AcPL1", settings.Ac.Pl1Watts), ("AcPL2", settings.Ac.Pl2Watts), ("DcPL1", settings.Dc.Pl1Watts), ("DcPL2", settings.Dc.Pl2Watts));

                var source = _powerSource();
                if (source is not { } currentSource)
                {
                    MarkReconcileRequiredUnderLock();
                    AppLog.Warn("Profiles.Tdp", "Current power source is unknown; committed TDP apply was not queued.", null, ("Reason", "ConfigurationCommit"), ("Action", "Deferred"), ("Cause", "UnknownPowerSource"));
                    return new(TdpCommitOutcome.Succeeded, null);
                }
                var completion = new TaskCompletionSource<TdpApplyCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
                EnqueueSnapshotUnderLock(currentSource, effective, "ConfigurationCommit", completion);
                return new(TdpCommitOutcome.Succeeded, null, completion.Task);
            }
        }
    }

    internal void BeginShutdown()
    {
        AppLog.Debug("Profiles.Tdp", "TDP runtime stopping", ("Action", "CloseAdmission"));
        lock (_sync)
        {
            _accepting = false;
            _authorityVersion++;
        }
    }

    internal Task DrainAsync()
    {
        lock (_sync) return _tail;
    }

    public async ValueTask DisposeAsync()
    {
        BeginShutdown();
        await DrainAsync().ConfigureAwait(false);
    }

    private void EnqueueSnapshotUnderLock(TdpPowerSource currentSource, DeviceTdpSettings settings, string reason, TaskCompletionSource<TdpApplyCompletion>? completion = null)
    {
        var pair = currentSource == TdpPowerSource.AC ? settings.Ac : settings.Dc;
        var reconcileVersion = ++_reconcileVersion;
        _lastAdmittedPowerSource = currentSource;
        _reconcileRequired = true;
        AppLog.Debug("Profiles.Tdp", "TDP apply queued", ("Reason", reason), ("Source", currentSource), ("PL1", pair.Pl1Watts), ("PL2", pair.Pl2Watts), ("AuthorityVersion", _authorityVersion), ("ReconcileVersion", reconcileVersion));
        EnqueueSnapshotUnderLock(new TdpApplySnapshot(_authorityVersion, reconcileVersion, currentSource, pair.Pl1Watts, pair.Pl2Watts, reason, completion));
    }

    private void MarkReconcileRequiredUnderLock()
    {
        _reconcileVersion++;
        _reconcileRequired = true;
    }

    private void RequestCacheInvalidationUnderLock(string reason)
    {
        _invalidateHardwareCacheBeforeNextApply = true;
        if (!_pendingCacheInvalidationReasons.Contains(reason, StringComparer.Ordinal))
            _pendingCacheInvalidationReasons.Add(reason);
    }

    private void EnqueueSnapshotUnderLock(TdpApplySnapshot snapshot)
    {
        if (!_accepting) return;
        _tail = _tail.ContinueWith(_ => ExecuteAsync(snapshot), CancellationToken.None,
            TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }

    private static DeviceTdpSettings BuildPersistedTdp(DeviceTdpSettings? previous, DeviceTdpSettings requested)
    {
        if (previous is null) return requested;
        return previous with
        {
            Enabled = requested.Enabled,
            Ac = previous.Ac with { Pl1Watts = requested.Ac.Pl1Watts, Pl2Watts = requested.Ac.Pl2Watts },
            Dc = previous.Dc with { Pl1Watts = requested.Dc.Pl1Watts, Pl2Watts = requested.Dc.Pl2Watts }
        };
    }

    private DeviceTdpSettings? ResolveEffectiveTdp(ProfileDocument document, uint actualAppId)
    {
        if (actualAppId > 0 && document.Games.TryGetValue(actualAppId.ToString(), out var game)
            && game.Enabled && game.Performance.Tdp is { } gameTdp)
        {
            if (!MsiClawTdpPolicy.TryResolve(_modelId!.Value, out var policy)
                || !policy.IsValid(gameTdp.Ac) || !policy.IsValid(gameTdp.Dc))
            {
                AppLog.Warn("Profiles.Tdp", "Active Game TDP is outside the current model ranges; no target was queued.", null,
                    ("RunningAppID", actualAppId), ("Action", "Deferred"));
                return null;
            }

            return new DeviceTdpSettings { Enabled = true, Ac = gameTdp.Ac, Dc = gameTdp.Dc };
        }

        return document.Device.Performance.Tdp is { Enabled: true } deviceTdp ? deviceTdp : null;
    }

    private async Task ExecuteAsync(TdpApplySnapshot snapshot)
    {
        bool invalidateHardwareCache;
        string? cacheInvalidationReason;
        lock (_sync)
        {
            if (!_accepting || snapshot.AuthorityVersion != _authorityVersion)
            {
                AppLog.Debug("Profiles.Tdp", "Pending TDP apply skipped", ("Reason", "AuthorityRevoked"), ("Source", snapshot.Source), ("PL1", snapshot.Pl1Watts), ("PL2", snapshot.Pl2Watts));
                snapshot.Completion?.TrySetResult(new(snapshot.Source, snapshot.Pl1Watts, snapshot.Pl2Watts, false, false));
                return;
            }

            invalidateHardwareCache = _invalidateHardwareCacheBeforeNextApply;
            cacheInvalidationReason = invalidateHardwareCache
                ? string.Join('+', _pendingCacheInvalidationReasons)
                : null;
            _invalidateHardwareCacheBeforeNextApply = false;
            _pendingCacheInvalidationReasons.Clear();
        }

        try
        {
            if (_modelId is not { } modelId)
            {
                snapshot.Completion?.TrySetResult(new(snapshot.Source, snapshot.Pl1Watts, snapshot.Pl2Watts, false, false));
                return;
            }
            if (invalidateHardwareCache)
                _hardware.InvalidateCachedPowerLimits(cacheInvalidationReason ?? "Unknown");
            var result = _hardware.Apply(modelId, new() { Pl1Watts = snapshot.Pl1Watts, Pl2Watts = snapshot.Pl2Watts });
            lock (_sync)
            {
                if (snapshot.ReconcileVersion == _reconcileVersion)
                    _reconcileRequired = !result.Succeeded;
            }
            AppLog.Info("Profiles.Tdp", "Global TDP apply completed.",
                ("Reason", snapshot.Reason), ("Source", snapshot.Source), ("PL1", snapshot.Pl1Watts), ("PL2", snapshot.Pl2Watts),
                ("Succeeded", result.Succeeded), ("FailureStage", result.FailureStage),
                ("RecoveryAttempted", result.RecoveryAttempted), ("RecoverySucceeded", result.RecoverySucceeded));
            snapshot.Completion?.TrySetResult(new(snapshot.Source, snapshot.Pl1Watts, snapshot.Pl2Watts, true, result.Succeeded));
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (snapshot.ReconcileVersion == _reconcileVersion)
                    _reconcileRequired = true;
            }
            AppLog.Error("Profiles.Tdp", "Global TDP queue item failed; later items continue.", exception,
                ("Source", snapshot.Source), ("PL1", snapshot.Pl1Watts), ("PL2", snapshot.Pl2Watts));
            snapshot.Completion?.TrySetResult(new(snapshot.Source, snapshot.Pl1Watts, snapshot.Pl2Watts, true, false));
        }
        await Task.CompletedTask;
    }
}

internal static class WindowsTdpPowerSource
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    internal static TdpPowerSource? Read()
    {
        if (!GetSystemPowerStatus(out var status)) return null;
        return status.ACLineStatus switch { 1 => TdpPowerSource.AC, 0 => TdpPowerSource.DC, _ => null };
    }
}

internal static class WindowsTdpCenterMManualSettings
{
    private const string UserScenarioPath = @"SOFTWARE\WOW6432Node\MSI\MSI Center M\Component\User Scenario";

    internal static DeviceTdpSettings? Read(HandheldDeviceModelId modelId)
    {
        if (!MsiClawTdpPolicy.TryResolve(modelId, out var policy)) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(UserScenarioPath, writable: false);
            if (key is null) return null;
            var values = new[] { "ManualPL1AC", "ManualPL2AC", "ManualPL1DC", "ManualPL2DC" }
                .Select(name => ReadInt(key.GetValue(name))).ToArray();
            if (values.Any(value => value is null)) return null;
            var ac = new TdpPowerPair { Pl1Watts = values[0]!.Value, Pl2Watts = values[1]!.Value };
            var dc = new TdpPowerPair { Pl1Watts = values[2]!.Value, Pl2Watts = values[3]!.Value };
            return policy.IsValid(ac) && policy.IsValid(dc)
                ? new DeviceTdpSettings { Enabled = true, Ac = ac, Dc = dc }
                : null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    private static int? ReadInt(object? value) => value switch
    {
        int number => number,
        long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
        string text when int.TryParse(text, out var number) => number,
        _ => null
    };
}
