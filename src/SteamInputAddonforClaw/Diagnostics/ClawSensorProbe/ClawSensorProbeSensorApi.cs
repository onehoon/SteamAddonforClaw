using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;

namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

internal sealed class ClawSensorProbeSensorApi : IDisposable
{
    private sealed class OwnedComPointer : IDisposable
    {
        public IntPtr Pointer { get; private set; }
        public OwnedComPointer(IntPtr pointer) => Pointer = pointer;
        public void Dispose()
        {
            if (Pointer == IntPtr.Zero) return;
            Marshal.Release(Pointer);
            Pointer = IntPtr.Zero;
        }
    }
    internal static readonly Guid SensorCategoryAll = new("C317C286-C468-4288-9975-D4C4587C442C");
    internal static readonly Guid SensorDataTypeCustomGuid = new("B14C764F-07CF-41E8-9D82-EBE3D0776A6F");
    private const int HResultFromWin32ErrorNoData = unchecked((int)0x800700E8);
    private static readonly Guid SensorManagerClass = new("77A1C827-FCD2-4689-8915-9D613CC5FA3E");
    private ISensorManager? _manager;
    public ClawSensorProbeSensorApi()
    {
        var type = Type.GetTypeFromCLSID(SensorManagerClass, true) ?? throw new COMException("SensorManager coclass is unavailable.");
        _manager = (ISensorManager)Activator.CreateInstance(type)!;
    }
    public void Dispose() { if (_manager is not null) { Marshal.FinalReleaseComObject(_manager); _manager = null; } GC.SuppressFinalize(this); }
    ~ClawSensorProbeSensorApi() => Dispose();
    internal IntPtr GetAllSensors()
    {
        var manager = _manager ?? throw new ObjectDisposedException(nameof(ClawSensorProbeSensorApi));
        var category = SensorCategoryAll;
        var hr = manager.GetSensorsByCategory(ref category, out var collection);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return collection;
    }
    internal IntPtr GetSensorById(Guid sensorId)
    {
        var manager = _manager ?? throw new ObjectDisposedException(nameof(ClawSensorProbeSensorApi));
        var hr = manager.GetSensorByID(ref sensorId, out var sensor);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return sensor;
    }
    internal static int GetCollectionCount(IntPtr collection)
    {
        var vtable = Marshal.ReadIntPtr(collection);
        var call = Marshal.GetDelegateForFunctionPointer<GetCount>(Marshal.ReadIntPtr(vtable, CollectionGetCountSlot * IntPtr.Size));
        var hr = call(collection, out var count);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return count;
    }
    internal ClawSensorDiscovery Discover()
    {
        var sensors = new List<ClawSensorProbeCandidate>();
        using var ownedCollection = new OwnedComPointer(GetAllSensors());
        var collection = ownedCollection.Pointer;
        for (var i = 0; i < GetCollectionCount(collection); i++)
        {
            var sensor = GetCollectionItem(collection, i);
            try { sensors.Add(ReadCandidate(sensor)); }
            finally
            {
                if (sensor != IntPtr.Zero)
                {
                    using var ownedSensor = new OwnedComPointer(sensor);
                }
            }
        }
        return ClawSensorDiscovery.Select(sensors);
    }

