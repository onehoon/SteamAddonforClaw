using System.Runtime.InteropServices;
using System.Text;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Diagnostics;

internal readonly record struct IntelGpuFrequencyRange(double Min, double Max);

internal readonly record struct IntelGpuPowerLimits(
    bool Pl1Enabled,
    int Pl1PowerMw,
    uint Pl1IntervalMs,
    bool Pl2Enabled,
    int Pl2PowerMw,
    int Pl4AcPowerMw,
    int Pl4DcPowerMw);

internal readonly record struct IntelGpuPowerCapability(bool CanControl, int DefaultLimitMw, int MinLimitMw, int MaxLimitMw);

internal static class IntelGpuIgclProbePolicy
{
    internal const double FrequencyToleranceMhz = 0.1;

    internal static bool TryCreateMaxMax(double hardwareMinMhz, double hardwareMaxMhz, out IntelGpuFrequencyRange range)
    {
        range = default;
        if (!double.IsFinite(hardwareMinMhz) || !double.IsFinite(hardwareMaxMhz)
            || hardwareMinMhz < 0 || hardwareMaxMhz <= 0 || hardwareMinMhz > hardwareMaxMhz)
            return false;
        range = new(hardwareMaxMhz, hardwareMaxMhz);
        return true;
    }

    internal static IntelGpuFrequencyRange CreateRestoreRequest(IntelGpuFrequencyRange original) =>
        new(original.Min >= 0 ? original.Min : -1, original.Max >= 0 ? original.Max : -1);

    internal static bool MatchesRequestedRange(IntelGpuFrequencyRange requested, IntelGpuFrequencyRange actual) =>
        MatchesExplicitSide(requested.Min, actual.Min) && MatchesExplicitSide(requested.Max, actual.Max);

    internal static bool MatchesRestoredRange(IntelGpuFrequencyRange original, IntelGpuFrequencyRange actual) =>
        (original.Min < 0 ? actual.Min < 0 : MatchesExplicitSide(original.Min, actual.Min))
        && (original.Max < 0 ? actual.Max < 0 : MatchesExplicitSide(original.Max, actual.Max));

    internal static bool IsPl1TargetValid(int valueMw, int minLimitMw, int maxLimitMw) =>
        minLimitMw >= 0 && maxLimitMw >= minLimitMw && valueMw >= minLimitMw && valueMw <= maxLimitMw;

    internal static IntelGpuPowerLimits WithPl1(IntelGpuPowerLimits original, int powerMw) =>
        original with { Pl1Enabled = true, Pl1PowerMw = powerMw };

    internal static bool MatchesPl1(IntelGpuPowerLimits actual, int requestedPowerMw) =>
        actual.Pl1Enabled && actual.Pl1PowerMw == requestedPowerMw;

    internal static bool MatchesPowerRestore(IntelGpuPowerLimits original, IntelGpuPowerLimits actual) =>
        original == actual;

    private static bool MatchesExplicitSide(double expected, double actual) =>
        double.IsFinite(actual) && Math.Abs(expected - actual) <= FrequencyToleranceMhz;
}

/// <summary>Developer-only IGCL frequency and power-limit write/readback probe.</summary>
internal sealed class IntelGpuIgclProbe : IDisposable
{
    private readonly object _gate = new();
    private readonly NativeIgclSession _native = new();
    private IntelGpuFrequencyRange? _originalFrequencyRange;
    private IntelGpuPowerLimits? _originalPowerLimits;
    private bool _frequencyModified;
    private bool _powerModified;
    private bool _disposed;
    private string? _lastFrequencyOperation;
    private bool? _lastFrequencyVerified;
    private uint? _lastFrequencyResult;
    private string? _lastFrequencyFailure;
    private string? _lastPowerOperation;
    private bool? _lastPowerVerified;
    private uint? _lastPowerResult;
    private string? _lastPowerFailure;

    internal FrontendIntelGpuFrequencyProbeSnapshot Capture()
    {
        lock (_gate)
        {
            if (_disposed) return FrontendIntelGpuFrequencyProbeSnapshot.Unavailable("The Runtime is shutting down.");
            return CaptureCore();
        }
    }

    internal FrontendIntelGpuFrequencyProbeSnapshot Run(FrontendIntelGpuFrequencyProbeOperation operation, int? testPl1Mw)
    {
        lock (_gate)
        {
            if (_disposed) return FrontendIntelGpuFrequencyProbeSnapshot.Unavailable("The Runtime is shutting down.");
            if (!Enum.IsDefined(operation)) return FrontendIntelGpuFrequencyProbeSnapshot.Unavailable("Unsupported Intel GPU probe operation.");
            var current = CaptureCore();
            switch (operation)
            {
                case FrontendIntelGpuFrequencyProbeOperation.SetMaxMax:
                    SetMaxMax(current);
                    break;
                case FrontendIntelGpuFrequencyProbeOperation.RestoreOriginalFrequency:
                    RestoreFrequency();
                    break;
                case FrontendIntelGpuFrequencyProbeOperation.SetTestPl1:
                    SetTestPl1(testPl1Mw);
                    break;
                case FrontendIntelGpuFrequencyProbeOperation.RestoreOriginalPower:
                    RestorePower();
                    break;
            }
            return CaptureCore();
        }
    }

