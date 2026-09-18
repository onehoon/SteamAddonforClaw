using System.Globalization;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Devices.MSI.Claw;

namespace SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;

internal sealed class EnvironmentDiscoveryBackendException(string message) : Exception(message);

internal static class WindowsControllerBackendDiscovery
{
    private const uint RimTypeHid = 2;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RidiDeviceInfo = 0x2000000B;
    private const ushort GenericDesktopUsagePage = 0x0001;
    private const ushort JoystickUsage = 0x0004;
    private const ushort GamepadUsage = 0x0005;
    private const ushort ViiperXbox360VendorId = 0x045E;
    private const ushort ViiperXbox360ProductId = 0x028E;
    private const ushort ViiperSteamDeckVendorId = 0x28DE;
    private const ushort ViiperSteamDeckProductId = 0x1205;

    internal static IReadOnlyList<RawInputDeviceDiscoveryInfo> CaptureRawInput()
    {
        var deviceCount = 0u;
        var listSize = (uint)Marshal.SizeOf<RawInputDeviceList>();
        if (GetRawInputDeviceList(null, ref deviceCount, listSize) == uint.MaxValue)
            throw new EnvironmentDiscoveryBackendException($"RawInputDeviceListFailed:{LastError()}");
        if (deviceCount == 0) return [];

        var devices = new RawInputDeviceList[checked((int)deviceCount)];
        if (GetRawInputDeviceList(devices, ref deviceCount, listSize) == uint.MaxValue)
            throw new EnvironmentDiscoveryBackendException($"RawInputDeviceListFailed:{LastError()}");

        var results = new List<RawInputDeviceDiscoveryInfo>();
        foreach (var device in devices)
        {
            if (device.Type != RimTypeHid) continue;
            try
            {
                var info = ReadRawInputDeviceInfo(device.Device);
                if (!IsRelevantRawInput(info.VendorId, info.ProductId, info.UsagePage, info.Usage)) continue;

                var path = ReadRawInputDeviceName(device.Device);
                if (path is null) continue;
                results.Add(new RawInputDeviceDiscoveryInfo(
                    "HID",
                    path,
                    TryConvertHidPathToPnpInstanceId(path),
                    info.VendorId,
                    info.ProductId,
                    info.VersionNumber,
                    info.UsagePage,
                    info.Usage));
            }
            catch
            {
                // A device can disappear between the list and the metadata calls. Keep the
                // one-shot report best-effort without allowing one stale endpoint to suppress
                // the other backend captures.
            }
        }

        return results;
    }

    internal static IReadOnlyList<GameInputDeviceDiscoveryInfo> CaptureGameInput()
    {
        try
        {
            return CaptureGameInputCore();
        }
        catch (EnvironmentDiscoveryBackendException)
        {
            throw;
        }
        catch (DllNotFoundException exception)
        {
            throw new EnvironmentDiscoveryBackendException($"GameInputUnavailable:{exception.GetType().Name}");
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new EnvironmentDiscoveryBackendException($"GameInputUnavailable:{exception.GetType().Name}");
        }
        catch (COMException exception)
        {
            throw new EnvironmentDiscoveryBackendException($"GameInputInteropFailed:0x{unchecked((uint)exception.HResult):X8}");
        }
        catch (Exception exception)
        {
            throw new EnvironmentDiscoveryBackendException($"GameInputInteropFailed:{exception.GetType().Name}");
        }
    }

    internal static bool IsRelevantRawInput(ushort vendorId, ushort productId, ushort usagePage, ushort usage) =>
        usagePage == GenericDesktopUsagePage && usage is JoystickUsage or GamepadUsage
        || MsiClawHardware.IsKnownController(vendorId, productId)
        || vendorId == ViiperXbox360VendorId && productId == ViiperXbox360ProductId
        || vendorId == ViiperSteamDeckVendorId && productId == ViiperSteamDeckProductId;

    internal static string? TryConvertHidPathToPnpInstanceId(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return null;
        var path = devicePath.Trim();
        if (!path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)) return null;

        var parts = path[4..].Split('#');
        return parts.Length >= 3
            && string.Equals(parts[0], "HID", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(parts[1])
            && !string.IsNullOrWhiteSpace(parts[2])
            ? $"HID\\{parts[1]}\\{parts[2]}"
            : null;
    }

