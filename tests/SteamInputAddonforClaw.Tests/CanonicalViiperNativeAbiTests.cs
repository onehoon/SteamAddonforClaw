using System.Reflection;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CanonicalViiperNativeAbiTests
{
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
    public void RequiredExports_PinTheAddonConsumedCanonicalSurface()
    {
        var expected = new[]
        {
            "NewUSBServer", "CloseUSBServer", "CreateUSBBus", "RemoveUSBBus",
            "GetUSBDeviceIdentity", "AttachUSBDevice", "DetachUSBDevice",
            "AttachUSBDeviceEx", "DetachUSBDeviceEx", "GetUSBDeviceAttachmentState",
            "CreateSteamDeckDevice", "SetSteamDeckDeviceState", "SetSteamDeckOutputCallback",
            "RemoveSteamDeckDevice", "RemoveSteamDeckDeviceEx",
            "CreateXbox360Device", "SetXbox360DeviceState", "RemoveXbox360Device", "RemoveXbox360DeviceEx"
        };

        Assert.Equal(expected, CanonicalViiperNativeApi.RequiredExports);
        Assert.DoesNotContain(CanonicalViiperNativeApi.RequiredExports, name => name.StartsWith("viiper_", StringComparison.Ordinal));
        Assert.DoesNotContain("CreateSteamControllerDevice", CanonicalViiperNativeApi.RequiredExports);
        Assert.DoesNotContain("SetSteamControllerDeviceState", CanonicalViiperNativeApi.RequiredExports);
        Assert.DoesNotContain("SetSteamControllerOutputCallback", CanonicalViiperNativeApi.RequiredExports);
        Assert.DoesNotContain("RemoveSteamControllerDevice", CanonicalViiperNativeApi.RequiredExports);
        Assert.DoesNotContain("RemoveSteamControllerDeviceEx", CanonicalViiperNativeApi.RequiredExports);
        // PR1 is buttons/D-pad/sticks/triggers only -- no Xbox360 rumble callback is bound.
        Assert.DoesNotContain("SetXbox360RumbleCallback", CanonicalViiperNativeApi.RequiredExports);
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
        var nonByteReturnTypes = new Dictionary<string, Type>
        {
            ["RemoveSteamDeckDeviceExDelegate"] = typeof(SteamDeckDeviceRemoveResult),
            ["AttachUsbDeviceExDelegate"] = typeof(USBDeviceAttachResult),
            ["DetachUsbDeviceExDelegate"] = typeof(USBDeviceDetachResult),
            ["RemoveXbox360DeviceExDelegate"] = typeof(Xbox360DeviceRemoveResult),
        };

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
            Assert.Equal(nonByteReturnTypes.GetValueOrDefault(type.Name, typeof(byte)), returnType);
        }

        Assert.Equal(CallingConvention.Cdecl, typeof(ViiperLogCallback).GetCustomAttribute<UnmanagedFunctionPointerAttribute>()!.CallingConvention);
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
        AssertParameters("AttachUsbDeviceExDelegate", typeof(nuint));
        AssertParameters("DetachUsbDeviceExDelegate", typeof(nuint));
        AssertParameters("GetUsbDeviceAttachmentStateDelegate", typeof(nuint), typeof(USBDeviceAttachmentState).MakeByRefType());
        AssertParameters("CreateXbox360DeviceDelegate", typeof(nuint), typeof(nuint).MakeByRefType(), typeof(uint), typeof(byte), typeof(ushort), typeof(ushort), typeof(byte));
        AssertParameters("SetXbox360DeviceStateDelegate", typeof(nuint), typeof(Xbox360DeviceState));
        AssertParameters("RemoveXbox360DeviceDelegate", typeof(nuint));
        AssertParameters("RemoveXbox360DeviceExDelegate", typeof(nuint));
    }

    // ---- Classified attachment ABI (USBDeviceAttachResult / USBDeviceDetachResult /
    // USBDeviceAttachmentState) ----

    [Fact]
    public void ClassifiedAttachmentEnums_HaveTheGeneratedNumericLayout()
    {
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(USBDeviceAttachResult)));
        Assert.Equal(0, (int)USBDeviceAttachResult.Success);
        Assert.Equal(1, (int)USBDeviceAttachResult.RetryableFailure);
        Assert.Equal(2, (int)USBDeviceAttachResult.UnsafeOutcomeUnknown);
        Assert.Equal(3, (int)USBDeviceAttachResult.Invalid);

        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(USBDeviceDetachResult)));
        Assert.Equal(0, (int)USBDeviceDetachResult.Success);
        Assert.Equal(1, (int)USBDeviceDetachResult.RetryableFailure);
        Assert.Equal(2, (int)USBDeviceDetachResult.UnsafeOutcomeUnknown);
        Assert.Equal(3, (int)USBDeviceDetachResult.Invalid);

        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(USBDeviceAttachmentState)));
        Assert.Equal(0, (int)USBDeviceAttachmentState.Detached);
        Assert.Equal(1, (int)USBDeviceAttachmentState.Attached);
        Assert.Equal(2, (int)USBDeviceAttachmentState.OutcomeUnknown);
    }

    [Theory]
    [InlineData((int)USBDeviceAttachResult.Success)]
    [InlineData((int)USBDeviceAttachResult.RetryableFailure)]
    [InlineData((int)USBDeviceAttachResult.UnsafeOutcomeUnknown)]
    [InlineData((int)USBDeviceAttachResult.Invalid)]
    public void AttachUSBDeviceEx_ReturnsEveryNativeClassificationUnchanged(int nativeValue)
    {
        var native = (USBDeviceAttachResult)nativeValue;
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        FakeExports.AttachExResult = native;
        Assert.Equal(native, api.AttachUSBDeviceEx(1));
    }

    [Theory]
    [InlineData((int)USBDeviceDetachResult.Success)]
    [InlineData((int)USBDeviceDetachResult.RetryableFailure)]
    [InlineData((int)USBDeviceDetachResult.UnsafeOutcomeUnknown)]
    [InlineData((int)USBDeviceDetachResult.Invalid)]
    public void DetachUSBDeviceEx_ReturnsEveryNativeClassificationUnchanged(int nativeValue)
    {
        var native = (USBDeviceDetachResult)nativeValue;
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        FakeExports.DetachExResult = native;
        Assert.Equal(native, api.DetachUSBDeviceEx(1));
    }

    [Theory]
    [InlineData((int)USBDeviceAttachmentState.Detached)]
    [InlineData((int)USBDeviceAttachmentState.Attached)]
    [InlineData((int)USBDeviceAttachmentState.OutcomeUnknown)]
    public void GetUSBDeviceAttachmentState_MirrorsEveryNativeState(int nativeValue)
    {
        var native = (USBDeviceAttachmentState)nativeValue;
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        FakeExports.AttachmentState = native;
        Assert.True(api.GetUSBDeviceAttachmentState(1, out var state));
        Assert.Equal(native, state);
    }

    [Fact]
    public void GetUSBDeviceAttachmentState_FailedQueryDoesNotClaimSuccess()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        FakeExports.FailGetAttachmentState = true;
        try
        {
            Assert.False(api.GetUSBDeviceAttachmentState(1, out _));
        }
        finally
        {
            FakeExports.FailGetAttachmentState = false;
        }
    }

    // ---- Xbox360 typed ABI (create/state/teardown -- ABI/foundation only, no rumble binding) ----

    [Fact]
    public void CreateXbox360Device_ForwardsAutoAttachVidPidSubtypeUnchanged()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);

        Assert.True(api.CreateXbox360Device(1, out var deviceHandle, 7, true, 0x045E, 0x028E, 1));

        Assert.Equal(30u, deviceHandle);
        Assert.True(FakeExports.LastXbox360AutoAttach);
        Assert.Equal((ushort)0x045E, FakeExports.LastXbox360Vendor);
        Assert.Equal((ushort)0x028E, FakeExports.LastXbox360Product);
        Assert.Equal((byte)1, FakeExports.LastXbox360SubType);
    }

    [Fact]
    public void CreateXbox360Device_FalseAutoAttachIsMarshalledAsZero()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateXbox360Device(1, out _, 7, false, 0, 0, 0));
        Assert.False(FakeExports.LastXbox360AutoAttach);
    }

    [Fact]
    public void CreateXbox360Device_FailureDoesNotRegisterOwnership()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        FakeExports.FailCreateXbox360 = true;
        try
        {
            Assert.False(api.CreateXbox360Device(1, out var deviceHandle, 7, false, 0, 0, 0));
            Assert.Equal((nuint)0, deviceHandle);

            var ownershipField = typeof(CanonicalViiperNativeApi).GetField("_deviceOwnership", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var ownership = (Dictionary<nuint, (nuint ServerHandle, uint BusId)>)ownershipField.GetValue(api)!;
            Assert.Empty(ownership);
        }
        finally
        {
            FakeExports.FailCreateXbox360 = false;
        }
    }

    [Fact]
    public void SetXbox360DeviceState_ForwardsStateUnchanged()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateXbox360Device(1, out var deviceHandle, 7, false, 0, 0, 0));

        var state = new Xbox360DeviceState { Buttons = Xbox360ButtonBits.A, LT = 10, RT = 20, LX = 100, LY = -100, RX = 200, RY = -200 };
        Assert.True(api.SetXbox360DeviceState(deviceHandle, state));
        Assert.Equal(state, FakeExports.LastXbox360State);
    }

    [Fact]
    public void RemoveXbox360Device_SuccessReleasesOwnership()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateXbox360Device(1, out var deviceHandle, 7, false, 0, 0, 0));

        Assert.True(api.RemoveXbox360Device(deviceHandle));
        AssertXbox360OwnershipReleased(api, deviceHandle);
    }

    [Fact]
    public void RemoveXbox360Device_FailureRetainsOwnership()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateXbox360Device(1, out var deviceHandle, 7, false, 0, 0, 0));

        FakeExports.FailRemoveXbox360 = true;
        try
        {
            Assert.False(api.RemoveXbox360Device(deviceHandle));
            AssertXbox360OwnershipRetained(api, deviceHandle);
        }
        finally
        {
            FakeExports.FailRemoveXbox360 = false;
        }
    }

    [Fact]
    public void RemoveXbox360DeviceEx_SuccessReleasesOwnership()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateXbox360Device(1, out var deviceHandle, 7, false, 0, 0, 0));

        Assert.Equal(Xbox360DeviceRemoveResult.Success, api.RemoveXbox360DeviceEx(deviceHandle));
        AssertXbox360OwnershipReleased(api, deviceHandle);
    }

    [Theory]
    [InlineData((int)Xbox360DeviceRemoveResult.RetryableFailure)]
    [InlineData((int)Xbox360DeviceRemoveResult.UnsafeOutcomeUnknown)]
    [InlineData((int)Xbox360DeviceRemoveResult.Invalid)]
    public void RemoveXbox360DeviceEx_NonSuccessRetainsOwnership(int resultValue)
    {
        var result = (Xbox360DeviceRemoveResult)resultValue;
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateXbox360Device(1, out var deviceHandle, 7, false, 0, 0, 0));

        FakeExports.RemoveXbox360ExResult = result;
        try
        {
            Assert.Equal(result, api.RemoveXbox360DeviceEx(deviceHandle));
            AssertXbox360OwnershipRetained(api, deviceHandle);
        }
        finally
        {
            FakeExports.RemoveXbox360ExResult = Xbox360DeviceRemoveResult.Success;
        }
    }

    [Fact]
    public void RemoveUSBBus_SuccessReleasesOwnedXbox360Devices()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateXbox360Device(1, out var deviceHandle, 7, false, 0, 0, 0));

        Assert.True(api.RemoveUSBBus(1, 7));
        AssertXbox360OwnershipReleased(api, deviceHandle);
    }

    [Fact]
    public void RemoveUSBBus_FailureRetainsOwnedXbox360Devices()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateXbox360Device(1, out var deviceHandle, 7, false, 0, 0, 0));

        FakeExports.FailRemoveBus = true;
        try
        {
            Assert.False(api.RemoveUSBBus(1, 7));
            AssertXbox360OwnershipRetained(api, deviceHandle);
        }
        finally
        {
            FakeExports.FailRemoveBus = false;
        }
    }

    [Fact]
    public void CloseUSBServer_SuccessReleasesOwnedXbox360Devices()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateXbox360Device(1, out var deviceHandle, 7, false, 0, 0, 0));

        Assert.True(api.CloseUSBServer(1));
        AssertXbox360OwnershipReleased(api, deviceHandle);
    }

    [Fact]
    public void CloseUSBServer_FailureRetainsOwnedXbox360Devices()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateXbox360Device(1, out var deviceHandle, 7, false, 0, 0, 0));

        FakeExports.FailCloseServer = true;
        try
        {
            Assert.False(api.CloseUSBServer(1));
            AssertXbox360OwnershipRetained(api, deviceHandle);
        }
        finally
        {
            FakeExports.FailCloseServer = false;
        }
    }

    // A released device is no longer tracked, so removing it again reports Success (nothing to
    // clean up) rather than acting on stale ownership evidence.
    private static void AssertXbox360OwnershipReleased(CanonicalViiperNativeApi api, nuint deviceHandle)
    {
        var ownershipField = typeof(CanonicalViiperNativeApi).GetField("_deviceOwnership", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ownership = (Dictionary<nuint, (nuint ServerHandle, uint BusId)>)ownershipField.GetValue(api)!;
        Assert.False(ownership.ContainsKey(deviceHandle));
    }

    private static void AssertXbox360OwnershipRetained(CanonicalViiperNativeApi api, nuint deviceHandle)
    {
        var ownershipField = typeof(CanonicalViiperNativeApi).GetField("_deviceOwnership", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ownership = (Dictionary<nuint, (nuint ServerHandle, uint BusId)>)ownershipField.GetValue(api)!;
        Assert.True(ownership.ContainsKey(deviceHandle));
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
    // matching RegisterSteamDeckOutputCallback's isolation, which the GC-rooting assertions
    // throughout this file rely on to be reliable.
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
    public void SteamDeckCallback_ConcurrentSetCallsCannotEnterTheNativeCallConcurrently()
    {
        // Regression test for a race where the native SetSteamDeckOutputCallback call and the
        // managed dictionary mutation were two separate, non-atomic steps: two threads racing to
        // set different callbacks on the same handle could interleave as native<-cbA, native<-cbB,
        // root<-cbB, root<-cbA, leaving native holding cbB's function pointer while the managed
        // dictionary only rooted cbA -- cbB becomes GC-eligible while VIIPER can still invoke it.
        //
        // A bare "run it N times and hope the scheduler overlaps" test does not actually exercise
        // the race with much confidence -- it can pass 200/200 runs against the old, unlocked
        // implementation if the scheduler happens to run the two calls back-to-back every time.
        // Instead, the fake native call itself detects concurrent entry: it increments a shared
        // depth counter, sleeps briefly to force any unsynchronized second caller into the window,
        // then decrements. A depth > 1 observed here means two callers were inside the "native" call
        // simultaneously, which the fix's lock (_callbackGate) around the native call and the root
        // mutation is meant to make impossible. This is a high-confidence concurrent-entry-detection
        // regression test, not a formal proof -- an OS scheduler could in principle still run one
        // thread's call (Thread.Sleep included) to completion before starting the other, so this
        // cannot guarantee it will always catch a reintroduced race. Also asserts the managed root
        // matches whichever call actually completed last, which the same atomicity guarantees.
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        Assert.True(api.CreateSteamDeckDevice(1, out var deviceHandle, 1, false, 0, 0));

        var rootsField = typeof(CanonicalViiperNativeApi).GetField("_steamDeckOutputCallbacks", BindingFlags.Instance | BindingFlags.NonPublic)!;

        FakeExports.TrackLastSetDeckCallback = true;
        FakeExports.DetectConcurrentSetDeckCallbackEntry = true;
        try
        {
            for (var iteration = 0; iteration < 20; iteration++)
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

                Assert.False(FakeExports.ConcurrentSetDeckCallbackEntryDetected, "Two threads entered the native SetSteamDeckOutputCallback call concurrently -- the native call and the managed root mutation are not serialized.");

                var roots = (Dictionary<nuint, SteamDeckOutputCallback>)rootsField.GetValue(api)!;
                Assert.True(roots.TryGetValue(deviceHandle, out var rootedCallback));
                Assert.Same(FakeExports.LastSetDeckCallback, rootedCallback);
            }
        }
        finally
        {
            FakeExports.TrackLastSetDeckCallback = false;
            FakeExports.LastSetDeckCallback = null;
            FakeExports.DetectConcurrentSetDeckCallbackEntry = false;
            FakeExports.ConcurrentSetDeckCallbackEntryDetected = false;
        }
    }

    [Fact]
    public void SteamDeckCallback_ServerCloseReleasesOwnedRootsAndLogCallback()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        var logWeak = RegisterLogCallback(api);
        Assert.True(api.CreateSteamDeckDevice(1, out var deckHandle, 1, false, 0, 0));
        var deckWeak = RegisterSteamDeckOutputCallback(api, deckHandle);

        Assert.True(api.CloseUSBServer(1));
        CollectGarbage();

        Assert.False(logWeak.TryGetTarget(out _));
        Assert.False(deckWeak.TryGetTarget(out _));
    }

    [Fact]
    public void LogCallback_CloseServerFailureKeepsLogCallbackRooted()
    {
        var api = new CanonicalViiperNativeApi(1, FakeExports.Resolve);
        var logWeak = RegisterLogCallback(api);

        FakeExports.FailCloseServer = true;
        try
        {
            Assert.False(api.CloseUSBServer(1));
            CollectGarbage();
            Assert.True(logWeak.TryGetTarget(out _));
        }
        finally
        {
            FakeExports.FailCloseServer = false;
        }
    }

    private static void AssertParameters(string delegateName, params Type[] expected)
    {
        var actual = NestedDelegate(delegateName).GetMethod("Invoke")!.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Assert.Equal(expected, actual);
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
        private static readonly CanonicalViiperNativeApi.AttachUsbDeviceExDelegate AttachEx = AttachExImpl;
        private static readonly CanonicalViiperNativeApi.DetachUsbDeviceExDelegate DetachEx = DetachExImpl;
        private static readonly CanonicalViiperNativeApi.GetUsbDeviceAttachmentStateDelegate GetAttachmentState = GetAttachmentStateImpl;

        private static readonly CanonicalViiperNativeApi.CreateSteamDeckDeviceDelegate CreateDeckDevice = CreateDeckDeviceImpl;
        private static readonly CanonicalViiperNativeApi.SetSteamDeckDeviceStateDelegate SetDeckState = SetDeckStateImpl;
        private static readonly CanonicalViiperNativeApi.SetSteamDeckOutputCallbackDelegate SetDeckCallback = SetDeckCallbackImpl;
        private static readonly CanonicalViiperNativeApi.RemoveSteamDeckDeviceDelegate RemoveDeckDevice = RemoveDeckDeviceImpl;
        private static readonly CanonicalViiperNativeApi.RemoveSteamDeckDeviceExDelegate RemoveDeckDeviceEx = RemoveDeckDeviceExImpl;

        private static readonly CanonicalViiperNativeApi.CreateXbox360DeviceDelegate CreateXbox360 = CreateXbox360Impl;
        private static readonly CanonicalViiperNativeApi.SetXbox360DeviceStateDelegate SetXbox360State = SetXbox360StateImpl;
        private static readonly CanonicalViiperNativeApi.RemoveXbox360DeviceDelegate RemoveXbox360 = RemoveXbox360Impl;
        private static readonly CanonicalViiperNativeApi.RemoveXbox360DeviceExDelegate RemoveXbox360Ex = RemoveXbox360ExImpl;

        [ThreadStatic]
        private static bool _failCloseServer;

        [ThreadStatic]
        private static bool _failRemoveBus;

        [ThreadStatic]
        private static SteamDeckDeviceRemoveResult _removeDeckDeviceExResult;

        [ThreadStatic]
        private static bool _failSetDeckCallback;

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

        // Deliberately not [ThreadStatic]: used to detect two threads inside the fake
        // SetSteamDeckOutputCallback body at once (see SetDeckCallbackImpl and the concurrency
        // regression test above).
        private static int _concurrentSetDeckCallbackDepth;
        internal static bool DetectConcurrentSetDeckCallbackEntry;
        internal static volatile bool ConcurrentSetDeckCallbackEntryDetected;

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

        [ThreadStatic]
        private static USBDeviceAttachResult _attachExResult;

        [ThreadStatic]
        private static USBDeviceDetachResult _detachExResult;

        [ThreadStatic]
        private static USBDeviceAttachmentState _attachmentState;

        [ThreadStatic]
        private static bool _failGetAttachmentState;

        [ThreadStatic]
        private static bool _failCreateXbox360;

        [ThreadStatic]
        private static bool _failRemoveXbox360;

        [ThreadStatic]
        private static Xbox360DeviceRemoveResult _removeXbox360ExResult;

        internal static USBDeviceAttachResult AttachExResult { get => _attachExResult; set => _attachExResult = value; }
        internal static USBDeviceDetachResult DetachExResult { get => _detachExResult; set => _detachExResult = value; }
        internal static USBDeviceAttachmentState AttachmentState { get => _attachmentState; set => _attachmentState = value; }
        internal static bool FailGetAttachmentState { get => _failGetAttachmentState; set => _failGetAttachmentState = value; }
        internal static bool FailCreateXbox360 { get => _failCreateXbox360; set => _failCreateXbox360 = value; }
        internal static bool FailRemoveXbox360 { get => _failRemoveXbox360; set => _failRemoveXbox360 = value; }
        internal static Xbox360DeviceRemoveResult RemoveXbox360ExResult
        {
            get => _removeXbox360ExResult;
            set => _removeXbox360ExResult = value;
        }

        internal static bool LastXbox360AutoAttach { get; private set; }
        internal static ushort LastXbox360Vendor { get; private set; }
        internal static ushort LastXbox360Product { get; private set; }
        internal static byte LastXbox360SubType { get; private set; }
        internal static Xbox360DeviceState LastXbox360State { get; private set; }

        private static readonly Dictionary<string, nint> Pointers = new(StringComparer.Ordinal)
        {
            ["NewUSBServer"] = Marshal.GetFunctionPointerForDelegate(NewServer),
            ["CloseUSBServer"] = Marshal.GetFunctionPointerForDelegate(CloseServer),
            ["CreateUSBBus"] = Marshal.GetFunctionPointerForDelegate(CreateBus),
            ["RemoveUSBBus"] = Marshal.GetFunctionPointerForDelegate(RemoveBus),
            ["GetUSBDeviceIdentity"] = Marshal.GetFunctionPointerForDelegate(Identity),
            ["AttachUSBDevice"] = Marshal.GetFunctionPointerForDelegate(Attach),
            ["DetachUSBDevice"] = Marshal.GetFunctionPointerForDelegate(Detach),
            ["AttachUSBDeviceEx"] = Marshal.GetFunctionPointerForDelegate(AttachEx),
            ["DetachUSBDeviceEx"] = Marshal.GetFunctionPointerForDelegate(DetachEx),
            ["GetUSBDeviceAttachmentState"] = Marshal.GetFunctionPointerForDelegate(GetAttachmentState),
            ["CreateSteamDeckDevice"] = Marshal.GetFunctionPointerForDelegate(CreateDeckDevice),
            ["SetSteamDeckDeviceState"] = Marshal.GetFunctionPointerForDelegate(SetDeckState),
            ["SetSteamDeckOutputCallback"] = Marshal.GetFunctionPointerForDelegate(SetDeckCallback),
            ["RemoveSteamDeckDevice"] = Marshal.GetFunctionPointerForDelegate(RemoveDeckDevice),
            ["RemoveSteamDeckDeviceEx"] = Marshal.GetFunctionPointerForDelegate(RemoveDeckDeviceEx),
            ["CreateXbox360Device"] = Marshal.GetFunctionPointerForDelegate(CreateXbox360),
            ["SetXbox360DeviceState"] = Marshal.GetFunctionPointerForDelegate(SetXbox360State),
            ["RemoveXbox360Device"] = Marshal.GetFunctionPointerForDelegate(RemoveXbox360),
            ["RemoveXbox360DeviceEx"] = Marshal.GetFunctionPointerForDelegate(RemoveXbox360Ex)
        };

        internal static nint Resolve(nint _, string name) => Pointers[name];

        private static byte NewServerImpl(ref USBServerConfig _, out nuint handle, ViiperLogCallback? __)
        {
            handle = 1;
            return 1;
        }

        private static byte CloseServerImpl(nuint _) => FailCloseServer ? (byte)0 : (byte)1;

        private static byte AttachImpl(nuint _) => 1;

        private static byte DetachImpl(nuint _) => 1;

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

        private static byte CreateDeckDeviceImpl(nuint _, out nuint handle, uint __, byte ___, ushort ____, ushort _____)
        {
            handle = 3;
            return 1;
        }

        private static byte SetDeckStateImpl(nuint _, SteamDeckDeviceState __) => 1;

        private static byte SetDeckCallbackImpl(nuint _, SteamDeckOutputCallback? __)
        {
            if (DetectConcurrentSetDeckCallbackEntry)
            {
                var depth = Interlocked.Increment(ref _concurrentSetDeckCallbackDepth);
                if (depth > 1) ConcurrentSetDeckCallbackEntryDetected = true;
                // Widens the window so an unsynchronized concurrent caller is virtually certain to
                // land inside it -- without this, a caller that raced in unsynchronized could still
                // slip through between the increment and decrement below on a fast machine.
                Thread.Sleep(5);
                Interlocked.Decrement(ref _concurrentSetDeckCallbackDepth);
            }

            if (FailSetDeckCallback) return 0;
            if (TrackLastSetDeckCallback) LastSetDeckCallback = __;
            return 1;
        }

        private static byte RemoveDeckDeviceImpl(nuint _) => 1;

        private static SteamDeckDeviceRemoveResult RemoveDeckDeviceExImpl(nuint _) => RemoveDeckDeviceExResult;

        private static USBDeviceAttachResult AttachExImpl(nuint _) => AttachExResult;

        private static USBDeviceDetachResult DetachExImpl(nuint _) => DetachExResult;

        private static byte GetAttachmentStateImpl(nuint _, out USBDeviceAttachmentState state)
        {
            state = AttachmentState;
            return FailGetAttachmentState ? (byte)0 : (byte)1;
        }

        private static byte CreateXbox360Impl(nuint _, out nuint handle, uint __, byte autoAttachLocalhost, ushort idVendor, ushort idProduct, byte xinputSubType)
        {
            if (FailCreateXbox360) { handle = 0; return 0; }
            handle = 30;
            LastXbox360AutoAttach = autoAttachLocalhost != 0;
            LastXbox360Vendor = idVendor;
            LastXbox360Product = idProduct;
            LastXbox360SubType = xinputSubType;
            return 1;
        }

        private static byte SetXbox360StateImpl(nuint _, Xbox360DeviceState state)
        {
            LastXbox360State = state;
            return 1;
        }

        private static byte RemoveXbox360Impl(nuint _) => FailRemoveXbox360 ? (byte)0 : (byte)1;

        private static Xbox360DeviceRemoveResult RemoveXbox360ExImpl(nuint _) => RemoveXbox360ExResult;
    }
}