    private FrontendIntelGpuFrequencyProbeSnapshot CaptureCore()
    {
        NativeCapture capture;
        try { capture = _native.Capture(); }
        catch (Exception exception)
        {
            AppLog.Warn("Diagnostics.IntelGpuFrequency", "IGCL probe capture failed.", exception);
            return FrontendIntelGpuFrequencyProbeSnapshot.Unavailable(exception.Message) with
            {
                LastOperation = _lastFrequencyOperation,
                LastOperationVerified = _lastFrequencyVerified,
                LastNativeResult = _lastFrequencyResult,
                OriginalRangeCaptured = _originalFrequencyRange is not null,
                ModifiedByProbe = _frequencyModified,
                LastPowerOperation = _lastPowerOperation,
                LastPowerOperationVerified = _lastPowerVerified,
                LastPowerNativeResult = _lastPowerResult,
                OriginalPowerLimitsCaptured = _originalPowerLimits is not null,
                PowerModifiedByProbe = _powerModified,
                PowerFailureMessage = _lastPowerFailure
            };
        }

        return new FrontendIntelGpuFrequencyProbeSnapshot(
            capture.FrequencyAvailable,
            capture.FrequencyCanControl,
            capture.AdapterName,
            capture.VendorId,
            capture.DeviceId,
            capture.HardwareMinMhz,
            capture.HardwareMaxMhz,
            capture.CurrentRange?.Min,
            capture.CurrentRange?.Max,
            capture.CurrentRange is { Min: >= 0 },
            capture.CurrentRange is { Max: >= 0 },
            capture.RequestMhz,
            capture.ActualMhz,
            capture.TdpMhz,
            capture.EfficientMhz,
            capture.Voltage,
            capture.ThrottleReasons,
            _originalFrequencyRange is not null,
            _frequencyModified,
            _lastFrequencyOperation,
            _lastFrequencyVerified,
            _lastFrequencyOperation is null ? capture.FrequencyResult : _lastFrequencyResult,
            Join(capture.FrequencyFailure, _lastFrequencyFailure),
            capture.PowerAvailable,
            capture.PowerCanControl,
            NonNegative(capture.PowerDefaultLimitMw),
            NonNegative(capture.PowerMinLimitMw),
            NonNegative(capture.PowerMaxLimitMw),
            capture.PowerLimits?.Pl1Enabled,
            capture.PowerLimits?.Pl1PowerMw,
            capture.PowerLimits is { } pl1 ? checked((int)pl1.Pl1IntervalMs) : null,
            capture.PowerLimits?.Pl2Enabled,
            capture.PowerLimits?.Pl2PowerMw,
            capture.PowerLimits?.Pl4AcPowerMw,
            capture.PowerLimits?.Pl4DcPowerMw,
            _originalPowerLimits is not null,
            _powerModified,
            _lastPowerOperation,
            _lastPowerVerified,
            _lastPowerOperation is null ? capture.PowerResult : _lastPowerResult,
            Join(capture.PowerFailure, _lastPowerFailure));
    }

    private void SetMaxMax(FrontendIntelGpuFrequencyProbeSnapshot current)
    {
        _lastFrequencyOperation = "Set Max / Max";
        _lastFrequencyVerified = false;
        _lastFrequencyResult = null;
        _lastFrequencyFailure = null;
        if (!current.Available || !current.CanControl
            || current.HardwareMinMhz is not { } min || current.HardwareMaxMhz is not { } max
            || !IntelGpuIgclProbePolicy.TryCreateMaxMax(min, max, out var requested))
        {
            _lastFrequencyFailure = "The Intel GPU frequency domain is unavailable or cannot be controlled.";
            AppLog.Warn("Diagnostics.IntelGpuFrequency", "Max/Max write refused by the reported capability.", null,
                ("CanControl", current.CanControl), ("HardwareMinMhz", current.HardwareMinMhz), ("HardwareMaxMhz", current.HardwareMaxMhz));
            return;
        }

        try
        {
            if (!_frequencyModified)
                _originalFrequencyRange = _native.GetFrequencyRange();
            var original = _originalFrequencyRange ?? throw new InvalidOperationException("The original GPU frequency range could not be captured.");
            var result = _native.SetFrequencyRange(requested);
            _lastFrequencyResult = result;
            if (result != 0)
            {
                _lastFrequencyFailure = $"ctlFrequencySetRange failed: 0x{result:X8}.";
                AppLog.Warn("Diagnostics.IntelGpuFrequency", "ctlFrequencySetRange failed.", null,
                    ("Operation", _lastFrequencyOperation), ("AdapterName", current.AdapterName), ("VendorId", current.VendorId),
                    ("DeviceId", current.DeviceId), ("CanControl", current.CanControl), ("OriginalMinMhz", original.Min),
                    ("OriginalMaxMhz", original.Max), ("RequestedMinMhz", requested.Min), ("RequestedMaxMhz", requested.Max),
                    ("NativeResult", $"0x{result:X8}"));
                return;
            }

            // Once IGCL accepted the write, retain ownership even if immediate readback fails.
            _frequencyModified = true;
            var readback = _native.GetFrequencyRange();
            _lastFrequencyVerified = IntelGpuIgclProbePolicy.MatchesRequestedRange(requested, readback);
            if (!_lastFrequencyVerified.Value)
                _lastFrequencyFailure = "The range write succeeded, but immediate GetRange readback materially differed.";
            // Keep GetRange as the authoritative write verification; capture the live state only as post-write evidence.
            LogFrequencyResult(CaptureCore(), original, requested, readback, result, _lastFrequencyVerified.Value, _lastFrequencyFailure);
        }
        catch (IgclProbeException exception)
        {
            _lastFrequencyResult = exception.Result;
            _lastFrequencyFailure = exception.Message;
            AppLog.Warn("Diagnostics.IntelGpuFrequency", "Max/Max write or readback failed.", exception,
                ("Operation", _lastFrequencyOperation), ("AdapterName", current.AdapterName), ("VendorId", current.VendorId),
                ("DeviceId", current.DeviceId), ("CanControl", current.CanControl), ("HardwareMinMhz", current.HardwareMinMhz),
                ("HardwareMaxMhz", current.HardwareMaxMhz), ("NativeResult", $"0x{exception.Result:X8}"));
        }
        catch (Exception exception)
        {
            _lastFrequencyFailure = exception.Message;
            AppLog.Warn("Diagnostics.IntelGpuFrequency", "Max/Max operation failed.", exception);
        }
    }