    private static IReadOnlyList<GameInputDeviceDiscoveryInfo> CaptureGameInputCore()
    {
        IGameInput? gameInput = null;
        var callbackRegistered = false;
        ulong callbackToken = 0;
        var ordinal = 0;
        var results = new List<GameInputDeviceDiscoveryInfo>();
        var gate = new object();
        GameInputDeviceCallback callback = (_, _, device, _, currentStatus, _) =>
        {
            var currentOrdinal = Interlocked.Increment(ref ordinal);
            var status = FormatGameInputStatus(currentStatus);
            GameInputDeviceDiscoveryInfo result;
            try
            {
                if (device is null)
                {
                    result = FailedGameInputRecord(currentOrdinal, status, "DeviceCallbackNull");
                }
                else
                {
                    var hResult = device.GetDeviceInfo(out var infoPointer);
                    if (hResult < 0 || infoPointer == IntPtr.Zero)
                    {
                        result = FailedGameInputRecord(currentOrdinal, status, $"GetDeviceInfoFailed:0x{unchecked((uint)hResult):X8}");
                    }
                    else
                    {
                        var info = Marshal.PtrToStructure<NativeGameInputDeviceInfo>(infoPointer);
                        result = new GameInputDeviceDiscoveryInfo(
                            currentOrdinal,
                            info.VendorId,
                            info.ProductId,
                            info.RevisionNumber,
                            info.Usage.Page,
                            info.Usage.Id,
                            FormatGameInputFamily(info.DeviceFamily),
                            FormatGameInputKind(info.SupportedInput),
                            status,
                            info.ContainerId == Guid.Empty ? null : info.ContainerId,
                            FormatDeviceId(info.DeviceId),
                            FormatDeviceId(info.DeviceRootId),
                            info.DisplayName == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(info.DisplayName),
                            info.PnpPath == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(info.PnpPath));
                    }
                }
            }
            catch (Exception exception)
            {
                result = FailedGameInputRecord(currentOrdinal, status, $"GetDeviceInfoFailed:{exception.GetType().Name}");
            }

            lock (gate) results.Add(result);
        };

        try
        {
            var createResult = GameInputCreate(out gameInput);
            ThrowIfFailed(createResult, "GameInputCreate");
            if (gameInput is null) throw new EnvironmentDiscoveryBackendException("GameInputCreateFailed:0x80004003");

            var registerResult = gameInput.RegisterDeviceCallback(
                null,
                GameInputKindController | GameInputKindGamepad,
                GameInputDeviceAnyStatus,
                GameInputBlockingEnumeration,
                IntPtr.Zero,
                callback,
                out callbackToken);
            ThrowIfFailed(registerResult, "RegisterDeviceCallback");
            callbackRegistered = true;
            return results.OrderBy(item => item.EnumerationOrdinal).ToArray();
        }
        finally
        {
            if (callbackRegistered)
            {
                try { gameInput?.UnregisterCallback(callbackToken); }
                catch { }
            }

            if (gameInput is not null && Marshal.IsComObject(gameInput))
            {
                try { Marshal.FinalReleaseComObject(gameInput); }
                catch { }
            }
        }
    }

    private static GameInputDeviceDiscoveryInfo FailedGameInputRecord(int ordinal, string status, string failure) =>
        new(ordinal, null, null, null, null, null, null, null, status, null, null, null, null, null, failure);

    private static void ThrowIfFailed(int hResult, string operation)
    {
        if (hResult < 0) throw new EnvironmentDiscoveryBackendException($"{operation}Failed:0x{unchecked((uint)hResult):X8}");
    }

    private static RawInputHidInfo ReadRawInputDeviceInfo(IntPtr device)
    {
        var size = Marshal.SizeOf<RidDeviceInfo>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new RidDeviceInfo { Size = (uint)size }, pointer, false);
            var byteCount = (uint)size;
            if (GetRawInputDeviceInfoW(device, RidiDeviceInfo, pointer, ref byteCount) == uint.MaxValue)
                throw new InvalidOperationException($"RawInputDeviceInfoFailed:{LastError()}");

