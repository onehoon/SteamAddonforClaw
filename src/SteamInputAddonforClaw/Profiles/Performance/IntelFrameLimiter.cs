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
    internal bool SupportsAddonRange { get { if (ValueType != 2 || Minimum > 40 || Maximum < 120 || Step <= 0) return false; for (var x = 40; x <= 120; x++) if ((x - Minimum) % Step != 0) return false; return true; } }
}

internal interface IIntelFrameLimiter : IDisposable
{
    bool Available { get; }
    string? UnavailableReason { get; }
    IntelFpsCapability? Capability { get; }
    bool Enable(int fps, FpsPowerSource source, uint appId);
    bool Disable(FpsPowerSource? source, uint appId);
}

internal sealed class IntelFrameLimiter : IIntelFrameLimiter
{
    private readonly NativeIgcl _native;
    internal IntelFrameLimiter(string? ownershipPath = null) => _native = new(ownershipPath ?? AddonDataPaths.IntelFpsLimitOwnershipPath);
    public bool Available => _native.Available;
    public string? UnavailableReason => _native.UnavailableReason;
    public IntelFpsCapability? Capability => _native.Capability;
    public bool Enable(int fps, FpsPowerSource source, uint appId) => _native.Set(true, fps, source, appId);
    public bool Disable(FpsPowerSource? source, uint appId) => _native.Set(false, 60, source, appId);
    public void Dispose() => _native.Dispose();
}

