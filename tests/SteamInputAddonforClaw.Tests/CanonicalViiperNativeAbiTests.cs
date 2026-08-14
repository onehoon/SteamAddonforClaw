using System.Reflection;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CanonicalViiperNativeAbiTests
{
    private static readonly string[] StateByteFields =
    [
        "A", "X", "B", "Y", "L1", "R1", "L2", "R2", "Menu", "Steam", "Options",
        "DPadDown", "DPadLeft", "DPadRight", "DPadUp", "L3", "LGrip", "RGrip",
        "LPadTouch", "RPadTouch", "LPadPress", "RPadPress", "LPadAndStick"
    ];

    [Fact]
    public void SteamControllerDeviceState_HasCanonicalSizeAndOffsets()
    {
        Assert.Equal(62, Marshal.SizeOf<SteamControllerDeviceState>());

        var expected = new Dictionary<string, int>
        {
            ["A"] = 0, ["X"] = 1, ["B"] = 2, ["Y"] = 3,
            ["L1"] = 4, ["R1"] = 5, ["L2"] = 6, ["R2"] = 7,
            ["Menu"] = 8, ["Steam"] = 9, ["Options"] = 10,
            ["DPadDown"] = 11, ["DPadLeft"] = 12, ["DPadRight"] = 13, ["DPadUp"] = 14,
            ["L3"] = 15, ["LGrip"] = 16, ["RGrip"] = 17,
            ["LPadTouch"] = 18, ["RPadTouch"] = 19, ["LPadPress"] = 20, ["RPadPress"] = 21, ["LPadAndStick"] = 22,
            ["LPadX"] = 24, ["LPadY"] = 26, ["RPadX"] = 28, ["RPadY"] = 30,
            ["LTrigger"] = 32, ["RTrigger"] = 34, ["LStickX"] = 36, ["LStickY"] = 38,
            ["AccelX"] = 40, ["AccelY"] = 42, ["AccelZ"] = 44,
            ["GyroX"] = 46, ["GyroY"] = 48, ["GyroZ"] = 50,
            ["GyroQuatW"] = 52, ["GyroQuatX"] = 54, ["GyroQuatY"] = 56, ["GyroQuatZ"] = 58,
            ["BatteryMilliVolts"] = 60
        };

        foreach (var (name, offset) in expected)
            Assert.Equal(offset, Marshal.OffsetOf<SteamControllerDeviceState>(name).ToInt32());
    }

    [Fact]
    public void SteamControllerDeviceState_LogicalFieldsAreByteWidth()
    {
        foreach (var name in StateByteFields)
            Assert.Equal(typeof(byte), typeof(SteamControllerDeviceState).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType);
    }

    [Fact]
    public void USBServerConfig_HasCanonicalX64Layout()
    {
        Assert.Equal(32, Marshal.SizeOf<USBServerConfig>());
        Assert.Equal(0, Marshal.OffsetOf<USBServerConfig>(nameof(USBServerConfig.Addr)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<USBServerConfig>(nameof(USBServerConfig.ConnectionTimeoutMs)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<USBServerConfig>(nameof(USBServerConfig.DeviceHandlerConnectTimeoutMs)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<USBServerConfig>(nameof(USBServerConfig.WriteBatchFlushIntervalMs)).ToInt32());
    }

    [Fact]
    public void RequiredExports_PinTheCanonicalGordonSurface()
    {
        var expected = new[]
        {
            "NewUSBServer", "CloseUSBServer", "CreateUSBBus", "RemoveUSBBus",
            "GetUSBDeviceIdentity", "AttachUSBDevice", "DetachUSBDevice",
            "CreateSteamControllerDevice", "SetSteamControllerDeviceState",
            "SetSteamControllerOutputCallback", "RemoveSteamControllerDevice", "RemoveSteamControllerDeviceEx"
        };

        Assert.Equal(expected, CanonicalViiperNativeApi.RequiredExports);
        Assert.DoesNotContain(CanonicalViiperNativeApi.RequiredExports, name => name.StartsWith("viiper_", StringComparison.Ordinal));
    }

    [Fact]
    public void CanonicalFunctionDelegatesUseCdeclAndValidatedNativeReturnWidths()
    {
        var delegateTypes = typeof(CanonicalViiperNativeApi)
            .GetNestedTypes(BindingFlags.NonPublic)
            .Where(type => typeof(Delegate).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(delegateTypes);
        foreach (var type in delegateTypes)
        {
            var convention = type.GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
            Assert.NotNull(convention);
            Assert.Equal(CallingConvention.Cdecl, convention!.CallingConvention);
            var returnType = type.GetMethod("Invoke")!.ReturnType;
            if (type.Name == "RemoveSteamControllerDeviceExDelegate")
                Assert.Equal(typeof(SteamControllerDeviceRemoveResult), returnType);
            else
                Assert.Equal(typeof(byte), returnType);
        }

        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(SteamControllerDeviceRemoveResult)));
        Assert.Equal(4, Marshal.SizeOf<int>());
        Assert.Equal(0, (int)SteamControllerDeviceRemoveResult.Success);
        Assert.Equal(1, (int)SteamControllerDeviceRemoveResult.RetryableFailure);
        Assert.Equal(2, (int)SteamControllerDeviceRemoveResult.UnsafeOutcomeUnknown);
        Assert.Equal(3, (int)SteamControllerDeviceRemoveResult.Invalid);

        Assert.Equal(CallingConvention.Cdecl, typeof(ViiperLogCallback).GetCustomAttribute<UnmanagedFunctionPointerAttribute>()!.CallingConvention);
        Assert.Equal(CallingConvention.Cdecl, typeof(SteamControllerOutputCallback).GetCustomAttribute<UnmanagedFunctionPointerAttribute>()!.CallingConvention);
    }

    [Fact]
    public void CanonicalDelegatesUsePointerSizedHandlesAndStateByValue()
    {
        var create = NestedDelegate("CreateSteamControllerDeviceDelegate").GetMethod("Invoke")!.GetParameters();
        Assert.Equal(typeof(nuint), create[0].ParameterType);
        Assert.Equal(typeof(nuint).MakeByRefType(), create[1].ParameterType);
        Assert.Equal(typeof(uint), create[2].ParameterType);
        Assert.Equal(typeof(byte), create[3].ParameterType);

        var setState = NestedDelegate("SetSteamControllerDeviceStateDelegate").GetMethod("Invoke")!.GetParameters();
        Assert.Equal(typeof(nuint), setState[0].ParameterType);
        Assert.Equal(typeof(SteamControllerDeviceState), setState[1].ParameterType);
        Assert.False(setState[1].ParameterType.IsByRef);

        var callback = typeof(SteamControllerOutputCallback).GetMethod("Invoke")!.GetParameters();
        Assert.Equal(typeof(nuint), callback[0].ParameterType);
        Assert.Equal(typeof(nint), callback[1].ParameterType);
        Assert.Equal(typeof(uint), callback[2].ParameterType);
    }

    [Fact]
    public void CanonicalDelegatesHaveTheExpectedParameterSignatures()
    {
        AssertParameters("NewUsbServerDelegate", typeof(USBServerConfig).MakeByRefType(), typeof(nuint).MakeByRefType(), typeof(ViiperLogCallback));
        AssertParameters("CloseUsbServerDelegate", typeof(nuint));
        AssertParameters("CreateUsbBusDelegate", typeof(nuint), typeof(uint).MakeByRefType());
        AssertParameters("RemoveUsbBusDelegate", typeof(nuint), typeof(uint));
        AssertParameters("GetUsbDeviceIdentityDelegate", typeof(nuint), typeof(uint).MakeByRefType(), typeof(uint).MakeByRefType());
        AssertParameters("AttachUsbDeviceDelegate", typeof(nuint));
        AssertParameters("DetachUsbDeviceDelegate", typeof(nuint));
        AssertParameters("CreateSteamControllerDeviceDelegate", typeof(nuint), typeof(nuint).MakeByRefType(), typeof(uint), typeof(byte), typeof(ushort), typeof(ushort));
        AssertParameters("SetSteamControllerDeviceStateDelegate", typeof(nuint), typeof(SteamControllerDeviceState));
        AssertParameters("SetSteamControllerOutputCallbackDelegate", typeof(nuint), typeof(SteamControllerOutputCallback));
        AssertParameters("RemoveSteamControllerDeviceDelegate", typeof(nuint));
        AssertParameters("RemoveSteamControllerDeviceExDelegate", typeof(nuint));
    }

    [Fact]
    public void CanonicalApi_RootsRegisteredOutputCallback()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        var weak = RegisterOutputCallback(api, 42);

        CollectGarbage();

        Assert.True(weak.TryGetTarget(out _));
    }

    [Fact]
    public void CanonicalApi_RemovingBusReleasesOwnedOutputCallbacks()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamControllerDevice(1, out var deviceHandle, 1, false, 0, 0));
        var weak = RegisterOutputCallback(api, deviceHandle);

        Assert.True(api.RemoveUSBBus(1, 1));
        CollectGarbage();

        Assert.False(weak.TryGetTarget(out _));
    }

    [Fact]
    public void CanonicalApi_ClosingServerReleasesOwnedOutputAndLogCallbacks()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        var logWeak = RegisterLogCallback(api);
        Assert.True(api.CreateSteamControllerDevice(1, out var deviceHandle, 1, false, 0, 0));
        var outputWeak = RegisterOutputCallback(api, deviceHandle);

        Assert.True(api.CloseUSBServer(1));
        CollectGarbage();

        Assert.False(logWeak.TryGetTarget(out _));
        Assert.False(outputWeak.TryGetTarget(out _));
    }

    [Fact]
    public void CanonicalApi_RemoveBusFailureKeepsOwnedOutputCallbackRooted()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamControllerDevice(1, out var deviceHandle, 1, false, 0, 0));
        var weak = RegisterOutputCallback(api, deviceHandle);

        FakeExports.FailRemoveBus = true;
        try
        {
            Assert.False(api.RemoveUSBBus(1, 1));
            CollectGarbage();
            Assert.True(weak.TryGetTarget(out _));
        }
        finally
        {
            FakeExports.FailRemoveBus = false;
        }
    }

    [Fact]
    public void CanonicalApi_CloseServerFailureKeepsOutputAndLogCallbacksRooted()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        var logWeak = RegisterLogCallback(api);
        Assert.True(api.CreateSteamControllerDevice(1, out var deviceHandle, 1, false, 0, 0));
        var outputWeak = RegisterOutputCallback(api, deviceHandle);

        FakeExports.FailCloseServer = true;
        try
        {
            Assert.False(api.CloseUSBServer(1));
            CollectGarbage();
            Assert.True(logWeak.TryGetTarget(out _));
            Assert.True(outputWeak.TryGetTarget(out _));
        }
        finally
        {
            FakeExports.FailCloseServer = false;
        }
    }

    [Fact]
    public void CanonicalApi_ExSuccessReleasesOwnedOutputCallback()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamControllerDevice(1, out var deviceHandle, 1, false, 0, 0));
        var weak = RegisterOutputCallback(api, deviceHandle);

        Assert.Equal(SteamControllerDeviceRemoveResult.Success, api.RemoveSteamControllerDeviceEx(deviceHandle));
        CollectGarbage();

        Assert.False(weak.TryGetTarget(out _));
    }

    [Fact]
    public void CanonicalApi_ExNonSuccessRetainsOwnedOutputCallback()
    {
        foreach (var result in new[]
        {
            SteamControllerDeviceRemoveResult.RetryableFailure,
            SteamControllerDeviceRemoveResult.UnsafeOutcomeUnknown,
            SteamControllerDeviceRemoveResult.Invalid
        })
        {
            var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
            Assert.True(api.CreateSteamControllerDevice(1, out var deviceHandle, 1, false, 0, 0));
            var weak = RegisterOutputCallback(api, deviceHandle);
            FakeExports.RemoveDeviceExResult = result;
            try
            {
                Assert.Equal(result, api.RemoveSteamControllerDeviceEx(deviceHandle));
                CollectGarbage();
                Assert.True(weak.TryGetTarget(out _));
            }
            finally
            {
                FakeExports.RemoveDeviceExResult = SteamControllerDeviceRemoveResult.Success;
            }
        }
    }

    [Fact]
    public void CanonicalLoad_RejectsRelativePaths()
    {
        Assert.Throws<ArgumentException>(() => CanonicalViiperNativeApi.Load("libVIIPER.dll"));
    }

    private static void AssertParameters(string delegateName, params Type[] expected)
    {
        var actual = NestedDelegate(delegateName).GetMethod("Invoke")!.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Assert.Equal(expected, actual);
    }

    private static WeakReference<SteamControllerOutputCallback> RegisterOutputCallback(CanonicalViiperNativeApi api, nuint deviceHandle)
    {
        var marker = new object();
        var callback = new SteamControllerOutputCallback((_, _, _) => GC.KeepAlive(marker));
        var weak = new WeakReference<SteamControllerOutputCallback>(callback);
        Assert.True(api.SetSteamControllerOutputCallback(deviceHandle, callback));
        return weak;
    }

    private static WeakReference<ViiperLogCallback> RegisterLogCallback(CanonicalViiperNativeApi api)
    {
        var marker = new object();
        var callback = new ViiperLogCallback((_, _) => GC.KeepAlive(marker));
        var weak = new WeakReference<ViiperLogCallback>(callback);
        var config = default(USBServerConfig);
        Assert.True(api.NewUSBServer(ref config, out _, callback));
        return weak;
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static Type NestedDelegate(string name) => typeof(CanonicalViiperNativeApi).GetNestedType(name, BindingFlags.NonPublic)!;

    private static class FakeExports
    {
        private static readonly CanonicalViiperNativeApi.NewUsbServerDelegate NewServer = NewServerImpl;
        private static readonly CanonicalViiperNativeApi.CloseUsbServerDelegate CloseServer = CloseServerImpl;
        private static readonly CanonicalViiperNativeApi.CreateUsbBusDelegate CreateBus = CreateBusImpl;
        private static readonly CanonicalViiperNativeApi.RemoveUsbBusDelegate RemoveBus = RemoveBusImpl;
        private static readonly CanonicalViiperNativeApi.GetUsbDeviceIdentityDelegate Identity = IdentityImpl;
        private static readonly CanonicalViiperNativeApi.AttachUsbDeviceDelegate Attach = AttachImpl;
        private static readonly CanonicalViiperNativeApi.DetachUsbDeviceDelegate Detach = DetachImpl;
        private static readonly CanonicalViiperNativeApi.CreateSteamControllerDeviceDelegate CreateDevice = CreateDeviceImpl;
        private static readonly CanonicalViiperNativeApi.SetSteamControllerDeviceStateDelegate SetState = SetStateImpl;
        private static readonly CanonicalViiperNativeApi.SetSteamControllerOutputCallbackDelegate SetCallback = SetCallbackImpl;
        private static readonly CanonicalViiperNativeApi.RemoveSteamControllerDeviceDelegate RemoveDevice = RemoveDeviceImpl;
        private static readonly CanonicalViiperNativeApi.RemoveSteamControllerDeviceExDelegate RemoveDeviceEx = RemoveDeviceExImpl;

        [ThreadStatic]
        private static bool _failCloseServer;

        [ThreadStatic]
        private static bool _failRemoveBus;

        [ThreadStatic]
        private static SteamControllerDeviceRemoveResult _removeDeviceExResult;

        internal static SteamControllerDeviceRemoveResult RemoveDeviceExResult
        {
            get => _removeDeviceExResult;
            set => _removeDeviceExResult = value;
        }

        internal static bool FailCloseServer
        {
            get => _failCloseServer;
            set => _failCloseServer = value;
        }

        internal static bool FailRemoveBus
        {
            get => _failRemoveBus;
            set => _failRemoveBus = value;
        }

        private static readonly Dictionary<string, nint> Pointers = new(StringComparer.Ordinal)
        {
            ["NewUSBServer"] = Marshal.GetFunctionPointerForDelegate(NewServer),
            ["CloseUSBServer"] = Marshal.GetFunctionPointerForDelegate(CloseServer),
            ["CreateUSBBus"] = Marshal.GetFunctionPointerForDelegate(CreateBus),
            ["RemoveUSBBus"] = Marshal.GetFunctionPointerForDelegate(RemoveBus),
            ["GetUSBDeviceIdentity"] = Marshal.GetFunctionPointerForDelegate(Identity),
            ["AttachUSBDevice"] = Marshal.GetFunctionPointerForDelegate(Attach),
            ["DetachUSBDevice"] = Marshal.GetFunctionPointerForDelegate(Detach),
            ["CreateSteamControllerDevice"] = Marshal.GetFunctionPointerForDelegate(CreateDevice),
            ["SetSteamControllerDeviceState"] = Marshal.GetFunctionPointerForDelegate(SetState),
            ["SetSteamControllerOutputCallback"] = Marshal.GetFunctionPointerForDelegate(SetCallback),
            ["RemoveSteamControllerDevice"] = Marshal.GetFunctionPointerForDelegate(RemoveDevice),
            ["RemoveSteamControllerDeviceEx"] = Marshal.GetFunctionPointerForDelegate(RemoveDeviceEx)
        };

        internal static nint Resolve(nint _, string name) => Pointers[name];

        private static byte NewServerImpl(ref USBServerConfig _, out nuint handle, ViiperLogCallback? __)
        {
            handle = 1;
            return 1;
        }

        private static byte HandleOnlyImpl(nuint _) => 1;

        private static byte CloseServerImpl(nuint _) => FailCloseServer ? (byte)0 : (byte)1;

        private static byte AttachImpl(nuint _) => 1;

        private static byte DetachImpl(nuint _) => 1;

        private static byte RemoveDeviceImpl(nuint _) => 1;

        private static SteamControllerDeviceRemoveResult RemoveDeviceExImpl(nuint _) => RemoveDeviceExResult;

        private static byte CreateBusImpl(nuint _, ref uint busId)
        {
            busId = busId == 0 ? 1u : busId;
            return 1;
        }

        private static byte RemoveBusImpl(nuint _, uint __) => FailRemoveBus ? (byte)0 : (byte)1;

        private static byte IdentityImpl(nuint _, out uint busId, out uint deviceId)
        {
            busId = 1;
            deviceId = 1;
            return 1;
        }

        private static byte CreateDeviceImpl(nuint _, out nuint handle, uint __, byte ___, ushort ____, ushort _____)
        {
            handle = 2;
            return 1;
        }

        private static byte SetStateImpl(nuint _, SteamControllerDeviceState __) => 1;

        private static byte SetCallbackImpl(nuint _, SteamControllerOutputCallback? __) => 1;

    }
}