            var info = Marshal.PtrToStructure<RidDeviceInfo>(pointer);
            return new RawInputHidInfo(info.Type, checked((ushort)info.VendorId), checked((ushort)info.ProductId), info.VersionNumber, info.UsagePage, info.Usage);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static string? ReadRawInputDeviceName(IntPtr device)
    {
        var characterCount = 0u;
        var result = GetRawInputDeviceInfoW(device, RidiDeviceName, IntPtr.Zero, ref characterCount);
        if (result == uint.MaxValue || characterCount == 0) return null;

        var pointer = Marshal.AllocHGlobal(checked((int)((characterCount + 1) * 2)));
        try
        {
            var capacity = characterCount;
            result = GetRawInputDeviceInfoW(device, RidiDeviceName, pointer, ref capacity);
            return result == uint.MaxValue ? null : Marshal.PtrToStringUni(pointer, checked((int)result));
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static string? FormatDeviceId(byte[]? value) => value is null || value.Length == 0 ? null : Convert.ToHexString(value);

    private static string FormatGameInputFamily(int value) => value switch
    {
        -1 => "Virtual",
        0 => "Unknown",
        1 => "XboxOne",
        2 => "Xbox360",
        3 => "Hid",
        4 => "I8042",
        5 => "Aggregate",
        _ => $"0x{unchecked((uint)value):X8}"
    };

    private static string FormatGameInputKind(int value)
    {
        var names = new List<string>();
        AddFlag(names, value, 0x0000000E, "Controller");
        AddFlag(names, value, 0x00040000, "Gamepad");
        AddFlag(names, value, 0x00000010, "Keyboard");
        AddFlag(names, value, 0x00000020, "Mouse");
        AddFlag(names, value, 0x00000040, "Sensors");
        return names.Count == 0 ? $"0x{unchecked((uint)value):X8}" : string.Join('|', names);
    }

    private static void AddFlag(List<string> names, int value, int flag, string name)
    {
        if ((value & flag) == flag) names.Add(name);
    }

    private static string FormatGameInputStatus(int value) => value switch
    {
        0 => "NoStatus",
        1 => "Connected",
        0x00200000 => "HapticInfoReady",
        -1 => "AnyStatus",
        _ => $"0x{unchecked((uint)value):X8}"
    };

    private static string LastError() => Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);

    private const int GameInputKindController = 0x0000000E;
    private const int GameInputKindGamepad = 0x00040000;
    private const int GameInputDeviceAnyStatus = -1;
    private const int GameInputBlockingEnumeration = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList
    {
        public IntPtr Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct RidDeviceInfo
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(4)] public uint Type;
        [FieldOffset(8)] public uint VendorId;
        [FieldOffset(12)] public uint ProductId;
        [FieldOffset(16)] public uint VersionNumber;
        [FieldOffset(20)] public ushort UsagePage;
        [FieldOffset(22)] public ushort Usage;
    }

    private readonly record struct RawInputHidInfo(uint Type, ushort VendorId, ushort ProductId, uint VersionNumber, ushort UsagePage, ushort Usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGameInputUsage
    {
        public ushort Page;
        public ushort Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGameInputVersion
    {
        public ushort Major;
        public ushort Minor;
        public ushort Build;
        public ushort Revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGameInputDeviceInfo
    {
        public ushort VendorId;
        public ushort ProductId;
        public ushort RevisionNumber;
        public NativeGameInputUsage Usage;
        public NativeGameInputVersion HardwareVersion;
        public NativeGameInputVersion FirmwareVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)] public byte[] DeviceId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)] public byte[] DeviceRootId;
        public int DeviceFamily;
        public int SupportedInput;
        public int SupportedRumbleMotors;
        public int SupportedSystemButtons;
        public Guid ContainerId;
        public IntPtr DisplayName;
        public IntPtr PnpPath;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GameInputDeviceCallback(ulong callbackToken, IntPtr context, [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, ulong timestamp, int currentStatus, int previousStatus);

    [ComImport]
    [Guid("20EFC1C7-5D9A-43BA-B26F-B807FA48609C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGameInput
    {
        [PreserveSig] ulong GetCurrentTimestamp();
        [PreserveSig] int GetCurrentReading(int inputKind, [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, out IntPtr reading);
        [PreserveSig] int GetNextReading(IntPtr referenceReading, int inputKind, [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, out IntPtr reading);
        [PreserveSig] int GetPreviousReading(IntPtr referenceReading, int inputKind, [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, out IntPtr reading);
        [PreserveSig] int RegisterReadingCallback([MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, int inputKind, IntPtr context, IntPtr callbackFunc, out ulong callbackToken);
        [PreserveSig] int RegisterDeviceCallback([MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, int inputKind, int statusFilter, int enumerationKind, IntPtr context, [MarshalAs(UnmanagedType.FunctionPtr)] GameInputDeviceCallback callbackFunc, out ulong callbackToken);
        [PreserveSig] int RegisterSystemButtonCallback([MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, int buttonFilter, IntPtr context, IntPtr callbackFunc, out ulong callbackToken);
        [PreserveSig] int RegisterKeyboardLayoutCallback([MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device, IntPtr context, IntPtr callbackFunc, out ulong callbackToken);
        [PreserveSig] void StopCallback(ulong callbackToken);
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.I1)]
        bool UnregisterCallback(ulong callbackToken);
    }

    [ComImport]
    [Guid("63E2F38B-A399-4275-8AE7-D4C6E524D12A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGameInputDevice
    {
        [PreserveSig] int GetDeviceInfo(out IntPtr info);
        [PreserveSig] int GetHapticInfo(IntPtr info);
        [PreserveSig] int GetDeviceStatus();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([Out] RawInputDeviceList[]? rawInputDeviceList, ref uint deviceCount, uint size);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", ExactSpelling = true, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfoW(IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("GameInput.dll", ExactSpelling = true)]
    private static extern int GameInputCreate([MarshalAs(UnmanagedType.Interface)] out IGameInput? gameInput);
}
