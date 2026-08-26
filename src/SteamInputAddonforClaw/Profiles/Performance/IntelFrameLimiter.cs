using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal enum FpsPowerSource { AC, DC }
internal static class WindowsFpsPowerSource
{
    [StructLayout(LayoutKind.Sequential)] private struct SYSTEM_POWER_STATUS { public byte ACLineStatus; public byte BatteryFlag; public byte BatteryLifePercent; public byte Reserved; public int BatteryLifeTime; public int BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    internal static FpsPowerSource? Read() => !GetSystemPowerStatus(out var s) ? null : s.ACLineStatus switch { 1 => FpsPowerSource.AC, 0 => FpsPowerSource.DC, _ => null };
}

internal readonly record struct IntelFpsCapability(int Minimum, int Maximum, int Step, int ValueType, short FeatureMiscSupport, bool PerAppSupport)
{
    private const short LiveChange = 1 << 4;
    internal bool SupportsLiveChange => (FeatureMiscSupport & LiveChange) != 0;
    internal bool SupportsAddonRange { get { if (ValueType != 2 || !SupportsLiveChange || Minimum > 40 || Maximum < 120 || Step <= 0) return false; for (var x = 40; x <= 120; x++) if ((x - Minimum) % Step != 0) return false; return true; } }
}

internal interface IIntelFrameLimiter : IDisposable
{
    void Initialize();
    bool Available { get; }
    string? UnavailableReason { get; }
    IntelFpsCapability? Capability { get; }
    IntelFpsApplyOutcome Enable(int fps, FpsPowerSource source, uint appId);
    bool Disable(FpsPowerSource? source, uint appId);
}

internal enum IntelFpsApplyOutcome
{
    Verified,
    SetFailed,
    SetSucceededButUnverified
}

internal sealed class IntelFrameLimiter : IIntelFrameLimiter
{
    private readonly NativeIgcl _native;
    internal IntelFrameLimiter(string? ownershipPath = null) => _native = new(ownershipPath ?? AddonDataPaths.IntelFpsLimitOwnershipPath);
    public void Initialize() => _native.Initialize();
    public bool Available => _native.Available;
    public string? UnavailableReason => _native.UnavailableReason;
    public IntelFpsCapability? Capability => _native.Capability;
    public IntelFpsApplyOutcome Enable(int fps, FpsPowerSource source, uint appId) => _native.Set(true, fps, source, appId);
    public bool Disable(FpsPowerSource? source, uint appId) => _native.Set(false, 60, source, appId) == IntelFpsApplyOutcome.Verified;
    public void Dispose() => _native.Dispose();
}

internal sealed class UnavailableIntelFrameLimiter : IIntelFrameLimiter
{
    public void Initialize() { }
    public bool Available => false;
    public string? UnavailableReason => "Intel IGCL is unavailable in this test host.";
    public IntelFpsCapability? Capability => null;
    public IntelFpsApplyOutcome Enable(int fps, FpsPowerSource source, uint appId) => IntelFpsApplyOutcome.SetFailed;
    public bool Disable(FpsPowerSource? source, uint appId) => false;
    public void Dispose() { }
}

internal sealed class IntelFrameLimiterRuntime : IDisposable
{
    internal const int DefaultFps = 60;
    private readonly ProfileStore _store; private readonly ProfileMutationGate _gate; private readonly IIntelFrameLimiter _limiter; private readonly Func<FpsPowerSource?> _power; private readonly string _marker;
    private Func<uint> _app = static () => 0; private bool _shutdown; private bool _ownsGlobalState;
    internal IntelFrameLimiterRuntime(ProfileStore store, ProfileMutationGate gate, IIntelFrameLimiter limiter, Func<FpsPowerSource?>? power = null, string? marker = null) { _store = store; _gate = gate; _limiter = limiter; _power = power ?? WindowsFpsPowerSource.Read; _marker = marker ?? AddonDataPaths.IntelFpsLimitOwnershipPath; }
    internal bool Available => _limiter.Available;
    internal string? UnavailableReason => _limiter.UnavailableReason;
    internal IntelFpsCapability? Capability => _limiter.Capability;
    internal bool HasPendingOwnership => _ownsGlobalState || File.Exists(_marker);
    internal void Initialize() => _limiter.Initialize();
    internal void SetActualAppIdSource(Func<uint> source) => _app = source;
    internal void StartupRecover() { if (!HasPendingOwnership) return; try { if (_limiter.Disable(_power(), 0)) { _ownsGlobalState = false; TryDeleteOwnershipMarker("StartupRecovery", 0); } else AppLog.Warn("Profiles.IntelFps", "Stale Intel FPS ownership cleanup failed; keeping marker."); } catch (Exception e) { AppLog.Error("Profiles.IntelFps", "Stale Intel FPS ownership cleanup failed.", e); } }
    internal void StartupReconcile(uint appId) => Reconcile(appId, "Startup");
    internal void Reconcile(uint appId, string reason = "Reconcile") { try { lock (_gate.Sync) { var loaded = _store.Load(); if (!loaded.CanSafelyReplace || (!_limiter.Available && !HasPendingOwnership)) return; ApplyPolicy(loaded.Document, appId, reason); } } catch (Exception e) { AppLog.Error("Profiles.IntelFps", "FPS reconcile failed.", e, ("RunningAppID", appId), ("Reason", reason)); } }
    internal bool ReconcileWithResult(uint appId) { try { lock (_gate.Sync) { var loaded = _store.Load(); if (!loaded.CanSafelyReplace) return false; return ApplyPolicy(loaded.Document, appId, "Mutation"); } } catch (Exception e) { AppLog.Error("Profiles.IntelFps", "FPS apply failed.", e, ("RunningAppID", appId)); return false; } }
    private bool ApplyPolicy(ProfileDocument doc, uint appId, string reason)
    {
        var target = appId > 0 && doc.Games.TryGetValue(appId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var game) && game.Enabled && game.Performance.FpsLimit is { Enabled: true } fps ? fps : null;
        if (target is null) return Release(appId, reason);
        var source = _power(); if (source is null) return FailClosedOwnedState(appId, reason, "UnknownPowerSource");
        var value = source == FpsPowerSource.AC ? target.AcFps : target.DcFps;
        if (value is < 40 or > 120) return FailClosedOwnedState(appId, reason, "InvalidTarget");
        var outcome = _limiter.Enable(value, source.Value, appId);
        if (outcome != IntelFpsApplyOutcome.Verified)
        {
            if (outcome == IntelFpsApplyOutcome.SetSucceededButUnverified)
                _ownsGlobalState = true;
            return FailClosedOwnedState(appId, reason, "EnableFailed", value);
        }
        _ownsGlobalState = true;
        try { Directory.CreateDirectory(Path.GetDirectoryName(_marker)!); File.WriteAllText(_marker, $"{{\"fps\":{value}}}"); return true; }
        catch (Exception e)
        {
            AppLog.Error("Profiles.IntelFps", "FPS ownership marker persistence failed; disabling immediately.", e);
            var disabled = _limiter.Disable(source, appId);
            if (disabled) _ownsGlobalState = false;
            if (!disabled) AppLog.Warn("Profiles.IntelFps", "Immediate disable after ownership marker failure also failed; retaining any ownership evidence.", null, ("RunningAppID", appId));
            return false;
        }
    }
    private bool FailClosedOwnedState(uint appId, string reason, string cause, int? recoveryFps = null)
    {
        if (!_ownsGlobalState && !File.Exists(_marker)) return false;
        var disabled = _limiter.Disable(_power(), appId);
        if (!disabled)
        {
            if (_ownsGlobalState && !File.Exists(_marker))
                TryPersistOwnershipMarker(recoveryFps ?? DefaultFps, reason, appId);
            AppLog.Warn("Profiles.IntelFps", "FPS fail-close disable failed; keeping ownership marker.", null, ("Reason", reason), ("Cause", cause), ("RunningAppID", appId));
        }
        else
        {
            _ownsGlobalState = false;
            TryDeleteOwnershipMarker(reason, appId);
        }
        return false;
    }
    private void TryPersistOwnershipMarker(int fps, string reason, uint appId)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_marker)!);
            File.WriteAllText(_marker, $"{{\"fps\":{fps}}}");
        }
        catch (Exception e)
        {
            AppLog.Error("Profiles.IntelFps", "FPS ownership marker persistence for recovery failed.", e, ("Reason", reason), ("RunningAppID", appId));
        }
    }
    private bool Release(uint appId, string reason)
    {
        if (!_ownsGlobalState && !File.Exists(_marker)) return true;
        if (!_limiter.Disable(_power(), appId)) return false;
        _ownsGlobalState = false;
        return TryDeleteOwnershipMarker(reason, appId);
    }
    private bool TryDeleteOwnershipMarker(string reason, uint appId)
    {
        try { File.Delete(_marker); return true; }
        catch (Exception e) { AppLog.Warn("Profiles.IntelFps", "FPS ownership marker deletion failed; keeping ownership evidence.", e, ("Reason", reason), ("RunningAppID", appId)); return false; }
    }
    internal void BeginShutdown() => _shutdown = true;
    public void Dispose() { if (_shutdown) { try { lock (_gate.Sync) Release(_app(), "Shutdown"); } catch (Exception e) { AppLog.Error("Profiles.IntelFps", "FPS shutdown cleanup failed.", e); } } _limiter.Dispose(); }
}

