using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal sealed record IntelGpuMinimumClockOperationResult(
    bool Succeeded,
    bool Verified,
    string? FailureReason,
    double? RequestedMinMhz,
    IntelGpuFrequencyRange? PreWriteRange,
    IntelGpuFrequencyRange? ReadbackRange,
    uint? NativeSetResult,
    uint? NativeReadResult);

/// <summary>Production minimum-only Intel GPU frequency owner; startup discovery is read-only.</summary>
internal sealed class IntelGpuMinimumClockRuntime : IDisposable
{
    internal const uint DeviceLostResult = 0x40000003;
    internal const uint DeviceUnavailableResult = 0x40000027;

    private const string Category = "Profiles.IntelGpuMinimumClock";
    private readonly object _gate = new();
    private readonly IIntelGpuMinimumClockControl _control;
    private readonly ProfileStore? _profileStore;
    private readonly ProfileMutationGate? _mutationGate;
    private readonly Func<AcDcPowerSource?> _powerSource;
    private Func<ProfileDocument, ResolvedActiveProfile?> _activeProfileResolver = static _ => null;
    private IntelGpuMinimumClockCapability? _capability;
    private bool _initialized;
    private bool _shuttingDown;
    private bool _disposed;

    internal IntelGpuMinimumClockRuntime(IIntelGpuMinimumClockControl control)
        : this(null, null, control, static () => null)
    {
    }