internal sealed class IntelFrameLimiterRuntime : IDisposable
{
    internal const int DefaultFps = 60;
    private readonly ProfileStore _store; private readonly ProfileMutationGate _gate; private readonly IIntelFrameLimiter _limiter; private readonly Func<FpsPowerSource?> _power; private readonly string _marker;
    private Func<uint> _app = static () => 0; private bool _shutdown;
    internal IntelFrameLimiterRuntime(ProfileStore store, ProfileMutationGate gate, IIntelFrameLimiter limiter, Func<FpsPowerSource?>? power = null, string? marker = null) { _store = store; _gate = gate; _limiter = limiter; _power = power ?? WindowsFpsPowerSource.Read; _marker = marker ?? AddonDataPaths.IntelFpsLimitOwnershipPath; }
    internal bool Available => _limiter.Available;
    internal string? UnavailableReason => _limiter.UnavailableReason;
    internal IntelFpsCapability? Capability => _limiter.Capability;
    internal void SetActualAppIdSource(Func<uint> source) => _app = source;
    internal void StartupRecover() { if (!File.Exists(_marker)) return; try { if (_limiter.Disable(_power(), 0)) File.Delete(_marker); else AppLog.Warn("Profiles.IntelFps", "Stale Intel FPS ownership cleanup failed; keeping marker."); } catch (Exception e) { AppLog.Error("Profiles.IntelFps", "Stale Intel FPS ownership cleanup failed.", e); } }
    internal void StartupReconcile(uint appId) => Reconcile(appId, "Startup");
    internal void Reconcile(uint appId, string reason = "Reconcile") { try { lock (_gate.Sync) { var loaded = _store.Load(); if (!loaded.CanSafelyReplace || !_limiter.Available) return; ApplyPolicy(loaded.Document, appId, reason); } } catch (Exception e) { AppLog.Error("Profiles.IntelFps", "FPS reconcile failed.", e, ("RunningAppID", appId), ("Reason", reason)); } }
    internal bool ReconcileWithResult(uint appId) { try { lock (_gate.Sync) { var loaded = _store.Load(); if (!loaded.CanSafelyReplace) return false; return ApplyPolicy(loaded.Document, appId, "Mutation"); } } catch (Exception e) { AppLog.Error("Profiles.IntelFps", "FPS apply failed.", e, ("RunningAppID", appId)); return false; } }
    private bool ApplyPolicy(ProfileDocument doc, uint appId, string reason)
    {
        var target = appId > 0 && doc.Games.TryGetValue(appId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var game) && game.Enabled && game.Performance.FpsLimit is { Enabled: true } fps ? fps : null;
        if (target is null) return Release(appId, reason);
        var source = _power(); if (source is null) return false;
        var value = source == FpsPowerSource.AC ? target.AcFps : target.DcFps;
        if (value is < 40 or > 120 || !_limiter.Enable(value, source.Value, appId)) return false;
        try { Directory.CreateDirectory(Path.GetDirectoryName(_marker)!); File.WriteAllText(_marker, $"{{\"fps\":{value}}}"); return true; }
        catch (Exception e) { AppLog.Error("Profiles.IntelFps", "FPS ownership marker persistence failed; disabling immediately.", e); _limiter.Disable(source, appId); return false; }
    }
    private bool Release(uint appId, string reason) { if (!File.Exists(_marker)) return true; var ok = _limiter.Disable(_power(), appId); if (ok) try { File.Delete(_marker); } catch (Exception e) { AppLog.Warn("Profiles.IntelFps", "FPS ownership marker deletion failed.", e); } return ok; }
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
    private const int FrameLimit = 2, Int32 = 2; private readonly string _marker; private nint _library, _api, _adapter; private bool _closed;
    internal bool Available { get; private set; } internal string? UnavailableReason { get; private set; } internal IntelFpsCapability? Capability { get; private set; }
    private readonly CtlInit _init = null!; private readonly CtlClose _close = null!; private readonly CtlEnumerate _enumerate = null!; private readonly CtlCaps _caps = null!; private readonly CtlGetSet _getSet = null!;
    internal NativeIgcl(string marker)
    {
        _marker = marker; try { _library = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "ControlLib.dll")); _init = Get<CtlInit>("ctlInit"); _close = Get<CtlClose>("ctlClose"); _enumerate = Get<CtlEnumerate>("ctlEnumerateDevices"); _caps = Get<CtlCaps>("ctlGetSupported3DCapabilities"); _getSet = Get<CtlGetSet>("ctlGetSet3DFeature"); Initialize(); }
        catch (Exception e) { UnavailableReason = e.Message; AppLog.Warn("Profiles.IntelFps", "IGCL is unavailable.", e); }
    }
    private T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    private void Initialize()
    {
        var args = new InitArgs { Size = (uint)Marshal.SizeOf<InitArgs>(), Version = 1, AppVersion = 0x00010001, ApplicationUid = new ApplicationId() }; var result = _init(ref args, out _api); Log("ctlInit", result); if (result != 0) throw new InvalidOperationException($"ctlInit failed: 0x{result:X8}");
        uint count = 0; result = _enumerate(_api, ref count, null); Log("ctlEnumerateDevices", result); if (result != 0 || count == 0) throw new InvalidOperationException("No IGCL adapter."); var adapters = new nint[count]; result = _enumerate(_api, ref count, adapters); Log("ctlEnumerateDevices", result); if (result != 0) throw new InvalidOperationException($"ctlEnumerateDevices failed: 0x{result:X8}");
        foreach (var adapter in adapters) { var details = new FeatureDetails[32]; var caps = new FeatureCaps { Size = (uint)Marshal.SizeOf<FeatureCaps>(), Version = 1, NumSupportedFeatures = (uint)details.Length }; var handle = GCHandle.Alloc(details, GCHandleType.Pinned); try { caps.Features = handle.AddrOfPinnedObject(); result = _caps(adapter, ref caps); Log("ctlGetSupported3DCapabilities", result); if (result != 0) continue; foreach (var d in details.Take((int)Math.Min(caps.NumSupportedFeatures, (uint)details.Length))) if (d.FeatureType == FrameLimit) { Capability = new(Int32Value(d.Value.IntType.Range.Min), Int32Value(d.Value.IntType.Range.Max), Int32Value(d.Value.IntType.Range.Step), (int)d.ValueType, d.FeatureMiscSupport, d.PerAppSupport); AppLog.Debug("Profiles.IntelFps", "Intel FRAME_LIMIT capability detected.", ("Minimum", Capability.Value.Minimum), ("Maximum", Capability.Value.Maximum), ("Step", Capability.Value.Step), ("ValueType", Capability.Value.ValueType), ("FeatureMiscSupport", Capability.Value.FeatureMiscSupport), ("PerAppSupport", Capability.Value.PerAppSupport)); if (Capability.Value.SupportsAddonRange) { _adapter = adapter; Available = true; return; } } } finally { handle.Free(); } }
        UnavailableReason = "Intel FRAME_LIMIT is unavailable or cannot represent 40-120 FPS.";
    }
    private static int Int32Value(int value) => value;
    internal bool Set(bool enable, int fps, FpsPowerSource? source, uint appId) { if (!Available || _adapter == 0) return false; var feature = new FeatureGetSet { Size = (uint)Marshal.SizeOf<FeatureGetSet>(), Version = 1, FeatureType = FrameLimit, ApplicationName = 0, ApplicationNameLength = 0, Set = true, ValueType = Int32, Value = new Property { Int = new IntProperty { Enable = enable, Value = fps } } }; var result = _getSet(_adapter, ref feature); Log(enable ? "ctlGetSet3DFeature enable" : "ctlGetSet3DFeature disable", result, fps, source, appId); return result == 0; }
    private static void Log(string operation, uint result, int? fps = null, FpsPowerSource? source = null, uint? appId = null) { if (result != 0) AppLog.Warn("Profiles.IntelFps", $"{operation} failed.", null, ("Operation", operation), ("Result", $"0x{result:X8}"), ("RequestedFps", fps), ("PowerSource", source), ("RunningAppID", appId)); }
    public void Dispose() { if (_closed) return; _closed = true; if (_api != 0) { var result = _close(_api); Log("ctlClose", result); _api = 0; } if (_library != 0) { NativeLibrary.Free(_library); _library = 0; } }
    [StructLayout(LayoutKind.Sequential)] private struct ApplicationId { public uint Data1; public ushort Data2; public ushort Data3; public byte Data4_0; public byte Data4_1; public byte Data4_2; public byte Data4_3; public byte Data4_4; public byte Data4_5; public byte Data4_6; public byte Data4_7; }
    [StructLayout(LayoutKind.Sequential)] private struct InitArgs { public uint Size; public byte Version; public uint AppVersion; public uint Flags; public uint SupportedVersion; public ApplicationId ApplicationUid; }
    [StructLayout(LayoutKind.Sequential)] private struct Range { public int Min; public int Max; public int Step; public int Default; }
    [StructLayout(LayoutKind.Sequential)] private struct IntInfo { [MarshalAs(UnmanagedType.I1)] public bool DefaultEnable; public Range Range; }
    [StructLayout(LayoutKind.Explicit, Size = 20)] private struct PropertyInfo { [FieldOffset(0)] public IntInfo IntType; }
    [StructLayout(LayoutKind.Sequential)] private struct FeatureDetails { public int FeatureType; public int ValueType; public PropertyInfo Value; public int CustomSize; public nint Custom; [MarshalAs(UnmanagedType.I1)] public bool PerAppSupport; public long Conflicts; public short FeatureMiscSupport; public short Reserved; public short Reserved1; public short Reserved2; }
    [StructLayout(LayoutKind.Sequential)] private struct FeatureCaps { public uint Size; public byte Version; public uint NumSupportedFeatures; public nint Features; }
    [StructLayout(LayoutKind.Sequential)] private struct IntProperty { [MarshalAs(UnmanagedType.I1)] public bool Enable; public int Value; }
    [StructLayout(LayoutKind.Explicit, Size = 8)] private struct Property { [FieldOffset(0)] public IntProperty Int; }
    [StructLayout(LayoutKind.Sequential)] private struct FeatureGetSet { public uint Size; public byte Version; public int FeatureType; public nint ApplicationName; public sbyte ApplicationNameLength; [MarshalAs(UnmanagedType.I1)] public bool Set; public int ValueType; public Property Value; public int CustomSize; public nint Custom; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlInit(ref InitArgs args, out nint api); [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlClose(nint api); [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlEnumerate(nint api, ref uint count, [Out] nint[]? devices); [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlCaps(nint adapter, ref FeatureCaps caps); [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CtlGetSet(nint adapter, ref FeatureGetSet feature);
}
