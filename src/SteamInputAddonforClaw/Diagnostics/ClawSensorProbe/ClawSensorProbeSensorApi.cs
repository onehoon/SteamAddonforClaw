using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

internal sealed class ClawSensorProbeSensorApi : IDisposable
{
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
        var collection = GetAllSensors();
        try
        {
            for (var i = 0; i < GetCollectionCount(collection); i++)
            {
                var sensor = GetCollectionItem(collection, i);
                try { sensors.Add(ReadCandidate(sensor)); }
                finally { if (sensor != IntPtr.Zero) Marshal.Release(sensor); }
            }
        }
        finally { if (collection != IntPtr.Zero) Marshal.Release(collection); }
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
    internal static (double X, double Y, double Z) ReadXYZ(IntPtr sensor)
    {
        var vtable = Marshal.ReadIntPtr(sensor);
        var getData = Marshal.GetDelegateForFunctionPointer<GetReport>(Marshal.ReadIntPtr(vtable, SensorGetDataSlot * IntPtr.Size));
        var hr = getData(sensor, out var report);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        try { return (ReadValue(report, 7), ReadValue(report, 8), ReadValue(report, 9)); }
        finally { if (report != IntPtr.Zero) Marshal.Release(report); }
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
            Manufacturer = "Unavailable: optional property not exposed by verified contract",
            Model = "Unavailable: optional property not exposed by verified contract",
            PersistentUniqueId = "Unavailable: optional property not exposed by verified contract",
            MinimumReportInterval = "Unavailable: optional property not exposed by verified contract",
            CustomUsage = "Unavailable: optional property not exposed by verified contract"
        };
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
        finally { PropVariantClear(ref value); }
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
    internal const int CollectionGetAtSlot = 3, CollectionGetCountSlot = 4, SensorGetIdSlot = 3, SensorGetCategorySlot = 4, SensorGetTypeSlot = 5, SensorGetFriendlyNameSlot = 6, SensorGetDataSlot = 13, ReportGetTimestampSlot = 3, ReportGetSensorValueSlot = 4;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSensorsByCategory(IntPtr self, ref Guid category, out IntPtr collection);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetCount(IntPtr self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAt(IntPtr self, int index, out IntPtr sensor);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetString(IntPtr self, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetGuid(IntPtr self, out Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetReport(IntPtr self, out IntPtr report);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSensorValue(IntPtr self, ref PropertyKey key, out PropVariant value);

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
        public bool Bool => Marshal.ReadInt16(Pointer) != 0;
    }
    [DllImport("oleaut32.dll")] private static extern int PropVariantClear(ref PropVariant value);

}