    // Diagnostics-only one-shot queries for Environment Discovery. These preserve the raw HRESULT
    // and every returned candidate as report evidence instead of throwing, unlike Discover() above,
    // which remains the interactive probe's existing selection behavior and is left unchanged.
    internal LegacySensorQueryInfo EnumerateByCategory(Guid category, string? label = null)
    {
        var manager = _manager ?? throw new ObjectDisposedException(nameof(ClawSensorProbeSensorApi));
        return Enumerate("CategoryAll", category, label, manager.GetSensorsByCategory);
    }
    internal LegacySensorQueryInfo EnumerateByType(Guid type, string? label = null)
    {
        var manager = _manager ?? throw new ObjectDisposedException(nameof(ClawSensorProbeSensorApi));
        return Enumerate("DirectType", type, label, manager.GetSensorsByType);
    }
    private delegate int SensorEnumerationMethod(ref Guid guid, out IntPtr collection);
    private static LegacySensorQueryInfo Enumerate(string queryKind, Guid guid, string? label, SensorEnumerationMethod query)
    {
        var target = guid;
        var hr = query(ref target, out var collection);
        if (hr < 0) return new LegacySensorQueryInfo(queryKind, guid.ToString("D"), label, false, hr, DescribeFailure(hr), []);
        using var ownedCollection = new OwnedComPointer(collection);
        var candidates = new List<LegacySensorCandidateInfo>();
        for (var i = 0; i < GetCollectionCount(collection); i++)
        {
            var sensor = GetCollectionItem(collection, i);
            try { candidates.Add(ReadDiagnosticCandidate(sensor)); }
            finally { if (sensor != IntPtr.Zero) { using var ownedSensor = new OwnedComPointer(sensor); } }
        }
        return new LegacySensorQueryInfo(queryKind, guid.ToString("D"), label, true, hr, null, candidates);
    }
    private static string DescribeFailure(int hr)
    {
        try { return Marshal.GetExceptionForHR(hr)?.Message ?? "Unavailable"; }
        catch { return "Unavailable"; }
    }
    private static LegacySensorCandidateInfo ReadDiagnosticCandidate(IntPtr sensor)
    {
        var candidate = ReadCandidate(sensor);
        return new LegacySensorCandidateInfo(
            candidate.FriendlyName,
            candidate.SensorId,
            candidate.TypeGuid,
            candidate.CategoryGuid,
            ReadPropertyState(sensor),
            candidate.Manufacturer,
            candidate.Model,
            candidate.PersistentUniqueId,
            ReadPropertyString(sensor, SensorPropertyDevicePath),
            candidate.MinimumReportInterval,
            candidate.CustomUsage,
            ReadSupportsDataField(sensor, 7),
            ReadSupportsDataField(sensor, 8),
            ReadSupportsDataField(sensor, 9));
    }
    private static string ReadPropertyState(IntPtr sensor)
    {
        try
        {
            var value = ReadProperty(sensor, SensorPropertyState);
            try { return value.VarType == 19 ? DescribeState(value.UInt32) : "Unavailable"; }
            finally { value.Dispose(); }
        }
        catch { return "Unavailable"; }
    }
    private static string DescribeState(uint state) => state switch
    {
        0 => "Min", 1 => "Ready", 2 => "NotAvailable", 3 => "NoData", 4 => "Initializing", 5 => "AccessDenied", 6 => "Error", _ => "Unavailable"
    };
    private static string ReadSupportsDataField(IntPtr sensor, int pid)
    {
        try
        {
            var vtable = Marshal.ReadIntPtr(sensor);
            var call = Marshal.GetDelegateForFunctionPointer<SupportsDataField>(Marshal.ReadIntPtr(vtable, SensorSupportsDataFieldSlot * IntPtr.Size));
            var key = new PropertyKey(SensorDataTypeCustomGuid, pid);
            var hr = call(sensor, ref key, out var supported);
            return hr < 0 ? "Unavailable" : (supported != 0).ToString();
        }
        catch { return "Unavailable"; }
    }
    internal static ClawSensorProbeCandidate ReadMetadata(IntPtr sensor)
    {
        var id = ReadSensorId(sensor).ToString("D");
        var name = ReadFriendlyName(sensor);
        var category = ReadGuid(sensor, SensorGetCategorySlot);
        var type = ReadGuid(sensor, SensorGetTypeSlot);
        return new(name, id, type.ToString("D"), category.ToString("D"));
    }
    internal static ClawSensorReportReadResult ReadXYZ(IntPtr sensor)
    {
        var vtable = Marshal.ReadIntPtr(sensor);
        var getData = Marshal.GetDelegateForFunctionPointer<GetReport>(Marshal.ReadIntPtr(vtable, SensorGetDataSlot * IntPtr.Size));
        var hr = getData(sensor, out var report);
        if (hr == HResultFromWin32ErrorNoData) return ClawSensorReportReadResult.NoData();
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        using var ownedReport = new OwnedComPointer(report);
        return ClawSensorReportReadResult.Data(ReadValue(ownedReport.Pointer, 7), ReadValue(ownedReport.Pointer, 8), ReadValue(ownedReport.Pointer, 9), ReadTimestamp(ownedReport.Pointer));
    }
    internal static ClawSensorProbeCandidate ReadCandidate(IntPtr sensor)
    {
        var baseCandidate = ReadMetadata(sensor);
        return baseCandidate with
        {
            Manufacturer = ReadPropertyString(sensor, SensorPropertyManufacturer),
            Model = ReadPropertyString(sensor, SensorPropertyModel),
            PersistentUniqueId = ReadPropertyGuid(sensor, SensorPropertyPersistentUniqueId),
            MinimumReportInterval = ReadPropertyUInt32(sensor, SensorPropertyMinReportInterval),
            CustomUsage = ReadPropertyUInt32(sensor, SensorPropertyHidUsage)
        };
    }
    private static string ReadPropertyString(IntPtr sensor, int propertyId)
    {
        try
        {
            var value = ReadProperty(sensor, propertyId);
            try { return value.VarType == 31 && value.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(value.Pointer) ?? "Unavailable" : "Unavailable"; }
            finally { value.Dispose(); }
        }
        catch { return "Unavailable"; }
    }
    private static string ReadPropertyGuid(IntPtr sensor, int propertyId)
    {
        try
        {
            var value = ReadProperty(sensor, propertyId);
            try { return value.VarType == 72 && value.Pointer != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(value.Pointer).ToString("D") : "Unavailable"; }
            finally { value.Dispose(); }
        }
        catch { return "Unavailable"; }
    }
    private static string ReadPropertyUInt32(IntPtr sensor, int propertyId)
    {
        try
        {
            var value = ReadProperty(sensor, propertyId);
            try { return value.VarType == 19 ? value.UInt32.ToString(System.Globalization.CultureInfo.InvariantCulture) : "Unavailable"; }
            finally { value.Dispose(); }
        }
        catch { return "Unavailable"; }
    }
    private static PropVariant ReadProperty(IntPtr sensor, int propertyId)
    {
        var vtable = Marshal.ReadIntPtr(sensor);
        var call = Marshal.GetDelegateForFunctionPointer<GetProperty>(Marshal.ReadIntPtr(vtable, SensorGetPropertySlot * IntPtr.Size));
        var key = new PropertyKey(SensorPropertyCommonGuid, propertyId);
        var hr = call(sensor, ref key, out var value);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return value;
    }
    private static double ReadValue(IntPtr report, int pid)
    {
        var vtable = Marshal.ReadIntPtr(report);
        var getValue = Marshal.GetDelegateForFunctionPointer<GetSensorValue>(Marshal.ReadIntPtr(vtable, ReportGetSensorValueSlot * IntPtr.Size));
        var key = new PropertyKey(SensorDataTypeCustomGuid, pid);
        var value = new PropVariant();
        var hr = getValue(report, ref key, out value);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        try
        {
            return value.VarType switch
            {
                3 => value.Int32,
                4 => value.Float,
                5 => value.Double,
                11 => value.Bool ? 1 : 0,
                19 => value.UInt32,
                _ => throw new InvalidOperationException($"Unsupported sensor value type {value.VarType}.")
            };
        }
        finally { value.Dispose(); }
    }
    private static DateTimeOffset ReadTimestamp(IntPtr report)
    {
        var vtable = Marshal.ReadIntPtr(report);
        var getTimestamp = Marshal.GetDelegateForFunctionPointer<GetTimestamp>(Marshal.ReadIntPtr(vtable, ReportGetTimestampSlot * IntPtr.Size));
        var hr = getTimestamp(report, out var timestamp);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return timestamp.ToDateTimeOffset();
    }
    private static Guid ReadSensorId(IntPtr sensor)
    {
        var vtable = Marshal.ReadIntPtr(sensor);
        var call = Marshal.GetDelegateForFunctionPointer<GetGuid>(Marshal.ReadIntPtr(vtable, SensorGetIdSlot * IntPtr.Size));
        var hr = call(sensor, out var value);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return value;
    }
    private static string ReadFriendlyName(IntPtr sensor)
    {
        var vtable = Marshal.ReadIntPtr(sensor);
        var call = Marshal.GetDelegateForFunctionPointer<GetFriendlyName>(Marshal.ReadIntPtr(vtable, SensorGetFriendlyNameSlot * IntPtr.Size));
        var hr = call(sensor, out var value);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return value ?? "Unavailable";
    }
    private static Guid ReadGuid(IntPtr sensor, int slot)
    {
        var vtable = Marshal.ReadIntPtr(sensor);
        var call = Marshal.GetDelegateForFunctionPointer<GetGuid>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
        var hr = call(sensor, out var value);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return value;
    }
    internal static IntPtr GetCollectionItem(IntPtr collection, int index)
    {
        var vtable = Marshal.ReadIntPtr(collection);
        var call = Marshal.GetDelegateForFunctionPointer<GetAt>(Marshal.ReadIntPtr(vtable, CollectionGetAtSlot * IntPtr.Size));
        var hr = call(collection, index, out var sensor);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return sensor;
    }
    // The returned Sensor API interfaces are intentionally consumed through the validated raw vtable slots.
    private static readonly Guid SensorPropertyCommonGuid = new("7F8383EC-D3EC-495C-A8CF-B8BBE85C2920");
    private const int SensorPropertyPersistentUniqueId = 5, SensorPropertyManufacturer = 6, SensorPropertyModel = 7, SensorPropertyState = 3, SensorPropertyMinReportInterval = 12, SensorPropertyDevicePath = 15, SensorPropertyHidUsage = 22;
    // ISensor vtable order (sensorsapi.h, confirmed against Windows SDK SensorsApi.h): GetID=3, GetCategory=4, GetType=5,
    // GetFriendlyName=6, GetProperty=7, GetProperties=8, GetSupportedDataFields=9, SetProperties=10, SupportsDataField=11, GetState=12, GetData=13.
    internal const int CollectionGetAtSlot = 3, CollectionGetCountSlot = 4, SensorGetIdSlot = 3, SensorGetCategorySlot = 4, SensorGetTypeSlot = 5, SensorGetFriendlyNameSlot = 6, SensorGetPropertySlot = 7, SensorSupportsDataFieldSlot = 11, SensorGetDataSlot = 13, ReportGetTimestampSlot = 3, ReportGetSensorValueSlot = 4;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetCount(IntPtr self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAt(IntPtr self, int index, out IntPtr sensor);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetFriendlyName(IntPtr self, [MarshalAs(UnmanagedType.BStr)] out string? value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetGuid(IntPtr self, out Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetProperty(IntPtr self, ref PropertyKey key, out PropVariant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetReport(IntPtr self, out IntPtr report);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSensorValue(IntPtr self, ref PropertyKey key, out PropVariant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetTimestamp(IntPtr self, out SystemTime timestamp);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SupportsDataField(IntPtr self, ref PropertyKey key, out short supported);

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid formatId, int propertyId) { public Guid FormatId = formatId; public int PropertyId = propertyId; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] internal struct PropVariant
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public int Int32;
        [FieldOffset(8)] public uint UInt32;
        [FieldOffset(8)] public long Int64;
        [FieldOffset(8)] public ulong UInt64;
        [FieldOffset(8)] public float Float;
        [FieldOffset(8)] public double Double;
        [FieldOffset(8)] public short VariantBool;
        [FieldOffset(8)] public IntPtr Pointer;
        public bool Bool => VariantBool != 0;
        public void Dispose() => _ = PropVariantClear(ref this);
    }
    internal static double ConvertPropVariantForTest(PropVariant value) => value.VarType switch
    {
        3 => value.Int32,
        4 => value.Float,
        5 => value.Double,
        11 => value.Bool ? 1 : 0,
        19 => value.UInt32,
        _ => throw new InvalidOperationException($"Unsupported sensor value type {value.VarType}.")
    };
    internal static int ClearPropVariantForTest(ref PropVariant value) => PropVariantClear(ref value);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SystemTime
    {
        private readonly ushort _year, _month, _dayOfWeek, _day, _hour, _minute, _second, _milliseconds;
        public DateTimeOffset ToDateTimeOffset() => new(_year, _month, _day, _hour, _minute, _second, _milliseconds, TimeSpan.Zero);
    }

    [ComImport]
    [Guid("BD77DB67-45A8-42DC-8D00-6DCF15F8377A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISensorManager
    {
        [PreserveSig] int GetSensorsByCategory(ref Guid sensorCategory, out IntPtr sensorCollection);
        [PreserveSig] int GetSensorsByType(ref Guid sensorType, out IntPtr sensorCollection);
        [PreserveSig] int GetSensorByID(ref Guid sensorId, out IntPtr sensor);
    }

}
