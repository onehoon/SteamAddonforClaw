using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal readonly record struct IntelGpuFrequencyRange(double Min, double Max);

internal sealed record IntelGpuMinimumClockNativeCapability(
    bool Available,
    string? UnavailableReason,
    string AdapterName,
    uint VendorId,
    uint DeviceId,
    bool CanControl,
    double HardwareMinMhz,
    double HardwareMaxMhz,
    IReadOnlyList<double> AvailableClocksMhz,
    uint? NativeResult = null);

internal sealed record IntelGpuMinimumClockCapability(
    bool Available,
    string? UnavailableReason,
    string AdapterName,
    uint VendorId,
    uint DeviceId,
    bool CanControl,
    double HardwareMinMhz,
    double HardwareMaxMhz,
    IReadOnlyList<double> AvailableClocksMhz,
    IReadOnlyList<double> SelectableClocksMhz,
    double? SelectableMinMhz,
    double? SelectableMaxMhz,
    double? RecommendedDefaultMhz);

internal sealed record IntelGpuMinimumClockOperationResult(
    bool Succeeded,
    bool Verified,
    string? FailureReason,
    double? RequestedMinMhz,
    IntelGpuFrequencyRange? PreWriteRange,
    IntelGpuFrequencyRange? ReadbackRange,
    uint? NativeSetResult,
    uint? NativeReadResult);

internal interface IIntelGpuMinimumClockControl : IDisposable
{
    IntelGpuMinimumClockNativeCapability Initialize();
    IntelGpuMinimumClockNativeCapability Reinitialize();
    IntelGpuFrequencyRange GetRange();
    uint SetRange(IntelGpuFrequencyRange range);
}

internal static class IntelGpuMinimumClockPolicy
{
    internal const double MinimumUsefulGamingClockMhz = 1500.0;
    internal const double FrequencyToleranceMhz = 0.1;

    internal static ClockSelection SelectAvailableClocks(IEnumerable<double>? clocks)
    {
        if (clocks is null)
            return ClockSelection.Unavailable("IGCL did not return an available-clock list.");

        var normalized = new List<double>();
        foreach (var value in clocks.Where(static value => double.IsFinite(value) && value > 0).OrderBy(static value => value))
        {
            if (normalized.Count == 0 || Math.Abs(value - normalized[^1]) > FrequencyToleranceMhz)
                normalized.Add(value);
        }

        if (normalized.Count < 3)
            return ClockSelection.Unavailable("IGCL returned fewer than three distinct valid GPU clocks.", normalized);

        var upper = normalized[^3];
        var lowerIndex = normalized.FindIndex(static value => value >= MinimumUsefulGamingClockMhz);
        if (lowerIndex < 0 || normalized[lowerIndex] > upper)
            return ClockSelection.Unavailable("No selectable GPU clock exists between the useful-gaming threshold and the dynamic upper bound.", normalized);

        var selectable = normalized
            .Where(value => value >= normalized[lowerIndex] && value <= upper)
            .ToArray();
        if (selectable.Length == 0)
            return ClockSelection.Unavailable("The available GPU clocks produced an empty selectable range.", normalized);

        return new(
            true,
            null,
            Array.AsReadOnly(normalized.ToArray()),
            Array.AsReadOnly(selectable),
            selectable[0],
            selectable[^1],
            selectable[^1]);
    }

    internal static bool IsHardwareRangeValid(double minimumMhz, double maximumMhz) =>
        double.IsFinite(minimumMhz)
        && double.IsFinite(maximumMhz)
        && maximumMhz > 0
        && minimumMhz <= maximumMhz;

    internal static bool TryGetCanonicalTarget(
        IEnumerable<double> selectableClocksMhz,
        double targetMhz,
        out double canonicalTargetMhz)
    {
        canonicalTargetMhz = default;
        if (!double.IsFinite(targetMhz)) return false;

        foreach (var selectable in selectableClocksMhz)
        {
            if (double.IsFinite(selectable) && Math.Abs(selectable - targetMhz) <= FrequencyToleranceMhz)
            {
                canonicalTargetMhz = selectable;
                return true;
            }
        }

        return false;
    }