    internal IntelGpuMinimumClockRuntime(
        ProfileStore? profileStore,
        ProfileMutationGate? mutationGate,
        IIntelGpuMinimumClockControl control,
        Func<AcDcPowerSource?> powerSource)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _profileStore = profileStore;
        _mutationGate = mutationGate;
        _powerSource = powerSource ?? throw new ArgumentNullException(nameof(powerSource));
    }

    internal IntelGpuMinimumClockCapability? Capability
    {
        get { lock (_gate) return _capability; }
    }

    internal void SetActiveProfileResolver(Func<ProfileDocument, ResolvedActiveProfile?> resolver) =>
        _activeProfileResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    internal double? ResolveSelectableClockIndex(int index)
    {
        lock (_gate)
        {
            var capability = _capability ?? InitializeReadOnly();
            return index >= 0 && index < capability.SelectableClocksMhz.Count
                ? capability.SelectableClocksMhz[index]
                : null;
        }
    }

    internal double? ResolveSelectableClockOrRecommendedDefault(double? savedMhz)
    {
        lock (_gate)
        {
            var capability = _capability ?? InitializeReadOnly();
            if (!capability.Available || capability.RecommendedDefaultMhz is not { } fallback) return null;
            return CanonicalOrDefault(capability.SelectableClocksMhz, savedMhz ?? double.NaN, fallback);
        }
    }

    internal IntelGpuMinimumClockCapability InitializeReadOnly()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized) return _capability!;

            try
            {
                _capability = BuildCapability(_control.Initialize());
            }
            catch (Exception exception)
            {
                _capability = UnavailableCapability(exception.Message, exception is IgclMinimumClockException igcl ? igcl.Result : null);
            }

            _initialized = true;
            LogCapability("RuntimeStartup");
            return _capability;
        }
    }

    internal void StartupReconcile()
    {
        try
        {
            InitializeReadOnly();
            _ = ReconcileEffective("Startup");
        }
        catch (Exception exception)
        {
            AppLog.Warn(Category, "Game minimum GPU clock startup reconcile failed; Addon Runtime remains available.", exception,
                ("Reason", exception.GetType().Name));
        }
    }

    internal IntelGpuMinimumClockCapability CaptureCapability()
    {
        lock (_gate) return _capability ?? InitializeReadOnly();
    }

    internal IntelGpuMinimumClockOperationResult ReconcileEffective(string reason, bool allowSessionReinitialize = false)
    {
        lock (_gate)
        {
            if (_disposed || _shuttingDown)
                return FailedOperation("RuntimeShuttingDown");

            var capability = _capability ?? InitializeReadOnly();
            var loaded = LoadProfile();
            var result = ReconcileLoadedEffective(loaded, reason);
            var nativeResult = result.NativeSetResult ?? result.NativeReadResult;
            if (!result.Succeeded && allowSessionReinitialize && nativeResult is (DeviceLostResult or DeviceUnavailableResult))
            {
                if (TryReinitializeSession(nativeResult.Value))
                {
                    loaded = LoadProfile();
                    result = ReconcileLoadedEffective(loaded, reason + "AfterSessionRecovery");
                }
                else
                {
                    result = FailedOperation("IGCL session reinitialization failed.");
                }
            }

            LogEffectiveReconcile(reason, loaded, result, _capability ?? capability);
            return result;
        }
    }

    internal IntelGpuMinimumClockOperationResult ReconcileAfterResume() =>
        ReconcileEffective("PowerResume", allowSessionReinitialize: true);

    internal IntelGpuMinimumClockOperationResult ReconcileGameRailMutation(bool editedAc, string reason)
    {
        lock (_gate)
        {
            if (_disposed || _shuttingDown)
                return FailedOperation("RuntimeShuttingDown");

            var loaded = LoadProfile();
            if (loaded.CanSafelyReplace
                && _activeProfileResolver(loaded.Document)?.Performance.GpuMinimumClock is { Enabled: true })
            {
                AcDcPowerSource? source;
                try { source = _powerSource(); }
                catch { source = null; }

                if (source is not null && (source == AcDcPowerSource.AC) != editedAc)
                    return NoOpResult();
            }

            return ReconcileEffective(reason);
        }
    }

    internal IntelGpuMinimumClockOperationResult PrepareForUninstall()
    {
        lock (_gate)
        {
            if (_disposed || _shuttingDown)
                return FailedOperation("RuntimeShuttingDown");

            var capability = _capability ?? InitializeReadOnly();
            if (!capability.Available)
            {
                try
                {
                    _capability = BuildCapability(_control.Reinitialize());
                    _initialized = true;
                    capability = _capability;
                    LogCapability("UninstallReinitialize");
                }
                catch (Exception exception)
                {
                    _capability = UnavailableCapability(exception.Message,
                        exception is IgclMinimumClockException igcl ? igcl.Result : null);
                    capability = _capability;
                }
            }

            var result = capability.Available
                ? ReleaseToFactoryMinimum("Uninstall")
                : FailedOperation(capability.UnavailableReason ?? "CapabilityUnavailable");
            AppLog.Info(Category, "Game minimum GPU clock uninstall factory release completed.",
                ("Outcome", result.Succeeded ? "Succeeded" : "Failed"),
                ("Failure", result.FailureReason));
            return result;
        }
    }

    internal bool TryReinitializeSession(uint nativeResult)
    {
        lock (_gate)
        {
            if (_disposed || _shuttingDown || nativeResult is not (DeviceLostResult or DeviceUnavailableResult))
                return false;

            try
            {
                _capability = BuildCapability(_control.Reinitialize());
                _initialized = true;
                LogCapability(nativeResult == DeviceLostResult ? "DeviceLostReinitialize" : "DeviceUnavailableReinitialize");
                return _capability.Available;
            }
            catch (Exception exception)
            {
                _capability = UnavailableCapability(exception.Message, exception is IgclMinimumClockException igcl ? igcl.Result : nativeResult);
                LogCapability("SessionReinitializeFailed");
                return false;
            }
        }
    }

    internal IntelGpuMinimumClockOperationResult ApplyMinimum(double targetMhz, string reason)
    {
        lock (_gate)
        {
            IntelGpuFrequencyRange? preWrite = null;
            IntelGpuFrequencyRange? readback = null;
            double? requested = null;
            uint? setResult = null;
            uint? readResult = null;

            try
            {
                if (_shuttingDown) return Complete(false, false, "RuntimeShuttingDown");
                var capability = _capability ?? throw new InvalidOperationException("Minimum GPU clock capability has not been initialized.");
                if (!capability.Available) return Complete(false, false, capability.UnavailableReason ?? "CapabilityUnavailable");

                if (!IntelGpuMinimumClockPolicy.TryGetCanonicalTarget(capability.SelectableClocksMhz, targetMhz, out var canonicalTarget))
                    return Complete(false, false, "InvalidMinimumTarget");
                requested = canonicalTarget;

                preWrite = _control.GetRange();
                if (!IntelGpuMinimumClockPolicy.TryCreateMinimumRequest(
                        capability.SelectableClocksMhz,
                        canonicalTarget,
                        preWrite.Value,
                        out canonicalTarget,
                        out var request,
                        out var requestFailure))
                    return Complete(false, false, requestFailure ?? "InvalidRange");
                requested = canonicalTarget;

                setResult = _control.SetRange(request);
                if (setResult != 0)
                    return Complete(false, false, "SetRangeFailed");

                readback = _control.GetRange();
                var verified = IntelGpuMinimumClockPolicy.MatchesReadback(request, preWrite.Value, readback.Value);
                return Complete(verified, verified, verified ? null : "ReadbackMismatch");
            }
            catch (IgclMinimumClockException exception)
            {
                readResult = exception.Result;
                return Complete(false, false, exception.Message);
            }
            catch (Exception exception)
            {
                return Complete(false, false, exception.Message);
            }

            IntelGpuMinimumClockOperationResult Complete(bool succeeded, bool verified, string? failure) =>
                CompleteAndLog("ApplyMinimum", reason, succeeded, verified, failure, requested, preWrite, readback, setResult, readResult);
        }
    }

    internal IntelGpuMinimumClockOperationResult ReleaseToFactoryMinimum(string reason)
    {
        lock (_gate)
        {
            IntelGpuFrequencyRange? preWrite = null;
            IntelGpuFrequencyRange? readback = null;
            double? requested = null;
            uint? setResult = null;
            uint? readResult = null;

            try
            {
                if (_shuttingDown) return Complete(false, false, "RuntimeShuttingDown");
                var capability = _capability ?? throw new InvalidOperationException("Minimum GPU clock capability has not been initialized.");
                if (!capability.Available) return Complete(false, false, capability.UnavailableReason ?? "CapabilityUnavailable");
                preWrite = _control.GetRange();
                if (!IntelGpuMinimumClockPolicy.TryCreateFactoryMinimumReleaseRequest(preWrite.Value, out var request))
                    return Complete(false, false, "InvalidRange");
                requested = request.Min;

                setResult = _control.SetRange(request);
                if (setResult != 0) return Complete(false, false, "SetRangeFailed");

                readback = _control.GetRange();
                var verified = IntelGpuMinimumClockPolicy.MatchesReadback(request, preWrite.Value, readback.Value);
                return Complete(verified, verified, verified ? null : "ReadbackMismatch");
            }
            catch (IgclMinimumClockException exception)
            {
                readResult = exception.Result;
                return Complete(false, false, exception.Message);
            }
            catch (Exception exception)
            {
                return Complete(false, false, exception.Message);
            }

            IntelGpuMinimumClockOperationResult Complete(bool succeeded, bool verified, string? failure) =>
                CompleteAndLog("ReleaseToFactoryMinimum", reason, succeeded, verified, failure, requested, preWrite, readback, setResult, readResult);
        }
    }

    internal void BeginShutdown()
    {
        lock (_gate) _shuttingDown = true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _shuttingDown = true;
            _control.Dispose();
        }
    }

    private ProfileLoadResult LoadProfile() => _profileStore is null
        ? new(new ProfileDocument(), ProfileLoadStatus.ReadFailure)
        : _profileStore.Load();

    private IntelGpuMinimumClockOperationResult ReconcileLoadedEffective(ProfileLoadResult loaded, string reason)
    {
        if (!loaded.CanSafelyReplace)
        {
            var release = ReleaseToFactoryMinimum(reason + "ProfileUnavailable");
            return FailedOperation(release.Succeeded
                ? "Profile state is not safe to replace."
                : $"Profile state is not safe to replace; factory minimum release failed: {release.FailureReason}");
        }

        var active = _activeProfileResolver(loaded.Document);
        var gameDesired = active?.Performance.GpuMinimumClock;
        if (gameDesired is not { Enabled: true })
            return ReleaseToFactoryMinimum(reason + "NoEnabledActiveGame");

        AcDcPowerSource? source;
        try { source = _powerSource(); }
        catch (Exception exception)
        {
            var release = ReleaseToFactoryMinimum(reason + "PowerSourceReadFailed");
            return FailedOperation(release.Succeeded
                ? $"PowerSourceReadFailed: {exception.Message}"
                : $"PowerSourceReadFailed: {exception.Message}; factory minimum release failed: {release.FailureReason}");
        }

        if (source is null)
        {
            var release = ReleaseToFactoryMinimum(reason + "PowerSourceUnknown");
            return release.Succeeded
                ? FailedOperation("PowerSourceUnknown")
                : FailedOperation($"PowerSourceUnknown; factory minimum release failed: {release.FailureReason}");
        }

        var target = source == AcDcPowerSource.AC ? gameDesired.AcMhz : gameDesired.DcMhz;
        var capability = _capability;
        if (capability is null || !capability.Available
            || !IntelGpuMinimumClockPolicy.TryGetCanonicalTarget(capability.SelectableClocksMhz, target, out var canonical))
        {
            var release = ReleaseToFactoryMinimum(reason + "UnsupportedGameTarget");
            return FailedOperation(release.Succeeded
                ? "SavedTargetUnsupportedByCurrentDriver"
                : $"SavedTargetUnsupportedByCurrentDriver; factory minimum release failed: {release.FailureReason}");
        }

        return ApplyMinimum(canonical, reason);
    }

    private IntelGpuMinimumClockOperationResult NoOpResult() =>
        new(true, true, null, null, null, null, null, null);

    private IntelGpuMinimumClockOperationResult FailedOperation(string failure) =>
        new(false, false, failure, null, null, null, null, null);

    private void LogEffectiveReconcile(
        string reason,
        ProfileLoadResult loaded,
        IntelGpuMinimumClockOperationResult result,
        IntelGpuMinimumClockCapability capability)
    {
        var active = loaded.CanSafelyReplace ? _activeProfileResolver(loaded.Document) : null;
        var gameDesired = active?.Performance.GpuMinimumClock;
        var gameOwns = gameDesired is { Enabled: true };
        AcDcPowerSource? source;
        try { source = _powerSource(); }
        catch { source = null; }
        AppLog.Info(Category, "Game minimum GPU clock reconcile completed.",
            ("Reason", reason),
            ("EffectiveSource", gameOwns ? active!.Value.TargetLabel : "Factory"),
            ("Enabled", gameOwns),
            ("PowerSource", source?.ToString() ?? "Unknown"),
            ("DesiredAcMhz", gameDesired?.AcMhz),
            ("DesiredDcMhz", gameDesired?.DcMhz),
            ("EffectiveTargetMhz", result.RequestedMinMhz),
            ("PersistenceWritable", loaded.CanSafelyReplace),
            ("CapabilityAvailable", capability.Available),
            ("Outcome", result.Succeeded ? "Succeeded" : "Failed"),
            ("Failure", result.FailureReason));
    }

    private static double CanonicalOrDefault(IReadOnlyList<double> clocks, double saved, double fallback) =>
        IntelGpuMinimumClockPolicy.TryGetCanonicalTarget(clocks, saved, out var canonical) ? canonical : fallback;

    private IntelGpuMinimumClockCapability BuildCapability(IntelGpuMinimumClockNativeCapability native)
    {
        var selection = IntelGpuMinimumClockPolicy.SelectAvailableClocks(native.AvailableClocksMhz);
        var hardwareRangeValid = IntelGpuMinimumClockPolicy.IsHardwareRangeValid(native.HardwareMinMhz, native.HardwareMaxMhz);
        var available = native.Available && native.CanControl && hardwareRangeValid && selection.Available;
        var reason = native.UnavailableReason
            ?? (!native.Available ? "IGCL frequency capability is unavailable." : null)
            ?? (!native.CanControl ? "The IGCL GPU frequency domain reports canControl=false." : null)
            ?? (!hardwareRangeValid ? "IGCL reported invalid GPU hardware frequency limits." : null)
            ?? selection.UnavailableReason;

        return new(
            available,
            available ? null : reason,
            native.AdapterName,
            native.VendorId,
            native.DeviceId,
            native.CanControl,
            native.HardwareMinMhz,
            native.HardwareMaxMhz,
            Array.AsReadOnly(native.AvailableClocksMhz.ToArray()),
            selection.SelectableClocksMhz,
            selection.SelectableMinMhz,
            selection.SelectableMaxMhz,
            selection.RecommendedDefaultMhz);
    }

    private IntelGpuMinimumClockCapability UnavailableCapability(string reason, uint? nativeResult) => new(
        false,
        nativeResult is { } result ? $"{reason} (IGCL=0x{result:X8})" : reason,
        "",
        0,
        0,
        false,
        0,
        0,
        Array.Empty<double>(),
        Array.Empty<double>(),
        null,
        null,
        null);

    private void LogCapability(string reason)
    {
        var capability = _capability!;
        AppLog.Debug(Category, "Minimum GPU clock capability initialized.",
            ("Reason", reason),
            ("Available", capability.Available),
            ("UnavailableReason", capability.UnavailableReason),
            ("AdapterName", capability.AdapterName),
            ("VendorId", $"0x{capability.VendorId:X4}"),
            ("DeviceId", $"0x{capability.DeviceId:X4}"),
            ("CanControl", capability.CanControl),
            ("HardwareMinMhz", capability.HardwareMinMhz),
            ("HardwareMaxMhz", capability.HardwareMaxMhz),
            ("AvailableClockCount", capability.AvailableClocksMhz.Count),
            ("AvailableClocksMhz", string.Join(',', capability.AvailableClocksMhz)),
            ("SelectableClockCount", capability.SelectableClocksMhz.Count),
            ("SelectableMinMhz", capability.SelectableMinMhz),
            ("SelectableMaxMhz", capability.SelectableMaxMhz),
            ("RecommendedDefaultMhz", capability.RecommendedDefaultMhz));
    }

    private IntelGpuMinimumClockOperationResult CompleteAndLog(
        string operation,
        string reason,
        bool succeeded,
        bool verified,
        string? failure,
        double? requested,
        IntelGpuFrequencyRange? preWrite,
        IntelGpuFrequencyRange? readback,
        uint? nativeSetResult,
        uint? nativeReadResult)
    {
        var fields = new (string Key, object? Value)[]
        {
            ("Operation", operation),
            ("Reason", reason),
            ("RequestedMinMhz", requested),
            ("PreWriteMinMhz", preWrite?.Min),
            ("PreWriteMaxMhz", preWrite?.Max),
            ("ReadbackMinMhz", readback?.Min),
            ("ReadbackMaxMhz", readback?.Max),
            ("Verified", verified),
            ("NativeResult", nativeSetResult is { } set ? $"0x{set:X8}" : nativeReadResult is { } read ? $"0x{read:X8}" : null),
            ("NativeSetResult", nativeSetResult is { } setResult ? $"0x{setResult:X8}" : null),
            ("NativeReadResult", nativeReadResult is { } readResult ? $"0x{readResult:X8}" : null),
            ("Failure", failure)
        };

        if (succeeded) AppLog.Info(Category, "Minimum GPU clock operation verified.", fields);
        else AppLog.Warn(Category, "Minimum GPU clock operation failed.", null, fields);
        return new(succeeded, verified, failure, requested, preWrite, readback, nativeSetResult, nativeReadResult);
    }
}
