using SteamInputAddonforClaw.Diagnostics.GordonDPad;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RawInputGordonObserverTests
{
    // Native RID_DEVICE_INFO is cbSize(4) + dwType(4) + a union whose largest member is
    // RID_DEVICE_INFO_KEYBOARD (6 DWORDs = 24 bytes) -- not RID_DEVICE_INFO_HID (16 bytes) -- so the
    // total native size is 32 bytes and the union (hid variant included) starts at offset 8.
    [Fact]
    public void RidDeviceInfo_MatchesTheNativeUnionLayout()
    {
        Assert.Equal(32, Win32RawInputGordonObserver.RidDeviceInfoSizeForTests);
        Assert.Equal(8, Win32RawInputGordonObserver.RidDeviceInfoHidOffsetForTests);
    }

    private const uint GordonVendorId = 0x28DE;
    private const uint GordonProductId = 0x1102;
    private const ushort GordonUsagePage = 0xFF00;
    private const ushort GordonUsage = 0x01;

    [Fact]
    public void IsExpectedGordonDevice_MatchingIdentityAndPath_IsAccepted()
    {
        var result = Win32RawInputGordonObserver.IsExpectedGordonDevice(
            GordonVendorId, GordonProductId, GordonUsagePage, GordonUsage,
            deviceName: @"\??\HID#VID_28DE&PID_1102&IG_00#A\{4d1e55b2-f16f-11cf-88cb-001111000030}",
            expectedDeviceNameOrNormalized: @"\\?\hid#vid_28de&pid_1102&ig_00#a\{4d1e55b2-f16f-11cf-88cb-001111000030}",
            nameAlreadyNormalized: false);

        Assert.True(result);
    }

    [Fact]
    public void IsExpectedGordonDevice_SameVidPidUsageDifferentDevice_IsRejected()
    {
        // The exact scenario Major 2 exists to prevent: a real Steam Controller (device B) or a stale
        // Gordon node shares the same VID/PID/usage as the Addon-owned Gordon (device A) that was
        // actually selected/expected -- device B must never be accepted just because it's Gordon-shaped.
        const string expectedPathA = @"\\?\hid#vid_28de&pid_1102&ig_00#a\{4d1e55b2-f16f-11cf-88cb-001111000030}";
        const string deviceNameB = @"\??\HID#VID_28DE&PID_1102&IG_00#B\{4d1e55b2-f16f-11cf-88cb-001111000030}";

        var result = Win32RawInputGordonObserver.IsExpectedGordonDevice(
            GordonVendorId, GordonProductId, GordonUsagePage, GordonUsage,
            deviceName: deviceNameB,
            expectedDeviceNameOrNormalized: expectedPathA,
            nameAlreadyNormalized: false);

        Assert.False(result);
    }

    [Theory]
    [InlineData(0x28DD)] // wrong vendor
    public void IsExpectedGordonDevice_WrongVendorId_IsRejected(uint wrongVendorId)
    {
        var result = Win32RawInputGordonObserver.IsExpectedGordonDevice(
            wrongVendorId, GordonProductId, GordonUsagePage, GordonUsage,
            deviceName: @"\??\HID#SOMETHING", expectedDeviceNameOrNormalized: @"\\?\hid#something", nameAlreadyNormalized: false);

        Assert.False(result);
    }

    [Fact]
    public void IsExpectedGordonDevice_WrongUsage_IsRejected()
    {
        var result = Win32RawInputGordonObserver.IsExpectedGordonDevice(
            GordonVendorId, GordonProductId, GordonUsagePage, usage: 0x02,
            deviceName: @"\??\HID#SAME", expectedDeviceNameOrNormalized: @"\\?\hid#same", nameAlreadyNormalized: false);

        Assert.False(result);
    }

    [Fact]
    public void IsExpectedGordonDevice_KernelAndWin32NamespacePrefixesAreEquivalent()
    {
        // RIDI_DEVICENAME returns a kernel-namespace path (\??\...); GordonHidCandidate.DevicePath (from
        // SetupAPI) is the Win32-namespace equivalent (\\?\...) -- same device, different prefix
        // convention, and case may also differ.
        var result = Win32RawInputGordonObserver.IsExpectedGordonDevice(
            GordonVendorId, GordonProductId, GordonUsagePage, GordonUsage,
            deviceName: @"\??\HID#Vid_28DE&Pid_1102#Foo",
            expectedDeviceNameOrNormalized: @"\\?\HID#vid_28de&PID_1102#FOO",
            nameAlreadyNormalized: false);

        Assert.True(result);
    }

    [Fact]
    public void IsExpectedGordonDevice_NullDeviceName_IsRejected()
    {
        var result = Win32RawInputGordonObserver.IsExpectedGordonDevice(
            GordonVendorId, GordonProductId, GordonUsagePage, GordonUsage,
            deviceName: null, expectedDeviceNameOrNormalized: @"\\?\hid#something", nameAlreadyNormalized: false);

        Assert.False(result);
    }
}