    internal static bool TryCreateMinimumRequest(
        IEnumerable<double> selectableClocksMhz,
        double targetMhz,
        IntelGpuFrequencyRange current,
        out double canonicalTargetMhz,
        out IntelGpuFrequencyRange request,
        out string? failureReason)
    {
        canonicalTargetMhz = default;
        request = default;
        failureReason = null;

        if (!TryGetCanonicalTarget(selectableClocksMhz, targetMhz, out canonicalTargetMhz))
        {
            failureReason = "The requested minimum is not one of the currently selectable driver clocks.";
            return false;
        }
        if (!double.IsFinite(current.Min) || !double.IsFinite(current.Max))
        {
            failureReason = "The current IGCL range contains a non-finite limit.";
            return false;
        }
        if (current.Max >= 0 && current.Max < canonicalTargetMhz)
        {
            failureReason = "The requested minimum exceeds the current explicit maximum; the maximum was left unchanged.";
            return false;
        }

        request = new(canonicalTargetMhz, current.Max >= 0 ? current.Max : -1);
        return true;
    }

    internal static bool TryCreateFactoryMinimumReleaseRequest(
        IntelGpuFrequencyRange current,
        out IntelGpuFrequencyRange request)
    {
        request = default;
        if (!double.IsFinite(current.Min) || !double.IsFinite(current.Max))
            return false;

        request = new(-1, current.Max >= 0 ? current.Max : -1);
        return true;
    }

    internal static bool MatchesReadback(
        IntelGpuFrequencyRange request,
        IntelGpuFrequencyRange preWrite,
        IntelGpuFrequencyRange readback) =>
        MatchesRangeSide(request.Min, readback.Min)
        && MatchesRangeSide(preWrite.Max >= 0 ? preWrite.Max : -1, readback.Max);

    private static bool MatchesRangeSide(double requested, double actual) =>
        double.IsFinite(actual)
        && (requested < 0 ? actual < 0 : Math.Abs(requested - actual) <= FrequencyToleranceMhz);

