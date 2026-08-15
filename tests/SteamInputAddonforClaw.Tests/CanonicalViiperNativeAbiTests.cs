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
            "SetSteamControllerOutputCallback", "RemoveSteamControllerDevice", "RemoveSteamControllerDeviceEx",
            "CreateSteamDeckDevice", "SetSteamDeckDeviceState", "SetSteamDeckOutputCallback",
            "RemoveSteamDeckDevice", "RemoveSteamDeckDeviceEx"
        };

        Assert.Equal(expected, CanonicalViiperNativeApi.RequiredExports);
        Assert.DoesNotContain(CanonicalViiperNativeApi.RequiredExports, name => name.StartsWith("viiper_", StringComparison.Ordinal));
    }

    // Steam Deck ABI (VIIPER main@ec64282c69e5587466b950332d7983fd53a7d778, PR #16). Field order,
    // widths, and offsets below are copied directly from the generated dist/libVIIPER/libVIIPER.h
    // SteamDeckDeviceState struct produced by `just build-libVIIPER Release` at that commit -- not
    // from memory. See src/SteamInputAddonforClaw/Dependencies/Viiper/PROVENANCE.md.
    [Fact]
    public void SteamDeckDeviceState_HasCanonicalSizeAndOffsets()
    {
        Assert.Equal(76, Marshal.SizeOf<SteamDeckDeviceState>());

        var expected = new Dictionary<string, int>
        {
            ["A"] = 0, ["X"] = 1, ["B"] = 2, ["Y"] = 3,
            ["L1"] = 4, ["R1"] = 5,
            ["L2Digital"] = 6, ["R2Digital"] = 7,
            ["L5"] = 8, ["Menu"] = 9,
            ["Steam"] = 10, ["Options"] = 11,
            ["DPadDown"] = 12, ["DPadLeft"] = 13, ["DPadRight"] = 14, ["DPadUp"] = 15,
            ["L3"] = 16,
            ["RPadTouch"] = 17, ["LPadTouch"] = 18, ["RPadPress"] = 19, ["LPadPress"] = 20,
            ["R5"] = 21,
            ["R3"] = 22,
            ["RStickTouch"] = 23, ["LStickTouch"] = 24,
            ["R4"] = 25, ["L4"] = 26,
            ["QuickAccess"] = 27,
            ["LPadX"] = 28, ["LPadY"] = 30,
            ["RPadX"] = 32, ["RPadY"] = 34,
            ["AccelX"] = 36, ["AccelY"] = 38, ["AccelZ"] = 40,
            ["Pitch"] = 42, ["Yaw"] = 44, ["Roll"] = 46,
            ["GyroQuatW"] = 48, ["GyroQuatX"] = 50, ["GyroQuatY"] = 52, ["GyroQuatZ"] = 54,
            ["LTrigger"] = 56, ["RTrigger"] = 58,
            ["LStickX"] = 60, ["LStickY"] = 62,
            ["RStickX"] = 64, ["RStickY"] = 66,
            ["LPadForce"] = 68, ["RPadForce"] = 70,
            ["LStickForce"] = 72, ["RStickForce"] = 74
        };

        foreach (var (name, offset) in expected)
            Assert.Equal(offset, Marshal.OffsetOf<SteamDeckDeviceState>(name).ToInt32());
    }

    [Fact]
    public void SteamDeckDeviceState_CriticalOffsetsMatchGeneratedAbi()
    {
        // Subset called out explicitly by the SD2 work order, independent of the full-struct test
        // above, so a future field reorder that happens to keep the full test passing still fails
        // loudly on the fields the mapper actually depends on.
        Assert.Equal(6, Marshal.OffsetOf<SteamDeckDeviceState>("L2Digital").ToInt32());
        Assert.Equal(7, Marshal.OffsetOf<SteamDeckDeviceState>("R2Digital").ToInt32());
        Assert.Equal(22, Marshal.OffsetOf<SteamDeckDeviceState>("R3").ToInt32());
        Assert.Equal(27, Marshal.OffsetOf<SteamDeckDeviceState>("QuickAccess").ToInt32());
        Assert.Equal(28, Marshal.OffsetOf<SteamDeckDeviceState>("LPadX").ToInt32());
        Assert.Equal(36, Marshal.OffsetOf<SteamDeckDeviceState>("AccelX").ToInt32());
        Assert.Equal(56, Marshal.OffsetOf<SteamDeckDeviceState>("LTrigger").ToInt32());
        Assert.Equal(60, Marshal.OffsetOf<SteamDeckDeviceState>("LStickX").ToInt32());
        Assert.Equal(64, Marshal.OffsetOf<SteamDeckDeviceState>("RStickX").ToInt32());
    }

    [Fact]
    public void SteamDeckDeviceState_DoesNotExposeAFrameField()
    {
        // Frame ownership remains inside device/steamdeck; the external caller never owns or
        // increments the report frame counter (docs/VIIPER_INTEGRATION.md section 5.1).
        Assert.Null(typeof(SteamDeckDeviceState).GetField("Frame", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void SteamDeckDeviceRemoveResult_IsFourByteEnum()
    {
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(SteamDeckDeviceRemoveResult)));
        Assert.Equal(4, Marshal.SizeOf<int>());
        Assert.Equal(0, (int)SteamDeckDeviceRemoveResult.Success);
        Assert.Equal(1, (int)SteamDeckDeviceRemoveResult.RetryableFailure);
        Assert.Equal(2, (int)SteamDeckDeviceRemoveResult.UnsafeOutcomeUnknown);
        Assert.Equal(3, (int)SteamDeckDeviceRemoveResult.Invalid);
    }

    [Fact]
    public void SteamDeckDelegates_UseCdeclAndValidatedNativeReturnWidths()
    {
        AssertCdecl("CreateSteamDeckDeviceDelegate", typeof(byte));
        AssertCdecl("SetSteamDeckDeviceStateDelegate", typeof(byte));
        AssertCdecl("SetSteamDeckOutputCallbackDelegate", typeof(byte));
        AssertCdecl("RemoveSteamDeckDeviceDelegate", typeof(byte));
        AssertCdecl("RemoveSteamDeckDeviceExDelegate", typeof(SteamDeckDeviceRemoveResult));
    }

    [Fact]
    public void SteamDeckDelegates_HaveTheExpectedParameterSignatures()
    {
        AssertParameters("CreateSteamDeckDeviceDelegate", typeof(nuint), typeof(nuint).MakeByRefType(), typeof(uint), typeof(byte), typeof(ushort), typeof(ushort));
        AssertParameters("SetSteamDeckDeviceStateDelegate", typeof(nuint), typeof(SteamDeckDeviceState));
        AssertParameters("SetSteamDeckOutputCallbackDelegate", typeof(nuint), typeof(SteamDeckOutputCallback));
        AssertParameters("RemoveSteamDeckDeviceDelegate", typeof(nuint));
        AssertParameters("RemoveSteamDeckDeviceExDelegate", typeof(nuint));
    }

    [Fact]
    public void SteamDeckOutputCallback_IsCdeclAndHasTheExpectedParameterSignature()
    {
        Assert.Equal(CallingConvention.Cdecl, typeof(SteamDeckOutputCallback).GetCustomAttribute<UnmanagedFunctionPointerAttribute>()!.CallingConvention);

        var parameters = typeof(SteamDeckOutputCallback).GetMethod("Invoke")!.GetParameters();
        Assert.Equal(typeof(nuint), parameters[0].ParameterType);
        Assert.Equal(typeof(nint), parameters[1].ParameterType);
        Assert.Equal(typeof(uint), parameters[2].ParameterType);
    }

    [Fact]
    public void SteamDeckOutputCallback_IsADistinctDelegateTypeFromSteamControllerOutputCallback()
    {
        Assert.NotEqual(typeof(SteamControllerOutputCallback), typeof(SteamDeckOutputCallback));
    }

    private static void AssertCdecl(string delegateName, Type expectedReturnType)
    {
        var type = NestedDelegate(delegateName);
        var convention = type.GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
        Assert.NotNull(convention);
        Assert.Equal(CallingConvention.Cdecl, convention!.CallingConvention);
        Assert.Equal(expectedReturnType, type.GetMethod("Invoke")!.ReturnType);
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
            else if (type.Name == "RemoveSteamDeckDeviceExDelegate")
                Assert.Equal(typeof(SteamDeckDeviceRemoveResult), returnType);
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

    [Fact]
    public void SteamDeckCallback_CanBeRegisteredAndIsRooted()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamDeckDevice(1, out var deviceHandle, 1, false, 0, 0));
        var weak = RegisterSteamDeckOutputCallback(api, deviceHandle);

        CollectGarbage();

        Assert.True(weak.TryGetTarget(out _));
    }

    [Fact]
    public void SteamDeckCallback_ClearingWithNullReleasesTheRootUnderNativeSuccess()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamDeckDevice(1, out var deviceHandle, 1, false, 0, 0));
        var weak = RegisterSteamDeckOutputCallback(api, deviceHandle);

        Assert.True(api.SetSteamDeckOutputCallback(deviceHandle, null));
        CollectGarbage();

        Assert.False(weak.TryGetTarget(out _));
    }

    [Fact]
    public void SteamDeckCallback_RegistrationFailureDoesNotCreateAFalseRoot()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamDeckDevice(1, out var deviceHandle, 1, false, 0, 0));

        FakeExports.FailSetDeckCallback = true;
        try
        {
            var weak = AttemptAndDiscardSteamDeckOutputCallback(api, deviceHandle);
            CollectGarbage();

            Assert.False(weak.TryGetTarget(out _));
        }
        finally
        {
            FakeExports.FailSetDeckCallback = false;
        }
    }

    // Isolated in its own (non-inlined) method so the only strong reference to the callback is a
    // local that goes out of scope when this method returns, before the caller runs GC.Collect() --
    // matching RegisterOutputCallback/RegisterSteamDeckOutputCallback's isolation, which the
    // GC-rooting assertions throughout this file rely on to be reliable.
    private static WeakReference<SteamDeckOutputCallback> AttemptAndDiscardSteamDeckOutputCallback(CanonicalViiperNativeApi api, nuint deviceHandle)
    {
        var marker = new object();
        var callback = new SteamDeckOutputCallback((_, _, _) => GC.KeepAlive(marker));
        var weak = new WeakReference<SteamDeckOutputCallback>(callback);
        Assert.False(api.SetSteamDeckOutputCallback(deviceHandle, callback));
        return weak;
    }

    [Fact]
    public void SteamDeckCallback_ClearingFailureDoesNotIncorrectlyClaimSafeCleanup()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamDeckDevice(1, out var deviceHandle, 1, false, 0, 0));
        var weak = RegisterSteamDeckOutputCallback(api, deviceHandle);

        FakeExports.FailSetDeckCallback = true;
        try
        {
            Assert.False(api.SetSteamDeckOutputCallback(deviceHandle, null));
            CollectGarbage();

            // The native clear was rejected, so the managed root must still be intact: a caller
            // that (incorrectly) treated the false return as "safely cleared" would otherwise be
            // exposed to a dangling expectation that VIIPER can no longer invoke the callback.
            Assert.True(weak.TryGetTarget(out _));
        }
        finally
        {
            FakeExports.FailSetDeckCallback = false;
        }
    }

    [Fact]
    public void SteamDeckCallback_DeviceRemovalExSuccessReleasesTheRoot()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamDeckDevice(1, out var deviceHandle, 1, false, 0, 0));
        var weak = RegisterSteamDeckOutputCallback(api, deviceHandle);

        Assert.Equal(SteamDeckDeviceRemoveResult.Success, api.RemoveSteamDeckDeviceEx(deviceHandle));
        CollectGarbage();

        Assert.False(weak.TryGetTarget(out _));
    }

    [Fact]
    public void SteamDeckCallback_ExNonSuccessRetainsOwnedRoot()
    {
        foreach (var result in new[]
        {
            SteamDeckDeviceRemoveResult.RetryableFailure,
            SteamDeckDeviceRemoveResult.UnsafeOutcomeUnknown,
            SteamDeckDeviceRemoveResult.Invalid
        })
        {
            var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
            Assert.True(api.CreateSteamDeckDevice(1, out var deviceHandle, 1, false, 0, 0));
            var weak = RegisterSteamDeckOutputCallback(api, deviceHandle);
            FakeExports.RemoveDeckDeviceExResult = result;
            try
            {
                Assert.Equal(result, api.RemoveSteamDeckDeviceEx(deviceHandle));
                CollectGarbage();
                Assert.True(weak.TryGetTarget(out _));
            }
            finally
            {
                FakeExports.RemoveDeckDeviceExResult = SteamDeckDeviceRemoveResult.Success;
            }
        }
    }

    [Fact]
    public void SteamDeckCallback_RemoveBusFailureKeepsOwnedRootRooted()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamDeckDevice(1, out var deviceHandle, 1, false, 0, 0));
        var weak = RegisterSteamDeckOutputCallback(api, deviceHandle);

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
    public void SteamDeckCallback_CloseServerFailureKeepsOwnedRootRooted()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamDeckDevice(1, out var deviceHandle, 1, false, 0, 0));
        var weak = RegisterSteamDeckOutputCallback(api, deviceHandle);

        FakeExports.FailCloseServer = true;
        try
        {
            Assert.False(api.CloseUSBServer(1));
            CollectGarbage();
            Assert.True(weak.TryGetTarget(out _));
        }
        finally
        {
            FakeExports.FailCloseServer = false;
        }
    }

    [Fact]
    public void SteamDeckCallback_ConcurrentSetCallsKeepNativeStateAndManagedRootConsistent()
    {
        // Regression test for a race where the native SetSteamDeckOutputCallback call and the
        // managed dictionary mutation were two separate, non-atomic steps: two threads racing to
        // set different callbacks on the same handle could interleave as native<-cbA, native<-cbB,
        // root<-cbB, root<-cbA, leaving native holding cbB's function pointer while the managed
        // dictionary only rooted cbA -- cbB becomes GC-eligible while VIIPER can still invoke it.
        // With the native call and root mutation serialized under one lock, whichever thread's call
        // is the last to complete must be the same call that both "won" the native side (observed
        // here via FakeExports.LastSetDeckCallback, set from inside the locked native call) and the
        // managed root -- run repeatedly with a Barrier to maximize the chance of actual overlap.
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamDeckDevice(1, out var deviceHandle, 1, false, 0, 0));

        var rootsField = typeof(CanonicalViiperNativeApi).GetField("_steamDeckOutputCallbacks", BindingFlags.Instance | BindingFlags.NonPublic)!;

        FakeExports.TrackLastSetDeckCallback = true;
        try
        {
            for (var iteration = 0; iteration < 200; iteration++)
            {
                SteamDeckOutputCallback callbackA = (_, _, _) => { };
                SteamDeckOutputCallback callbackB = (_, _, _) => { };
                var barrier = new Barrier(2);

                var threadA = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    api.SetSteamDeckOutputCallback(deviceHandle, callbackA);
                });
                var threadB = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    api.SetSteamDeckOutputCallback(deviceHandle, callbackB);
                });

                threadA.Start();
                threadB.Start();
                threadA.Join();
                threadB.Join();

                var roots = (Dictionary<nuint, SteamDeckOutputCallback>)rootsField.GetValue(api)!;
                Assert.True(roots.TryGetValue(deviceHandle, out var rootedCallback));
                Assert.Same(FakeExports.LastSetDeckCallback, rootedCallback);
            }
        }
        finally
        {
            FakeExports.TrackLastSetDeckCallback = false;
            FakeExports.LastSetDeckCallback = null;
        }
    }

    [Fact]
    public void SteamDeckCallback_ServerCloseReleasesOwnedRootsAndControllerCallbacksAreUnaffectedByDeckLifecycle()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamControllerDevice(1, out var controllerHandle, 1, false, 0, 0));
        var controllerWeak = RegisterOutputCallback(api, controllerHandle);
        Assert.True(api.CreateSteamDeckDevice(1, out var deckHandle, 1, false, 0, 0));
        var deckWeak = RegisterSteamDeckOutputCallback(api, deckHandle);

        Assert.NotEqual(controllerHandle, deckHandle);

        Assert.True(api.CloseUSBServer(1));
        CollectGarbage();

        Assert.False(controllerWeak.TryGetTarget(out _));
        Assert.False(deckWeak.TryGetTarget(out _));
    }

    [Fact]
    public void SteamDeckAndSteamControllerCallbacks_CannotOverwriteEachOthersRoots()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamControllerDevice(1, out var controllerHandle, 1, false, 0, 0));
        var controllerWeak = RegisterOutputCallback(api, controllerHandle);
        Assert.True(api.CreateSteamDeckDevice(1, out var deckHandle, 1, false, 0, 0));
        var deckWeak = RegisterSteamDeckOutputCallback(api, deckHandle);

        // Removing only the Steam Controller device must not disturb the independently-rooted
        // Steam Deck callback, proving the two callback dictionaries are not aliased.
        Assert.Equal(SteamControllerDeviceRemoveResult.Success, api.RemoveSteamControllerDeviceEx(controllerHandle));
        CollectGarbage();

        Assert.False(controllerWeak.TryGetTarget(out _));
        Assert.True(deckWeak.TryGetTarget(out _));
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

    private static WeakReference<SteamDeckOutputCallback> RegisterSteamDeckOutputCallback(CanonicalViiperNativeApi api, nuint deviceHandle)
    {
        var marker = new object();
        var callback = new SteamDeckOutputCallback((_, _, _) => GC.KeepAlive(marker));
        var weak = new WeakReference<SteamDeckOutputCallback>(callback);
        Assert.True(api.SetSteamDeckOutputCallback(deviceHandle, callback));
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

        // Steam Deck surface: stubbed only so CanonicalViiperNativeApi's constructor (which binds
        // every required export, Gordon and Steam Deck alike) can resolve successfully in these
        // Gordon-focused fake-native tests. Not exercised by assertions in this file -- see
        // CanonicalSteamDeckSessionTests for the Steam Deck lifecycle fake.
        private static readonly CanonicalViiperNativeApi.CreateSteamDeckDeviceDelegate CreateDeckDevice = CreateDeckDeviceImpl;
        private static readonly CanonicalViiperNativeApi.SetSteamDeckDeviceStateDelegate SetDeckState = SetDeckStateImpl;
        private static readonly CanonicalViiperNativeApi.SetSteamDeckOutputCallbackDelegate SetDeckCallback = SetDeckCallbackImpl;
        private static readonly CanonicalViiperNativeApi.RemoveSteamDeckDeviceDelegate RemoveDeckDevice = RemoveDeckDeviceImpl;
        private static readonly CanonicalViiperNativeApi.RemoveSteamDeckDeviceExDelegate RemoveDeckDeviceEx = RemoveDeckDeviceExImpl;

        [ThreadStatic]
        private static bool _failCloseServer;

        [ThreadStatic]
        private static bool _failRemoveBus;

        [ThreadStatic]
        private static SteamControllerDeviceRemoveResult _removeDeviceExResult;

        [ThreadStatic]
        private static SteamDeckDeviceRemoveResult _removeDeckDeviceExResult;

        [ThreadStatic]
        private static bool _failSetDeckCallback;

        internal static SteamControllerDeviceRemoveResult RemoveDeviceExResult
        {
            get => _removeDeviceExResult;
            set => _removeDeviceExResult = value;
        }

        internal static SteamDeckDeviceRemoveResult RemoveDeckDeviceExResult
        {
            get => _removeDeckDeviceExResult;
            set => _removeDeckDeviceExResult = value;
        }

        internal static bool FailSetDeckCallback
        {
            get => _failSetDeckCallback;
            set => _failSetDeckCallback = value;
        }

        // Only tracked while TrackLastSetDeckCallback is explicitly enabled (by the concurrency
        // regression test below) -- otherwise this static field would itself become an unintended
        // extra GC root for every other test's registered callback, defeating the very
        // root-released-after-teardown assertions those tests make. Deliberately NOT [ThreadStatic]:
        // it mirrors VIIPER's single native callback slot per device handle, shared across whichever
        // managed thread last successfully set it.
        internal static bool TrackLastSetDeckCallback;
        internal static SteamDeckOutputCallback? LastSetDeckCallback;

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
            ["RemoveSteamControllerDeviceEx"] = Marshal.GetFunctionPointerForDelegate(RemoveDeviceEx),
            ["CreateSteamDeckDevice"] = Marshal.GetFunctionPointerForDelegate(CreateDeckDevice),
            ["SetSteamDeckDeviceState"] = Marshal.GetFunctionPointerForDelegate(SetDeckState),
            ["SetSteamDeckOutputCallback"] = Marshal.GetFunctionPointerForDelegate(SetDeckCallback),
            ["RemoveSteamDeckDevice"] = Marshal.GetFunctionPointerForDelegate(RemoveDeckDevice),
            ["RemoveSteamDeckDeviceEx"] = Marshal.GetFunctionPointerForDelegate(RemoveDeckDeviceEx)
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

        private static byte CreateDeckDeviceImpl(nuint _, out nuint handle, uint __, byte ___, ushort ____, ushort _____)
        {
            handle = 3;
            return 1;
        }

        private static byte SetDeckStateImpl(nuint _, SteamDeckDeviceState __) => 1;

        private static byte SetDeckCallbackImpl(nuint _, SteamDeckOutputCallback? __)
        {
            if (FailSetDeckCallback) return 0;
            if (TrackLastSetDeckCallback) LastSetDeckCallback = __;
            return 1;
        }

        private static byte RemoveDeckDeviceImpl(nuint _) => 1;

        private static SteamDeckDeviceRemoveResult RemoveDeckDeviceExImpl(nuint _) => RemoveDeckDeviceExResult;
    }
}
