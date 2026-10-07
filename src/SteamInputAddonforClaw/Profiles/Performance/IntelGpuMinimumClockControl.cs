using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Profiles.Performance;

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

internal interface IIntelGpuMinimumClockControl : IDisposable
{
    IntelGpuMinimumClockNativeCapability Initialize();
    IntelGpuMinimumClockNativeCapability Reinitialize();
    IntelGpuFrequencyRange GetRange();
    uint SetRange(IntelGpuFrequencyRange range);
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