    internal sealed record ClockSelection(
        bool Available,
        string? UnavailableReason,
        IReadOnlyList<double> AvailableClocksMhz,
        IReadOnlyList<double> SelectableClocksMhz,
        double? SelectableMinMhz,
        double? SelectableMaxMhz,
        double? RecommendedDefaultMhz)
    {
        internal static ClockSelection Unavailable(string reason, IReadOnlyList<double>? normalized = null) => new(
            false,
            reason,
            normalized ?? Array.Empty<double>(),
            Array.Empty<double>(),
            null,
            null,
            null);
    }
}

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

    internal bool BlocksDeveloperFrequencyMutation()
    {
        if (_profileStore is null || _mutationGate is null) return false;

        lock (_mutationGate.Sync)
        {
            var loaded = _profileStore.Load();
            return !loaded.CanSafelyReplace
                || loaded.Document.Games.Values.Any(profile => profile.Enabled && profile.Performance.GpuMinimumClock?.Enabled == true)
                || loaded.Document.XboxGames.Values.Any(profile => profile.Enabled && profile.Performance.GpuMinimumClock?.Enabled == true);
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

internal sealed class IntelGpuMinimumClockControl : IIntelGpuMinimumClockControl
{
    private const uint IntelVendorId = 0x8086;
    private const uint IntegratedAdapterFlag = 1;
    private const int DeviceTypeGraphics = 1;
    private const int FrequencyDomainGpu = 0;
    private const uint InitFlagUseLevelZero = 1;
    private const uint DeviceLostResult = IntelGpuMinimumClockRuntime.DeviceLostResult;
    private const uint DeviceUnavailableResult = IntelGpuMinimumClockRuntime.DeviceUnavailableResult;

    private readonly object _gate = new();
    private nint _library;
    private nint _api;
    private nint _adapter;
    private nint _frequencyDomain;
    private bool _initialized;
    private bool _disposed;
    private CtlClose? _close;
    private CtlGetRange? _getRange;
    private CtlSetRange? _setRange;
    private IntelGpuMinimumClockNativeCapability? _capability;

    public IntelGpuMinimumClockNativeCapability Initialize()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _initialized ? _capability! : InitializeCore();
        }
    }

    public IntelGpuMinimumClockNativeCapability Reinitialize()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ResetNativeResources();
            _initialized = false;
            _capability = null;
            return InitializeCore();
        }
    }

    public IntelGpuFrequencyRange GetRange()
    {
        lock (_gate)
        {
            EnsureUsable();
            var native = new FrequencyRange { Size = (uint)Marshal.SizeOf<FrequencyRange>(), Version = 0 };
            RequireSuccess("ctlFrequencyGetRange", _getRange!(_frequencyDomain, ref native));
            return new(native.Min, native.Max);
        }
    }

    public uint SetRange(IntelGpuFrequencyRange range)
    {
        lock (_gate)
        {
            EnsureUsable();
            var native = new FrequencyRange { Size = (uint)Marshal.SizeOf<FrequencyRange>(), Version = 0, Min = range.Min, Max = range.Max };
            return _setRange!(_frequencyDomain, ref native);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ResetNativeResources();
        }
    }

    internal static bool NativeAbiIsExpectedForTests() =>
        Marshal.SizeOf<ApplicationId>() == 16
        && Marshal.SizeOf<InitArgs>() == 36
        && Marshal.SizeOf<AdapterProperties>() == 320
        && Marshal.SizeOf<FrequencyProperties>() == 32
        && Marshal.OffsetOf<FrequencyProperties>(nameof(FrequencyProperties.Type)).ToInt32() == 8
        && Marshal.OffsetOf<FrequencyProperties>(nameof(FrequencyProperties.CanControl)).ToInt32() == 12
        && Marshal.OffsetOf<FrequencyProperties>(nameof(FrequencyProperties.Min)).ToInt32() == 16
        && Marshal.OffsetOf<FrequencyProperties>(nameof(FrequencyProperties.Max)).ToInt32() == 24
        && Marshal.SizeOf<FrequencyRange>() == 24
        && Marshal.OffsetOf<FrequencyRange>(nameof(FrequencyRange.Min)).ToInt32() == 8
        && Marshal.OffsetOf<FrequencyRange>(nameof(FrequencyRange.Max)).ToInt32() == 16
        && Marshal.SizeOf<FrequencyState>() == 56
        && Marshal.OffsetOf<FrequencyState>(nameof(FrequencyState.Request)).ToInt32() == 16
        && Marshal.OffsetOf<FrequencyState>(nameof(FrequencyState.Actual)).ToInt32() == 40
        && Marshal.OffsetOf<FrequencyState>(nameof(FrequencyState.ThrottleReasons)).ToInt32() == 48
        && AvailableClocksDelegateIsExpectedForTests();

    private IntelGpuMinimumClockNativeCapability InitializeCore()
    {
        try
        {
            _library = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "ControlLib.dll"));
            var init = Get<CtlInit>("ctlInit");
            _close = Get<CtlClose>("ctlClose");
            var enumerateDevices = Get<CtlEnumerateDevices>("ctlEnumerateDevices");
            var getDeviceProperties = Get<CtlGetDeviceProperties>("ctlGetDeviceProperties");
            var enumerateFrequencyDomains = Get<CtlEnumerateDomains>("ctlEnumFrequencyDomains");
            var getFrequencyProperties = Get<CtlGetFrequencyProperties>("ctlFrequencyGetProperties");
            var getAvailableClocks = Get<CtlGetAvailableClocks>("ctlFrequencyGetAvailableClocks");
            _getRange = Get<CtlGetRange>("ctlFrequencyGetRange");
            _setRange = Get<CtlSetRange>("ctlFrequencySetRange");

            var args = new InitArgs
            {
                Size = (uint)Marshal.SizeOf<InitArgs>(),
                Version = 0,
                AppVersion = 0x00010001,
                Flags = InitFlagUseLevelZero,
                ApplicationUid = new ApplicationId()
            };
            _ = RequireSuccess("ctlInit", init(ref args, out _api));
            SelectIntegratedIntelAdapter(enumerateDevices, getDeviceProperties);
            var frequencyProperties = SelectGpuFrequencyDomain(enumerateFrequencyDomains, getFrequencyProperties);
            var clocks = GetAvailableClocks(_frequencyDomain, getAvailableClocks);
            var name = DecodeAdapterName(_adapterProperties.Name);
            _capability = new(
                true,
                null,
                name,
                _adapterProperties.VendorId,
                _adapterProperties.PciDeviceId,
                frequencyProperties.CanControl,
                frequencyProperties.Min,
                frequencyProperties.Max,
                Array.AsReadOnly(clocks));
        }
        catch (Exception exception)
        {
            var result = exception is IgclMinimumClockException igcl ? igcl.Result : (uint?)null;
            ResetNativeResources();
            _capability = new(false, result is { } native ? $"{exception.Message} (IGCL=0x{native:X8})" : exception.Message,
                "", 0, 0, false, 0, 0, Array.Empty<double>(), result);
        }

        _initialized = true;
        return _capability!;
    }

    private void SelectIntegratedIntelAdapter(CtlEnumerateDevices enumerate, CtlGetDeviceProperties getProperties)
    {
        uint count = 0;
        RequireSuccess("ctlEnumerateDevices(count)", enumerate(_api, ref count, null));
        if (count == 0) throw new InvalidOperationException("IGCL returned no adapters.");
        var adapters = new nint[checked((int)count)];
        RequireSuccess("ctlEnumerateDevices", enumerate(_api, ref count, adapters));

        foreach (var adapter in adapters)
        {
            var properties = NewAdapterProperties();
            var result = getProperties(adapter, ref properties);
            if (result is DeviceLostResult or DeviceUnavailableResult)
                throw new IgclMinimumClockException("ctlGetDeviceProperties", result);
            if (result != 0) continue;
            if (properties.VendorId == IntelVendorId
                && properties.DeviceType == DeviceTypeGraphics
                && (properties.GraphicsAdapterProperties & IntegratedAdapterFlag) != 0)
            {
                _adapter = adapter;
                _adapterProperties = properties;
                return;
            }
        }

        throw new InvalidOperationException("No integrated Intel graphics adapter was identified by IGCL.");
    }

    private FrequencyProperties SelectGpuFrequencyDomain(CtlEnumerateDomains enumerate, CtlGetFrequencyProperties getProperties)
    {
        uint count = 0;
        RequireSuccess("ctlEnumFrequencyDomains(count)", enumerate(_adapter, ref count, null));
        if (count == 0) throw new InvalidOperationException("IGCL returned no frequency domains.");
        var domains = new nint[checked((int)count)];
        RequireSuccess("ctlEnumFrequencyDomains", enumerate(_adapter, ref count, domains));

        foreach (var domain in domains)
        {
            var properties = new FrequencyProperties { Size = (uint)Marshal.SizeOf<FrequencyProperties>(), Version = 0 };
            var result = getProperties(domain, ref properties);
            if (result is DeviceLostResult or DeviceUnavailableResult)
                throw new IgclMinimumClockException("ctlFrequencyGetProperties", result);
            if (result != 0 || properties.Type != FrequencyDomainGpu) continue;
            _frequencyDomain = domain;
            return properties;
        }

        throw new InvalidOperationException("IGCL did not expose a GPU frequency domain with readable properties.");
    }

    private static double[] GetAvailableClocks(nint frequencyDomain, CtlGetAvailableClocks getAvailableClocks)
    {
        uint count = 0;
        RequireSuccess("ctlFrequencyGetAvailableClocks(count)", getAvailableClocks(frequencyDomain, ref count, null));
        if (count == 0) return [];

        var clocks = new double[checked((int)count)];
        var returnedCount = count;
        RequireSuccess("ctlFrequencyGetAvailableClocks", getAvailableClocks(frequencyDomain, ref returnedCount, clocks));
        if (returnedCount > clocks.Length)
            throw new InvalidOperationException("IGCL returned more available clocks than the requested buffer can hold.");
        if (returnedCount == clocks.Length) return clocks;
        Array.Resize(ref clocks, checked((int)returnedCount));
        return clocks;
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized || _capability is not { Available: true } || _frequencyDomain == 0)
            throw new InvalidOperationException("The integrated Intel GPU frequency session is unavailable.");
    }

    private static AdapterProperties NewAdapterProperties() => new()
    {
        Size = (uint)Marshal.SizeOf<AdapterProperties>(),
        Version = 2,
        Name = new byte[100],
        Reserved = new byte[108]
    };

    private static string DecodeAdapterName(byte[]? bytes)
    {
        if (bytes is null) return "Intel GPU";
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0) length = bytes.Length;
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static T Get<T>(nint library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private T Get<T>(string name) where T : Delegate => Get<T>(_library, name);

    private static uint RequireSuccess(string operation, uint result)
    {
        if (result != 0) throw new IgclMinimumClockException(operation, result);
        return result;
    }

    private void ResetNativeResources()
    {
        if (_api != 0 && _close is not null)
        {
            try
            {
                var result = _close(_api);
                if (result is not (0 or 1))
                    AppLog.Debug("Profiles.IntelGpuMinimumClock", "ctlClose returned a non-success result.", ("NativeResult", $"0x{result:X8}"));
            }
            catch (Exception exception)
            {
                AppLog.Debug("Profiles.IntelGpuMinimumClock", "ctlClose threw while releasing the IGCL session.", ("Failure", exception.Message));
            }
        }
        _api = _adapter = _frequencyDomain = 0;
        _close = null;
        _getRange = null;
        _setRange = null;
        if (_library != 0)
        {
            try { NativeLibrary.Free(_library); } catch { }
            _library = 0;
        }
        _adapterProperties = default;
    }

    private static bool AvailableClocksDelegateIsExpectedForTests()
    {
        var convention = typeof(CtlGetAvailableClocks).GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
        var invoke = typeof(CtlGetAvailableClocks).GetMethod("Invoke")!;
        var parameters = invoke.GetParameters();
        return convention?.CallingConvention == CallingConvention.Cdecl
            && invoke.ReturnType == typeof(uint)
            && parameters.Length == 3
            && parameters[0].ParameterType == typeof(nint)
            && parameters[1].ParameterType == typeof(uint).MakeByRefType()
            && parameters[2].ParameterType == typeof(double[])
            && parameters[2].IsOut;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ApplicationId
    {
        public uint Data1; public ushort Data2; public ushort Data3;
        public byte Data4_0; public byte Data4_1; public byte Data4_2; public byte Data4_3;
        public byte Data4_4; public byte Data4_5; public byte Data4_6; public byte Data4_7;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InitArgs
    {
        public uint Size; public byte Version; public uint AppVersion; public uint Flags; public uint SupportedVersion; public ApplicationId ApplicationUid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdapterProperties
    {
        public uint Size; public byte Version; public nint DeviceIdPointer; public uint DeviceIdSize; public int DeviceType;
        public uint SupportedSubfunctionFlags; public ulong DriverVersion; public FirmwareVersion FirmwareVersion;
        public uint VendorId; public uint PciDeviceId; public uint RevisionId; public uint EusPerSubSlice;
        public uint SubSlicesPerSlice; public uint Slices;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100, ArraySubType = UnmanagedType.I1)] public byte[]? Name;
        public uint GraphicsAdapterProperties; public uint Frequency; public ushort PciSubsystemId; public ushort PciSubsystemVendorId;
        public AdapterBdf AdapterBdf; public uint XeCores;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 108)] public byte[]? Reserved;
    }

    [StructLayout(LayoutKind.Sequential)] private struct FirmwareVersion { public ulong Major; public ulong Minor; public ulong Build; }
    [StructLayout(LayoutKind.Sequential)] private struct AdapterBdf { public byte Bus; public byte Device; public byte Function; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrequencyProperties
    {
        public uint Size; public byte Version; public int Type;
        [MarshalAs(UnmanagedType.I1)] public bool CanControl;
        public double Min; public double Max;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrequencyRange { public uint Size; public byte Version; public double Min; public double Max; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrequencyState
    {
        public uint Size; public byte Version; public double Voltage; public double Request;
        public double Tdp; public double Efficient; public double Actual; public uint ThrottleReasons;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlInit(ref InitArgs args, out nint api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlClose(nint api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlEnumerateDevices(nint api, ref uint count, [Out] nint[]? devices);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetDeviceProperties(nint adapter, ref AdapterProperties properties);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlEnumerateDomains(nint adapter, ref uint count, [Out] nint[]? domains);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetFrequencyProperties(nint domain, ref FrequencyProperties properties);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetAvailableClocks(nint domain, ref uint count, [Out] double[]? clocks);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetRange(nint domain, ref FrequencyRange range);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlSetRange(nint domain, ref FrequencyRange range);

    private AdapterProperties _adapterProperties;
}

internal sealed class IgclMinimumClockException(string operation, uint result)
    : Exception($"{operation} failed: 0x{result:X8}.")
{
    internal uint Result { get; } = result;
}

internal sealed class UnavailableIntelGpuMinimumClockControl : IIntelGpuMinimumClockControl
{
    public IntelGpuMinimumClockNativeCapability Initialize() => Unavailable();
    public IntelGpuMinimumClockNativeCapability Reinitialize() => Unavailable();
    public IntelGpuFrequencyRange GetRange() => throw new InvalidOperationException("IGCL is unavailable in this test host.");
    public uint SetRange(IntelGpuFrequencyRange range) => 1;
    public void Dispose() { }

    private static IntelGpuMinimumClockNativeCapability Unavailable() => new(
        false, "IGCL is unavailable in this test host.", "", 0, 0, false, 0, 0, Array.Empty<double>());
}