internal sealed class WindowsIntelFpsPowerNotificationSource : IDisposable
{
    private static readonly Guid AcDc = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548"); private readonly DeviceNotifyCallbackRoutine _callback; private nint _registration;
    internal event Action? Changed;
    internal WindowsIntelFpsPowerNotificationSource() => _callback = OnNotification;
    internal bool TryRegister() { var guid = AcDc; var p = new Parameters { Callback = Marshal.GetFunctionPointerForDelegate(_callback) }; var result = PowerSettingRegisterNotification(ref guid, 2, ref p, out _registration); return result == 0; }
    private uint OnNotification(nint context, uint type, nint setting) { if (type != 4 && type != 7 && type != 18) Changed?.Invoke(); return 0; }
    public void Dispose() { var h = Interlocked.Exchange(ref _registration, 0); if (h != 0) _ = PowerSettingUnregisterNotification(h); }
    [StructLayout(LayoutKind.Sequential)] private struct Parameters { public nint Callback; public nint Context; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint DeviceNotifyCallbackRoutine(nint context, uint type, nint setting);
    [DllImport("powrprof.dll")] private static extern uint PowerSettingRegisterNotification(ref Guid guid, uint flags, ref Parameters recipient, out nint handle);
    [DllImport("powrprof.dll")] private static extern uint PowerSettingUnregisterNotification(nint handle);
}

// Minimal ABI projection of the official Intel IGCL v298 header (reviewed upstream commit
// b6c462933502e13d1537dd5024949a51be30e63d). The driver supplies ControlLib.dll;
// this application never packages that binary and resolves it from System32 only.
internal sealed class NativeIgcl : IDisposable
{
    private const int FrameLimit = 2, Int32 = 2; private readonly string _marker; private nint _library, _api, _adapter, _cleanupAdapter; private IntelFpsCapability? _cleanupCapability; private bool _closed, _initialized;
    internal bool Available { get; private set; } internal string? UnavailableReason { get; private set; } internal IntelFpsCapability? Capability { get; private set; }
    private CtlInit _init = null!; private CtlClose _close = null!; private CtlEnumerate _enumerate = null!; private CtlCaps _caps = null!; private CtlGetSet _getSet = null!;
    internal NativeIgcl(string marker) => _marker = marker;
    private T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    internal void Initialize()
    {
        if (_initialized) return;
        try
        {
            _library = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "ControlLib.dll")); _init = Get<CtlInit>("ctlInit"); _close = Get<CtlClose>("ctlClose"); _enumerate = Get<CtlEnumerate>("ctlEnumerateDevices"); _caps = Get<CtlCaps>("ctlGetSupported3DCapabilities"); _getSet = Get<CtlGetSet>("ctlGetSet3DFeature");
            var args = new InitArgs { Size = (uint)Marshal.SizeOf<InitArgs>(), Version = 0, AppVersion = 0x00010001, Flags = 1, ApplicationUid = new ApplicationId() }; var result = _init(ref args, out _api); Log("ctlInit", result); if (result != 0) throw new InvalidOperationException($"ctlInit failed: 0x{result:X8}");
            uint count = 0; result = _enumerate(_api, ref count, null); Log("ctlEnumerateDevices", result); if (result != 0 || count == 0) throw new InvalidOperationException("No IGCL adapter."); var adapters = new nint[count]; result = _enumerate(_api, ref count, adapters); Log("ctlEnumerateDevices", result); if (result != 0) throw new InvalidOperationException($"ctlEnumerateDevices failed: 0x{result:X8}");
            foreach (var adapter in adapters) if (TryInspectAdapter(adapter)) { _adapter = adapter; Available = true; _initialized = true; return; }
            UnavailableReason = Capability is { SupportsLiveChange: false }
                ? "Intel FRAME_LIMIT does not support live changes for the active game."
                : "Intel FRAME_LIMIT is unavailable or cannot represent 40-120 FPS.";
            _initialized = true;
        }
        catch (Exception e)
        {
            ResetFailedInitializationAttempt();
            _initialized = false;
            Available = false;
            UnavailableReason = e.Message;
            AppLog.Warn("Profiles.IntelFps", "IGCL initialization failed; a later startup phase may retry.", e);
        }
    }
    private void ResetFailedInitializationAttempt()
    {
        if (_api != 0)
        {
            try { if (_close is not null) _ = _close(_api); } catch { }
            _api = 0;
        }
        if (_library != 0) { try { NativeLibrary.Free(_library); } catch { } _library = 0; }
        _adapter = _cleanupAdapter = 0;
        _cleanupCapability = Capability = null;
        _init = null!; _close = null!; _enumerate = null!; _caps = null!; _getSet = null!;
    }
    private bool TryInspectAdapter(nint adapter)
    {
        var caps = new FeatureCaps { Size = (uint)Marshal.SizeOf<FeatureCaps>(), Version = 0, NumSupportedFeatures = 0, Features = 0 };
        var result = _caps(adapter, ref caps); Log("ctlGetSupported3DCapabilities", result); if (result != 0 || caps.NumSupportedFeatures == 0) return false;
        var stride = Marshal.SizeOf<FeatureDetails>(); var bytes = checked(stride * (int)caps.NumSupportedFeatures); var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            for (var offset = 0; offset < bytes; offset += stride) Marshal.StructureToPtr(default(FeatureDetails), buffer + offset, false);
            caps.Features = buffer; result = _caps(adapter, ref caps); Log("ctlGetSupported3DCapabilities", result); if (result != 0) return false;
            for (var i = 0; i < caps.NumSupportedFeatures; i++)
            {
                var d = Marshal.PtrToStructure<FeatureDetails>(buffer + (int)i * stride);
                if (d.FeatureType != FrameLimit || d.ValueType != Int32) continue;
                var capability = new IntelFpsCapability(d.Value.IntType.Range.Min, d.Value.IntType.Range.Max, d.Value.IntType.Range.Step, d.ValueType, d.FeatureMiscSupport, d.PerAppSupport);
                Capability = capability;
                _cleanupAdapter = adapter;
                _cleanupCapability = capability;
                AppLog.Debug("Profiles.IntelFps", "Intel FRAME_LIMIT capability detected.", ("Minimum", capability.Minimum), ("Maximum", capability.Maximum), ("Step", capability.Step), ("ValueType", capability.ValueType), ("FeatureMiscSupport", capability.FeatureMiscSupport), ("PerAppSupport", capability.PerAppSupport));
                return capability.SupportsAddonRange;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    internal IntelFpsApplyOutcome Set(bool enable, int fps, FpsPowerSource? source, uint appId)
    {
        var adapter = enable ? _adapter : _cleanupAdapter;
        if (adapter == 0 || (enable && !Available)) return IntelFpsApplyOutcome.SetFailed;
        // Cleanup only needs a valid INT32 value. Enable=false is the actual off semantic,
        // so it remains possible after a driver update narrows the user-facing range.
        var nativeFps = enable ? fps : _cleanupCapability?.Minimum ?? fps;
        var setFeature = CreateFrameLimitSetFeature(enable, nativeFps);
        var setResult = _getSet(adapter, ref setFeature);
        var getFeature = CreateFrameLimitGetFeature();
        uint? getResult = setResult == 0 ? _getSet(adapter, ref getFeature) : null;
        var readbackEnabled = DecodeFrameLimitEnabled(getFeature.Value);
        var readbackFps = getFeature.Value.IntValue;
        var failureReason = FrameLimitVerificationFailureReason(enable, setResult, getResult, readbackEnabled, readbackFps, nativeFps);
        var verified = failureReason is null;
        LogFrameLimitVerification(enable, source, appId, nativeFps, setResult, getResult, readbackEnabled, readbackFps, verified, failureReason);
        return setResult != 0 ? IntelFpsApplyOutcome.SetFailed : verified ? IntelFpsApplyOutcome.Verified : IntelFpsApplyOutcome.SetSucceededButUnverified;
    }
    private static FeatureGetSet CreateFrameLimitSetFeature(bool enable, int fps) => new()
    {
        Size = (uint)Marshal.SizeOf<FeatureGetSet>(),
        Version = 0,
        FeatureType = FrameLimit,
        ApplicationName = 0,
        ApplicationNameLength = 0,
        Set = true,
        ValueType = Int32,
        Value = new Property { EnableBits = enable ? 1u : 0u, IntValue = fps }
    };
    private static FeatureGetSet CreateFrameLimitGetFeature() => new()
    {
        Size = (uint)Marshal.SizeOf<FeatureGetSet>(),
        Version = 0,
        FeatureType = FrameLimit,
        ApplicationName = 0,
        ApplicationNameLength = 0,
        Set = false,
        ValueType = Int32,
        Value = default
    };
    private static bool DecodeFrameLimitEnabled(Property value) => (value.EnableBits & 0x1u) != 0;
    private static string? FrameLimitVerificationFailureReason(bool enable, uint setResult, uint? getResult, bool readbackEnabled, int readbackFps, int requestedFps) =>
        setResult != 0 ? "SetFailed" : getResult is null || getResult.Value != 0 ? "ReadbackFailed" : enable && !readbackEnabled ? "ReadbackEnableMismatch" : enable && readbackFps != requestedFps ? "ReadbackFpsMismatch" : !enable && readbackEnabled ? "ReadbackEnableMismatch" : null;
    private static void LogFrameLimitVerification(bool enable, FpsPowerSource? source, uint appId, int requestedFps, uint setResult, uint? getResult, bool readbackEnabled, int readbackFps, bool verified, string? failureReason)
    {
        var fields = new (string Key, object? Value)[]
        {
            ("Operation", enable ? "Enable" : "Disable"), ("RunningAppID", appId), ("PowerSource", source),
            ("RequestedEnabled", enable), ("RequestedFps", requestedFps), ("SetResult", $"0x{setResult:X8}"),
            ("GetResult", getResult is uint result ? $"0x{result:X8}" : "NotCalled"), ("ReadbackEnabled", readbackEnabled), ("ReadbackFps", readbackFps),
            ("Verified", verified), ("FailureReason", failureReason)
        };
        if (verified) AppLog.Info("Profiles.IntelFps", enable ? "FRAME_LIMIT apply verified." : "FRAME_LIMIT disable verified.", fields);
        else AppLog.Warn("Profiles.IntelFps", $"FRAME_LIMIT {(enable ? "apply" : "disable")} verification failed.", null, fields);
    }
    internal static bool VerifyFrameLimitReadbackForTests(bool enable, int requestedFps, uint setResult, uint? getResult, bool readbackEnabled, int readbackFps) =>
        FrameLimitVerificationFailureReason(enable, setResult, getResult, readbackEnabled, readbackFps, requestedFps) is null;
    internal static byte[] EncodeFrameLimitGetPropertyBytesForTests() =>
        EncodeFrameLimitPropertyBytes(CreateFrameLimitGetFeature().Value);
    internal static bool DecodeFrameLimitEnabledForTests(uint enableBits) =>
        DecodeFrameLimitEnabled(new Property { EnableBits = enableBits });
    internal static byte[] EncodeFrameLimitPropertyBytesForTests(bool enable, int fps)
    {
        var property = new Property { EnableBits = enable ? 1u : 0u, IntValue = fps };
        return EncodeFrameLimitPropertyBytes(property);
    }
    internal static byte[] EncodeFrameLimitSetFeatureBytesForTests(bool enable, int fps) =>
        EncodeFrameLimitFeatureBytes(CreateFrameLimitSetFeature(enable, fps));
    private static byte[] EncodeFrameLimitFeatureBytes(FeatureGetSet feature)
    {
        var size = Marshal.SizeOf<FeatureGetSet>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(feature, buffer, false);
            var bytes = new byte[size];
            Marshal.Copy(buffer, bytes, 0, size);
            return bytes;
        }
        finally
        {
            Marshal.DestroyStructure<FeatureGetSet>(buffer);
            Marshal.FreeHGlobal(buffer);
        }
    }
    private static byte[] EncodeFrameLimitPropertyBytes(Property property) => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref property, 1)).ToArray();
    private static void Log(string operation, uint result, int? fps = null, FpsPowerSource? source = null, uint? appId = null) { if (result != 0) AppLog.Warn("Profiles.IntelFps", $"{operation} failed.", null, ("Operation", operation), ("Result", $"0x{result:X8}"), ("RequestedFps", fps), ("PowerSource", source), ("RunningAppID", appId)); }
    public void Dispose() { if (_closed) return; _closed = true; if (_api != 0) { var result = _close(_api); Log("ctlClose", result); _api = 0; } if (_library != 0) { NativeLibrary.Free(_library); _library = 0; } }
    [StructLayout(LayoutKind.Sequential)] private struct ApplicationId { public uint Data1; public ushort Data2; public ushort Data3; public byte Data4_0; public byte Data4_1; public byte Data4_2; public byte Data4_3; public byte Data4_4; public byte Data4_5; public byte Data4_6; public byte Data4_7; }
    [StructLayout(LayoutKind.Sequential)] private struct InitArgs { public uint Size; public byte Version; public uint AppVersion; public uint Flags; public uint SupportedVersion; public ApplicationId ApplicationUid; }
    [StructLayout(LayoutKind.Sequential)] private struct Range { public int Min; public int Max; public int Step; public int Default; }
    [StructLayout(LayoutKind.Sequential)] private struct IntInfo { [MarshalAs(UnmanagedType.I1)] public bool DefaultEnable; public Range Range; }
    [StructLayout(LayoutKind.Sequential)] private struct EnumInfo { public ulong SupportedTypes; public uint DefaultType; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropertyInfo { [FieldOffset(0)] public IntInfo IntType; [FieldOffset(0)] public EnumInfo EnumType; }
    [StructLayout(LayoutKind.Sequential)] private struct FeatureDetails { public int FeatureType; public int ValueType; public PropertyInfo Value; public int CustomSize; public nint Custom; [MarshalAs(UnmanagedType.I1)] public bool PerAppSupport; public long Conflicts; public short FeatureMiscSupport; public short Reserved; public short Reserved1; public short Reserved2; }
    [StructLayout(LayoutKind.Sequential)] private struct FeatureCaps { public uint Size; public byte Version; public uint NumSupportedFeatures; public nint Features; }
    [StructLayout(LayoutKind.Explicit, Size = 8)] private struct Property { [FieldOffset(0)] public uint EnableBits; [FieldOffset(4)] public int IntValue; }
    [StructLayout(LayoutKind.Sequential)] private struct FeatureGetSet { public uint Size; public byte Version; public int FeatureType; public nint ApplicationName; public sbyte ApplicationNameLength; [MarshalAs(UnmanagedType.I1)] public bool Set; public int ValueType; public Property Value; public int CustomSize; public nint Custom; }
    internal static bool AbiLayoutIsExpectedForTests() => Marshal.SizeOf<InitArgs>() == 36 && Marshal.SizeOf<PropertyInfo>() == 24 && Marshal.SizeOf<FeatureDetails>() == 72 && Marshal.SizeOf<FeatureCaps>() == 24 && Marshal.SizeOf<FeatureGetSet>() == 56 && Marshal.OffsetOf<FeatureDetails>(nameof(FeatureDetails.Value)) == 8 && Marshal.OffsetOf<FeatureDetails>(nameof(FeatureDetails.Custom)) == 40 && Marshal.OffsetOf<FeatureGetSet>(nameof(FeatureGetSet.Value)) == 32 && Marshal.OffsetOf<FeatureGetSet>(nameof(FeatureGetSet.Custom)) == 48;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlInit(ref InitArgs args, out nint api); [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlClose(nint api); [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlEnumerate(nint api, ref uint count, [Out] nint[]? devices); [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlCaps(nint adapter, ref FeatureCaps caps); [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetSet(nint adapter, ref FeatureGetSet feature);
}