    private void RestoreFrequency()
    {
        _lastFrequencyOperation = "Restore Original Frequency";
        _lastFrequencyVerified = false;
        _lastFrequencyResult = null;
        _lastFrequencyFailure = null;
        if (_originalFrequencyRange is not { } original || !_frequencyModified)
        {
            _lastFrequencyFailure = "No probe-owned GPU frequency range is available to restore.";
            return;
        }
        try
        {
            var request = IntelGpuIgclProbePolicy.CreateRestoreRequest(original);
            var result = _native.SetFrequencyRange(request);
            _lastFrequencyResult = result;
            if (result != 0) throw new IgclProbeException("ctlFrequencySetRange restore", result);
            var readback = _native.GetFrequencyRange();
            _lastFrequencyVerified = IntelGpuIgclProbePolicy.MatchesRestoredRange(original, readback);
            if (_lastFrequencyVerified.Value)
            {
                _frequencyModified = false;
                _originalFrequencyRange = null;
            }
            else _lastFrequencyFailure = "Restore write succeeded, but GetRange did not confirm the original external-limit semantics.";
            LogFrequencyResult(CaptureCore(), original, request, readback, result, _lastFrequencyVerified.Value, _lastFrequencyFailure);
        }
        catch (IgclProbeException exception)
        {
            _lastFrequencyResult = exception.Result;
            _lastFrequencyFailure = exception.Message;
            AppLog.Warn("Diagnostics.IntelGpuFrequency", "Original GPU frequency restore failed.", exception,
                ("Operation", _lastFrequencyOperation), ("OriginalMinMhz", original.Min), ("OriginalMaxMhz", original.Max),
                ("NativeResult", $"0x{exception.Result:X8}"));
        }
        catch (Exception exception)
        {
            _lastFrequencyFailure = exception.Message;
            AppLog.Warn("Diagnostics.IntelGpuFrequency", "Original GPU frequency restore failed.", exception);
        }
    }

    private void SetTestPl1(int? testPl1Mw)
    {
        _lastPowerOperation = "Set Test PL1";
        _lastPowerVerified = false;
        _lastPowerResult = null;
        _lastPowerFailure = null;
        if (testPl1Mw is not { } requested)
        {
            _lastPowerFailure = "Enter an integer PL1 test value in milliwatts.";
            return;
        }
        try
        {
            var (properties, originalNow) = _native.GetPowerPropertiesAndLimits();
            if (!properties.CanControl)
            {
                _lastPowerFailure = "The IGCL power domain reports canControl=false.";
                AppLog.Warn("Diagnostics.IntelGpuFrequency", "PL1 write refused because the power domain cannot be controlled.", null,
                    ("CanControl", false), ("RequestedPl1Mw", requested));
                return;
            }
            if (!IntelGpuIgclProbePolicy.IsPl1TargetValid(requested, properties.MinLimitMw, properties.MaxLimitMw))
            {
                _lastPowerFailure = $"PL1 value must be within {properties.MinLimitMw}..{properties.MaxLimitMw} mW.";
                return;
            }
            if (!_powerModified) _originalPowerLimits = originalNow;
            var original = _originalPowerLimits ?? throw new InvalidOperationException("The original GPU power limits could not be captured.");
            var next = IntelGpuIgclProbePolicy.WithPl1(originalNow, requested);
            var result = _native.SetPowerLimits(next);
            _lastPowerResult = result;
            if (result != 0) throw new IgclProbeException("ctlPowerSetLimits", result);

            // A successful native write may have changed hardware even when verification fails.
            _powerModified = true;
            var readback = _native.GetPowerLimits();
            _lastPowerVerified = IntelGpuIgclProbePolicy.MatchesPl1(readback, requested);
            if (!_lastPowerVerified.Value) _lastPowerFailure = "PL1 write succeeded, but immediate GetLimits readback did not match.";
            LogPowerResult(original, next, readback, requested, result, _lastPowerVerified.Value, _lastPowerFailure);
        }
        catch (IgclProbeException exception)
        {
            _lastPowerResult = exception.Result;
            _lastPowerFailure = exception.Message;
            AppLog.Warn("Diagnostics.IntelGpuFrequency", "PL1 write or readback failed.", exception,
                ("Operation", _lastPowerOperation), ("RequestedPl1Mw", requested), ("NativeResult", $"0x{exception.Result:X8}"));
        }
        catch (Exception exception)
        {
            _lastPowerFailure = exception.Message;
            AppLog.Warn("Diagnostics.IntelGpuFrequency", "PL1 operation failed.", exception);
        }
    }

