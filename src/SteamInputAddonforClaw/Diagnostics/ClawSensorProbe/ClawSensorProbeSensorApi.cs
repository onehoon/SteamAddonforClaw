using System.Runtime.InteropServices;

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
    private static readonly Guid SensorManagerClass = new("77A1C827-FCD2-4689-8915-9D613CC5FA3E");
    private IntPtr _manager;
    public ClawSensorProbeSensorApi() => _manager = CreateManager();
    private static IntPtr CreateManager()
    {
        var type = Type.GetTypeFromCLSID(SensorManagerClass, true) ?? throw new COMException("SensorManager coclass is unavailable.");
        return Marshal.GetIUnknownForObject(Activator.CreateInstance(type)!);
    }
    public void Dispose() { if (_manager != IntPtr.Zero) { Marshal.Release(_manager); _manager = IntPtr.Zero; } GC.SuppressFinalize(this); }
    ~ClawSensorProbeSensorApi() => Dispose();
    internal IntPtr GetAllSensors()
    {
        var vtable = Marshal.ReadIntPtr(_manager);
        var call = Marshal.GetDelegateForFunctionPointer<GetSensorsByCategory>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
        var category = SensorCategoryAll;
        var hr = call(_manager, ref category, out var collection);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return collection;
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
    internal static ClawSensorProbeCandidate ReadMetadata(IntPtr sensor)
    {
        var id = ReadString(sensor, SensorGetIdSlot);
        var name = ReadString(sensor, SensorGetFriendlyNameSlot);
        var category = ReadGuid(sensor, SensorGetCategorySlot);
        var type = ReadGuid(sensor, SensorGetTypeSlot);
        return new(name, id, type.ToString("D"), category.ToString("D"));
    }
    internal static (double X, double Y, double Z, long? Timestamp) ReadXYZ(IntPtr sensor)
    {
        var vtable = Marshal.ReadIntPtr(sensor);
        var getData = Marshal.GetDelegateForFunctionPointer<GetReport>(Marshal.ReadIntPtr(vtable, SensorGetDataSlot * IntPtr.Size));
        var hr = getData(sensor, out var report);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        using var ownedReport = new OwnedComPointer(report);
        return (ReadValue(ownedReport.Pointer, 7), ReadValue(ownedReport.Pointer, 8), ReadValue(ownedReport.Pointer, 9), TryReadTimestamp(ownedReport.Pointer));
    }
    private static long? TryReadTimestamp(IntPtr report)
    {
        try
        {
            var vtable = Marshal.ReadIntPtr(report);
            var call = Marshal.GetDelegateForFunctionPointer<GetTimestamp>(Marshal.ReadIntPtr(vtable, ReportGetTimestampSlot * IntPtr.Size));
            var hr = call(report, out var timestamp);
            return hr < 0 ? null : (long)timestamp;
        }
        catch { return null; }
    }
    internal static string ReadOptionalMetadata(IntPtr sensor, int slot) => TryReadString(sensor, slot);
    private static string TryReadString(IntPtr sensor, int slot)
    {
        try { return ReadString(sensor, slot); }
        catch { return "Unavailable"; }
    }
    internal static ClawSensorProbeCandidate ReadCandidate(IntPtr sensor)
    {
        var baseCandidate = ReadMetadata(sensor);
        return baseCandidate with
        {
            Manufacturer = ReadPropertyString(sensor, SensorPropertyManufacturer),
            Model = ReadPropertyString(sensor, SensorPropertyModel),
            PersistentUniqueId = ReadPropertyGuid(sensor, SensorPropertyPersistentUniqueId, baseCandidate.SensorId),
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
    private static string ReadPropertyGuid(IntPtr sensor, int propertyId, string fallback)
    {
        try
        {
            var value = ReadProperty(sensor, propertyId);
            try { return value.VarType == 72 && value.Pointer != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(value.Pointer).ToString("D") : fallback; }
            finally { value.Dispose(); }
        }
        catch { return fallback; }
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
                4 => value.Int32,
                5 => value.UInt32,
                11 => value.Bool ? 1 : 0,
                14 => (double)Marshal.PtrToStructure<decimal>(value.Pointer),
                18 => value.UInt32,
                20 => value.Int64,
                21 => value.UInt64,
                23 => value.Double,
                _ => throw new InvalidOperationException($"Unsupported sensor value type {value.VarType}.")
            };
        }
        finally { value.Dispose(); }
    }
    private static string ReadString(IntPtr sensor, int slot)
    {
        var vtable = Marshal.ReadIntPtr(sensor);
        var call = Marshal.GetDelegateForFunctionPointer<GetString>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
        var hr = call(sensor, out var value);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        try { return value == IntPtr.Zero ? "Unavailable" : Marshal.PtrToStringUni(value) ?? "Unavailable"; }
        finally { if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value); }
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
    private const int SensorPropertyPersistentUniqueId = 5, SensorPropertyManufacturer = 6, SensorPropertyModel = 7, SensorPropertyMinReportInterval = 12, SensorPropertyHidUsage = 22;
    internal const int CollectionGetAtSlot = 3, CollectionGetCountSlot = 4, SensorGetIdSlot = 3, SensorGetCategorySlot = 4, SensorGetTypeSlot = 5, SensorGetFriendlyNameSlot = 6, SensorGetPropertySlot = 9, SensorGetDataSlot = 13, ReportGetTimestampSlot = 3, ReportGetSensorValueSlot = 4;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSensorsByCategory(IntPtr self, ref Guid category, out IntPtr collection);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetCount(IntPtr self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAt(IntPtr self, int index, out IntPtr sensor);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetString(IntPtr self, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetGuid(IntPtr self, out Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetProperty(IntPtr self, ref PropertyKey key, out PropVariant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetReport(IntPtr self, out IntPtr report);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSensorValue(IntPtr self, ref PropertyKey key, out PropVariant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetTimestamp(IntPtr self, out ulong timestamp);

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid formatId, int propertyId) { public Guid FormatId = formatId; public int PropertyId = propertyId; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public int Int32;
        [FieldOffset(8)] public uint UInt32;
        [FieldOffset(8)] public long Int64;
        [FieldOffset(8)] public ulong UInt64;
        [FieldOffset(8)] public double Double;
        [FieldOffset(8)] public IntPtr Pointer;
        public bool Bool => Pointer != IntPtr.Zero && Marshal.ReadInt16(Pointer) != 0;
        public void Dispose() => _ = PropVariantClear(ref this);
    }
    [DllImport("oleaut32.dll")] private static extern int PropVariantClear(ref PropVariant value);

}