    private void RestorePower()
    {
        _lastPowerOperation = "Restore Original Power Limits";
        _lastPowerVerified = false;
        _lastPowerResult = null;
        _lastPowerFailure = null;
        if (_originalPowerLimits is not { } original || !_powerModified)
        {
            _lastPowerFailure = "No probe-owned GPU power limits are available to restore.";
            return;
        }
        try
        {
            var result = _native.SetPowerLimits(original);
            _lastPowerResult = result;
            if (result != 0) throw new IgclProbeException("ctlPowerSetLimits restore", result);
            var readback = _native.GetPowerLimits();
            _lastPowerVerified = IntelGpuIgclProbePolicy.MatchesPowerRestore(original, readback);
            if (_lastPowerVerified.Value)
            {
                _powerModified = false;
                _originalPowerLimits = null;
            }
            else _lastPowerFailure = "Restore write succeeded, but GetLimits did not confirm the original PL1/PL2/PL4 values.";
            LogPowerResult(original, original, readback, null, result, _lastPowerVerified.Value, _lastPowerFailure);
        }
        catch (IgclProbeException exception)
        {
            _lastPowerResult = exception.Result;
            _lastPowerFailure = exception.Message;
            AppLog.Warn("Diagnostics.IntelGpuFrequency", "Original GPU power-limit restore failed.", exception,
                ("Operation", _lastPowerOperation), ("NativeResult", $"0x{exception.Result:X8}"));
        }
        catch (Exception exception)
        {
            _lastPowerFailure = exception.Message;
            AppLog.Warn("Diagnostics.IntelGpuFrequency", "Original GPU power-limit restore failed.", exception);
        }
    }

    private static void LogFrequencyResult(
        FrontendIntelGpuFrequencyProbeSnapshot state,
        IntelGpuFrequencyRange original,
        IntelGpuFrequencyRange requested,
        IntelGpuFrequencyRange readback,
        uint nativeResult,
        bool verified,
        string? failure)
    {
        var fields = new (string Name, object? Value)[]
        {
            ("AdapterName", state.AdapterName), ("VendorId", state.VendorId), ("DeviceId", state.DeviceId),
            ("CanControl", state.CanControl), ("HardwareMinMhz", state.HardwareMinMhz), ("HardwareMaxMhz", state.HardwareMaxMhz),
            ("OriginalMinMhz", original.Min), ("OriginalMaxMhz", original.Max), ("RequestedMinMhz", requested.Min),
            ("RequestedMaxMhz", requested.Max), ("ReadbackMinMhz", readback.Min), ("ReadbackMaxMhz", readback.Max),
            ("Verified", verified), ("NativeResult", $"0x{nativeResult:X8}"), ("RequestMhz", state.RequestMhz),
            ("ActualMhz", state.ActualMhz), ("TdpMhz", state.TdpMhz), ("ThrottleReasons", state.ThrottleReasons is { } flags ? $"0x{flags:X8}" : "Unknown")
        };
        if (verified) AppLog.Info("Diagnostics.IntelGpuFrequency", "GPU frequency range operation completed.", fields);
        else AppLog.Warn("Diagnostics.IntelGpuFrequency", failure ?? "GPU frequency range operation was not verified.", null, fields);
    }

    private static void LogPowerResult(
        IntelGpuPowerLimits original,
        IntelGpuPowerLimits requested,
        IntelGpuPowerLimits readback,
        int? requestedPl1Mw,
        uint nativeResult,
        bool verified,
        string? failure)
    {
        var fields = new (string Name, object? Value)[]
        {
            ("OriginalPl1Enabled", original.Pl1Enabled), ("OriginalPl1Mw", original.Pl1PowerMw),
            ("OriginalTauMs", original.Pl1IntervalMs), ("RequestedPl1Mw", requestedPl1Mw),
            ("RequestedPl1Enabled", requested.Pl1Enabled), ("ReadbackPl1Enabled", readback.Pl1Enabled),
            ("ReadbackPl1Mw", readback.Pl1PowerMw), ("ReadbackTauMs", readback.Pl1IntervalMs),
            ("ReadbackPl2Enabled", readback.Pl2Enabled), ("ReadbackPl2Mw", readback.Pl2PowerMw),
            ("ReadbackPl4AcMw", readback.Pl4AcPowerMw), ("ReadbackPl4DcMw", readback.Pl4DcPowerMw),
            ("Verified", verified), ("NativeResult", $"0x{nativeResult:X8}")
        };
        if (verified) AppLog.Info("Diagnostics.IntelGpuFrequency", "GPU power-limit operation completed.", fields);
        else AppLog.Warn("Diagnostics.IntelGpuFrequency", failure ?? "GPU power-limit operation was not verified.", null, fields);
    }

    private static int? NonNegative(int? value) => value is >= 0 ? value : null;
    private static string? Join(string? first, string? second) => string.IsNullOrWhiteSpace(first) ? second : string.IsNullOrWhiteSpace(second) ? first : $"{first} {second}";

    internal static bool AbiLayoutIsExpectedForTests() => NativeIgclSession.AbiLayoutIsExpectedForTests();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_frequencyModified) RestoreFrequency();
            if (_powerModified) RestorePower();
            _disposed = true;
            _native.Dispose();
        }
    }

    private sealed record NativeCapture(
        bool FrequencyAvailable,
        bool FrequencyCanControl,
        string AdapterName,
        uint VendorId,
        uint DeviceId,
        double? HardwareMinMhz,
        double? HardwareMaxMhz,
        IntelGpuFrequencyRange? CurrentRange,
        double? RequestMhz,
        double? ActualMhz,
        double? TdpMhz,
        double? EfficientMhz,
        double? Voltage,
        uint? ThrottleReasons,
        uint? FrequencyResult,
        string? FrequencyFailure,
        bool PowerAvailable,
        bool PowerCanControl,
        int? PowerDefaultLimitMw,
        int? PowerMinLimitMw,
        int? PowerMaxLimitMw,
        IntelGpuPowerLimits? PowerLimits,
        uint? PowerResult,
        string? PowerFailure);

    private sealed class NativeIgclSession : IDisposable
    {
        private const uint IntelVendorId = 0x8086;
        private const uint IntegratedAdapterFlag = 1;
        private const int DeviceTypeGraphics = 1;
        private const int FrequencyDomainGpu = 0;
        private const uint InitFlagUseLevelZero = 1;
        private const uint SuccessStillOpenByAnotherCaller = 1;
        private nint _library;
        private nint _api;
        private nint _adapter;
        private nint _frequencyDomain;
        private nint _powerDomain;
        private bool _initialized;
        private bool _disposed;
        private AdapterProperties _adapterProperties;
        private FrequencyProperties _frequencyProperties;
        private string? _frequencyInitializationFailure;
        private uint? _frequencyInitializationResult;
        private string? _powerInitializationFailure;
        private uint? _powerInitializationResult;

        private CtlInit? _init;
        private CtlClose? _close;
        private CtlEnumerateDevices? _enumerateDevices;
        private CtlGetDeviceProperties? _getDeviceProperties;
        private CtlEnumDomains? _enumFrequencyDomains;
        private CtlGetFrequencyProperties? _getFrequencyProperties;
        private CtlGetFrequencyRange? _getFrequencyRange;
        private CtlSetFrequencyRange? _setFrequencyRange;
        private CtlGetFrequencyState? _getFrequencyState;
        private CtlEnumDomains? _enumPowerDomains;
        private CtlGetPowerProperties? _getPowerProperties;
        private CtlGetPowerLimits? _getPowerLimits;
        private CtlSetPowerLimits? _setPowerLimits;

        internal NativeCapture Capture()
        {
            EnsureInitialized();
            var currentRange = default(IntelGpuFrequencyRange?);
            double? request = null, actual = null, tdp = null, efficient = null, voltage = null;
            uint? throttle = null;
            uint? frequencyResult = _frequencyInitializationResult;
            string? frequencyFailure = _frequencyInitializationFailure;
            if (_frequencyDomain != 0)
            {
                try { currentRange = GetFrequencyRange(); frequencyFailure = null; frequencyResult = 0; }
                catch (IgclProbeException exception) { frequencyFailure = exception.Message; frequencyResult = exception.Result; }
                try
                {
                    var state = GetFrequencyState();
                    request = Known(state.Request);
                    actual = Known(state.Actual);
                    tdp = Known(state.Tdp);
                    efficient = Known(state.Efficient);
                    voltage = Known(state.Voltage);
                    throttle = state.ThrottleReasons;
                }
                catch (IgclProbeException exception)
                {
                    frequencyFailure = Join(frequencyFailure, exception.Message);
                    frequencyResult = exception.Result;
                }
            }

            var powerAvailable = false;
            var powerCanControl = false;
            int? defaultLimit = null, minLimit = null, maxLimit = null;
            IntelGpuPowerLimits? limits = null;
            uint? powerResult = _powerInitializationResult;
            string? powerFailure = _powerInitializationFailure;
            if (_powerDomain != 0)
            {
                try
                {
                    var powerCapture = GetPowerPropertiesAndLimits();
                    powerCanControl = powerCapture.Properties.CanControl;
                    defaultLimit = powerCapture.Properties.DefaultLimitMw;
                    minLimit = powerCapture.Properties.MinLimitMw;
                    maxLimit = powerCapture.Properties.MaxLimitMw;
                    limits = powerCapture.Limits;
                    powerAvailable = true;
                    powerFailure = null;
                    powerResult = 0;
                }
                catch (IgclProbeException exception)
                {
                    powerFailure = exception.Message;
                    powerResult = exception.Result;
                }
            }

            return new(
                _frequencyDomain != 0 && _frequencyInitializationFailure is null,
                _frequencyProperties.CanControl,
                AdapterName(_adapterProperties.Name),
                _adapterProperties.VendorId,
                _adapterProperties.PciDeviceId,
                Finite(_frequencyProperties.Min),
                Finite(_frequencyProperties.Max),
                currentRange,
                request, actual, tdp, efficient, voltage, throttle,
                frequencyResult, frequencyFailure,
                powerAvailable, powerCanControl, defaultLimit, minLimit, maxLimit, limits,
                powerResult, powerFailure);
        }

        internal IntelGpuFrequencyRange GetFrequencyRange()
        {
            if (_frequencyDomain == 0 || _getFrequencyRange is null) throw new InvalidOperationException("The GPU frequency domain is unavailable.");
            var range = new FrequencyRange { Size = (uint)Marshal.SizeOf<FrequencyRange>(), Version = 0 };
            var result = _getFrequencyRange(_frequencyDomain, ref range);
            RequireSuccess("ctlFrequencyGetRange", result);
            return new(range.Min, range.Max);
        }

        internal uint SetFrequencyRange(IntelGpuFrequencyRange value)
        {
            if (_frequencyDomain == 0 || _setFrequencyRange is null) throw new InvalidOperationException("The GPU frequency domain is unavailable.");
            var range = new FrequencyRange { Size = (uint)Marshal.SizeOf<FrequencyRange>(), Version = 0, Min = value.Min, Max = value.Max };
            return _setFrequencyRange(_frequencyDomain, ref range);
        }

        internal (IntelGpuPowerCapability Properties, IntelGpuPowerLimits Limits) GetPowerPropertiesAndLimits()
        {
            if (_powerDomain == 0 || _getPowerProperties is null || _getPowerLimits is null)
                throw new InvalidOperationException("The Intel GPU power domain is unavailable.");
            var properties = new PowerProperties { Size = (uint)Marshal.SizeOf<PowerProperties>(), Version = 0 };
            var propertiesResult = _getPowerProperties(_powerDomain, ref properties);
            RequireSuccess("ctlPowerGetProperties", propertiesResult);
            var limits = ReadPowerLimits();
            return (new(properties.CanControl, properties.DefaultLimit, properties.MinLimit, properties.MaxLimit), limits);
        }

        internal IntelGpuPowerLimits GetPowerLimits() => ReadPowerLimits();

        internal uint SetPowerLimits(IntelGpuPowerLimits value)
        {
            if (_powerDomain == 0 || _setPowerLimits is null) throw new InvalidOperationException("The Intel GPU power domain is unavailable.");
            var native = ToNative(value);
            return _setPowerLimits(_powerDomain, ref native);
        }

        private void EnsureInitialized()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized) return;
            try
            {
                _library = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "ControlLib.dll"));
                _init = Get<CtlInit>("ctlInit");
                _close = Get<CtlClose>("ctlClose");
                _enumerateDevices = Get<CtlEnumerateDevices>("ctlEnumerateDevices");
                _getDeviceProperties = Get<CtlGetDeviceProperties>("ctlGetDeviceProperties");
                _enumFrequencyDomains = Get<CtlEnumDomains>("ctlEnumFrequencyDomains");
                _getFrequencyProperties = Get<CtlGetFrequencyProperties>("ctlFrequencyGetProperties");
                _getFrequencyRange = Get<CtlGetFrequencyRange>("ctlFrequencyGetRange");
                _setFrequencyRange = Get<CtlSetFrequencyRange>("ctlFrequencySetRange");
                _getFrequencyState = Get<CtlGetFrequencyState>("ctlFrequencyGetState");
                _enumPowerDomains = Get<CtlEnumDomains>("ctlEnumPowerDomains");
                _getPowerProperties = Get<CtlGetPowerProperties>("ctlPowerGetProperties");
                _getPowerLimits = Get<CtlGetPowerLimits>("ctlPowerGetLimits");
                _setPowerLimits = Get<CtlSetPowerLimits>("ctlPowerSetLimits");

                var args = new InitArgs
                {
                    Size = (uint)Marshal.SizeOf<InitArgs>(),
                    Version = 0,
                    AppVersion = 0x00010001,
                    Flags = InitFlagUseLevelZero,
                    ApplicationUid = new ApplicationId()
                };
                var initResult = _init(ref args, out _api);
                RequireSuccess("ctlInit", initResult);
                SelectIntegratedIntelAdapter();
                InitializeFrequencyDomain();
                InitializePowerDomain();
                _initialized = true;
            }
            catch
            {
                ResetFailedInitialization();
                throw;
            }
        }

        private void SelectIntegratedIntelAdapter()
        {
            if (_enumerateDevices is null || _getDeviceProperties is null) throw new InvalidOperationException("IGCL adapter APIs are unavailable.");
            uint count = 0;
            RequireSuccess("ctlEnumerateDevices(count)", _enumerateDevices(_api, ref count, null));
            if (count == 0) throw new InvalidOperationException("IGCL returned no adapters.");
            var adapters = new nint[count];
            RequireSuccess("ctlEnumerateDevices", _enumerateDevices(_api, ref count, adapters));
            foreach (var adapter in adapters)
            {
                var properties = NewAdapterProperties();
                var result = _getDeviceProperties(adapter, ref properties);
                if (result != 0) continue;
                if (properties.VendorId == IntelVendorId && properties.DeviceType == DeviceTypeGraphics
                    && (properties.GraphicsAdapterProperties & IntegratedAdapterFlag) != 0)
                {
                    _adapter = adapter;
                    _adapterProperties = properties;
                    return;
                }
            }
            throw new InvalidOperationException("No integrated Intel graphics adapter was identified by IGCL.");
        }

        private void InitializeFrequencyDomain()
        {
            try
            {
                if (_enumFrequencyDomains is null || _getFrequencyProperties is null) throw new InvalidOperationException("IGCL frequency exports are unavailable.");
                var domains = EnumerateDomains(_enumFrequencyDomains, "ctlEnumFrequencyDomains");
                foreach (var domain in domains)
                {
                    var properties = new FrequencyProperties { Size = (uint)Marshal.SizeOf<FrequencyProperties>(), Version = 0 };
                    var result = _getFrequencyProperties(domain, ref properties);
                    if (result != 0) continue;
                    if (properties.Type != FrequencyDomainGpu) continue;
                    _frequencyDomain = domain;
                    _frequencyProperties = properties;
                    return;
                }
                _frequencyInitializationFailure = "IGCL did not expose a GPU frequency domain with readable properties.";
            }
            catch (IgclProbeException exception)
            {
                _frequencyInitializationFailure = exception.Message;
                _frequencyInitializationResult = exception.Result;
            }
            catch (Exception exception) { _frequencyInitializationFailure = exception.Message; }
            if (_frequencyDomain == 0)
                AppLog.Warn("Diagnostics.IntelGpuFrequency", "IGCL GPU frequency domain is unavailable.", null,
                    ("AdapterName", AdapterName(_adapterProperties.Name)), ("VendorId", _adapterProperties.VendorId),
                    ("DeviceId", _adapterProperties.PciDeviceId), ("Failure", _frequencyInitializationFailure),
                    ("NativeResult", _frequencyInitializationResult is { } result ? $"0x{result:X8}" : null));
        }

        private void InitializePowerDomain()
        {
            try
            {
                if (_enumPowerDomains is null || _getPowerProperties is null || _getPowerLimits is null)
                    throw new InvalidOperationException("IGCL power exports are unavailable.");
                var domains = EnumerateDomains(_enumPowerDomains, "ctlEnumPowerDomains");
                foreach (var domain in domains)
                {
                    _powerDomain = domain;
                    try
                    {
                        _ = GetPowerPropertiesAndLimits();
                        return;
                    }
                    catch (IgclProbeException) { _powerDomain = 0; }
                }
                _powerInitializationFailure = "IGCL returned no power domain with readable properties and limits.";
            }
            catch (IgclProbeException exception)
            {
                _powerInitializationFailure = exception.Message;
                _powerInitializationResult = exception.Result;
            }
            catch (Exception exception) { _powerInitializationFailure = exception.Message; }
            if (_powerDomain == 0)
                AppLog.Warn("Diagnostics.IntelGpuFrequency", "IGCL GPU power domain is unavailable.", null,
                    ("AdapterName", AdapterName(_adapterProperties.Name)), ("VendorId", _adapterProperties.VendorId),
                    ("DeviceId", _adapterProperties.PciDeviceId), ("Failure", _powerInitializationFailure),
                    ("NativeResult", _powerInitializationResult is { } result ? $"0x{result:X8}" : null));
        }

        private nint[] EnumerateDomains(CtlEnumDomains enumerate, string operation)
        {
            uint count = 0;
            RequireSuccess($"{operation}(count)", enumerate(_adapter, ref count, null));
            if (count == 0) return [];
            var domains = new nint[count];
            RequireSuccess(operation, enumerate(_adapter, ref count, domains));
            return domains;
        }

        private FrequencyState GetFrequencyState()
        {
            if (_frequencyDomain == 0 || _getFrequencyState is null) throw new InvalidOperationException("The GPU frequency state is unavailable.");
            var state = new FrequencyState { Size = (uint)Marshal.SizeOf<FrequencyState>(), Version = 0 };
            var result = _getFrequencyState(_frequencyDomain, ref state);
            RequireSuccess("ctlFrequencyGetState", result);
            return state;
        }

        private IntelGpuPowerLimits ReadPowerLimits()
        {
            if (_powerDomain == 0 || _getPowerLimits is null) throw new InvalidOperationException("The Intel GPU power domain is unavailable.");
            var limits = new PowerLimits { Size = (uint)Marshal.SizeOf<PowerLimits>(), Version = 0 };
            var result = _getPowerLimits(_powerDomain, ref limits);
            RequireSuccess("ctlPowerGetLimits", result);
            return FromNative(limits);
        }

        private static PowerLimits ToNative(IntelGpuPowerLimits value) => new()
        {
            Size = (uint)Marshal.SizeOf<PowerLimits>(),
            Version = 0,
            Sustained = new() { Enabled = value.Pl1Enabled, Power = value.Pl1PowerMw, Interval = value.Pl1IntervalMs },
            Burst = new() { Enabled = value.Pl2Enabled, Power = value.Pl2PowerMw },
            Peak = new() { PowerAc = value.Pl4AcPowerMw, PowerDc = value.Pl4DcPowerMw }
        };

        private static IntelGpuPowerLimits FromNative(PowerLimits value) => new(
            value.Sustained.Enabled, value.Sustained.Power, value.Sustained.Interval,
            value.Burst.Enabled, value.Burst.Power, value.Peak.PowerAc, value.Peak.PowerDc);

        private static AdapterProperties NewAdapterProperties() => new()
        {
            Size = (uint)Marshal.SizeOf<AdapterProperties>(),
            Version = 2,
            Name = new byte[100],
            Reserved = new byte[108]
        };

        private static string AdapterName(byte[]? bytes)
        {
            if (bytes is null) return "Intel GPU";
            var length = Array.IndexOf(bytes, (byte)0);
            if (length < 0) length = bytes.Length;
            return Encoding.UTF8.GetString(bytes, 0, length);
        }

        private static double? Known(double value) => double.IsFinite(value) && value >= 0 ? value : null;
        private static double? Finite(double value) => double.IsFinite(value) ? value : null;

        private T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

        private static void RequireSuccess(string operation, uint result)
        {
            if (result != 0) throw new IgclProbeException(operation, result);
        }

        private void ResetFailedInitialization()
        {
            if (_api != 0 && _close is not null)
            {
                try { _ = _close(_api); } catch { }
                _api = 0;
            }
            if (_library != 0) { try { NativeLibrary.Free(_library); } catch { } _library = 0; }
            _adapter = _frequencyDomain = _powerDomain = 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_api != 0 && _close is not null)
            {
                try
                {
                    var result = _close(_api);
                    if (result is not (0 or SuccessStillOpenByAnotherCaller))
                        AppLog.Warn("Diagnostics.IntelGpuFrequency", "ctlClose failed.", null, ("NativeResult", $"0x{result:X8}"));
                }
                catch (Exception exception) { AppLog.Warn("Diagnostics.IntelGpuFrequency", "ctlClose failed.", exception); }
                _api = 0;
            }
            if (_library != 0) { NativeLibrary.Free(_library); _library = 0; }
        }

        internal static bool AbiLayoutIsExpectedForTests() =>
            Marshal.SizeOf<FrequencyProperties>() == 32
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
            && Marshal.SizeOf<PowerProperties>() == 20
            && Marshal.OffsetOf<PowerProperties>(nameof(PowerProperties.CanControl)).ToInt32() == 5
            && Marshal.OffsetOf<PowerProperties>(nameof(PowerProperties.DefaultLimit)).ToInt32() == 8
            && Marshal.SizeOf<PowerLimits>() == 36
            && Marshal.OffsetOf<PowerLimits>(nameof(PowerLimits.Sustained)).ToInt32() == 8
            && Marshal.OffsetOf<PowerLimits>(nameof(PowerLimits.Burst)).ToInt32() == 20
            && Marshal.OffsetOf<PowerLimits>(nameof(PowerLimits.Peak)).ToInt32() == 28
            && Marshal.SizeOf<AdapterProperties>() == 320;

        [StructLayout(LayoutKind.Sequential)]
        private struct ApplicationId { public uint Data1; public ushort Data2; public ushort Data3; public byte Data4_0; public byte Data4_1; public byte Data4_2; public byte Data4_3; public byte Data4_4; public byte Data4_5; public byte Data4_6; public byte Data4_7; }
        [StructLayout(LayoutKind.Sequential)]
        private struct InitArgs { public uint Size; public byte Version; public uint AppVersion; public uint Flags; public uint SupportedVersion; public ApplicationId ApplicationUid; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct AdapterProperties
        {
            public uint Size; public byte Version; public nint DeviceIdPointer; public uint DeviceIdSize; public int DeviceType; public uint SupportedSubfunctionFlags;
            public ulong DriverVersion; public FirmwareVersion FirmwareVersion; public uint VendorId; public uint PciDeviceId; public uint RevisionId;
            public uint EusPerSubSlice; public uint SubSlicesPerSlice; public uint Slices;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100, ArraySubType = UnmanagedType.I1)] public byte[]? Name;
            public uint GraphicsAdapterProperties; public uint Frequency; public ushort PciSubsystemId; public ushort PciSubsystemVendorId; public AdapterBdf AdapterBdf;
            public uint XeCores;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 108)] public byte[]? Reserved;
        }
        [StructLayout(LayoutKind.Sequential)] private struct FirmwareVersion { public ulong Major; public ulong Minor; public ulong Build; }
        [StructLayout(LayoutKind.Sequential)] private struct AdapterBdf { public byte Bus; public byte Device; public byte Function; }
        [StructLayout(LayoutKind.Sequential)]
        private struct FrequencyProperties { public uint Size; public byte Version; public int Type; [MarshalAs(UnmanagedType.I1)] public bool CanControl; public double Min; public double Max; }
        [StructLayout(LayoutKind.Sequential)]
        private struct FrequencyRange { public uint Size; public byte Version; public double Min; public double Max; }
        [StructLayout(LayoutKind.Sequential)]
        private struct FrequencyState { public uint Size; public byte Version; public double Voltage; public double Request; public double Tdp; public double Efficient; public double Actual; public uint ThrottleReasons; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerProperties { public uint Size; public byte Version; [MarshalAs(UnmanagedType.I1)] public bool CanControl; public int DefaultLimit; public int MinLimit; public int MaxLimit; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerSustainedLimit { [MarshalAs(UnmanagedType.I1)] public bool Enabled; public int Power; public uint Interval; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerBurstLimit { [MarshalAs(UnmanagedType.I1)] public bool Enabled; public int Power; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerPeakLimit { public int PowerAc; public int PowerDc; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerLimits { public uint Size; public byte Version; public PowerSustainedLimit Sustained; public PowerBurstLimit Burst; public PowerPeakLimit Peak; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlInit(ref InitArgs args, out nint api);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlClose(nint api);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlEnumerateDevices(nint api, ref uint count, [Out] nint[]? adapters);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetDeviceProperties(nint adapter, ref AdapterProperties properties);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlEnumDomains(nint adapter, ref uint count, [Out] nint[]? domains);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetFrequencyProperties(nint domain, ref FrequencyProperties properties);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetFrequencyRange(nint domain, ref FrequencyRange range);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlSetFrequencyRange(nint domain, ref FrequencyRange range);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetFrequencyState(nint domain, ref FrequencyState state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetPowerProperties(nint domain, ref PowerProperties properties);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetPowerLimits(nint domain, ref PowerLimits limits);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlSetPowerLimits(nint domain, ref PowerLimits limits);
    }
}

internal sealed class IgclProbeException(string operation, uint result)
    : Exception($"{operation} failed: 0x{result:X8}.")
{
    internal uint Result { get; } = result;
}
