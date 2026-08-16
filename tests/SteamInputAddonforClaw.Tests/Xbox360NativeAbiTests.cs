using System.Runtime.InteropServices;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class Xbox360NativeAbiTests
{
    // Xbox360 ABI (VIIPER main@e10b5f02945b1322f33c33468e583546600ba000). Field order, widths,
    // and offsets below are copied directly from the generated dist/libVIIPER/libVIIPER.h
    // Xbox360DeviceState struct -- not from memory. Guards the managed definition against future
    // drift from the canonical VIIPER header.
    [Fact]
    public void Xbox360DeviceState_HasCanonicalSizeAndOffsets()
    {
        Assert.Equal(20, Marshal.SizeOf<Xbox360DeviceState>());

        Assert.Equal(0, Marshal.OffsetOf<Xbox360DeviceState>("Buttons").ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<Xbox360DeviceState>("LT").ToInt32());
        Assert.Equal(5, Marshal.OffsetOf<Xbox360DeviceState>("RT").ToInt32());
        Assert.Equal(6, Marshal.OffsetOf<Xbox360DeviceState>("LX").ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<Xbox360DeviceState>("LY").ToInt32());
        Assert.Equal(10, Marshal.OffsetOf<Xbox360DeviceState>("RX").ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<Xbox360DeviceState>("RY").ToInt32());
        Assert.Equal(14, Marshal.OffsetOf<Xbox360DeviceState>("Reserved0").ToInt32());
        Assert.Equal(15, Marshal.OffsetOf<Xbox360DeviceState>("Reserved1").ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<Xbox360DeviceState>("Reserved2").ToInt32());
        Assert.Equal(17, Marshal.OffsetOf<Xbox360DeviceState>("Reserved3").ToInt32());
        Assert.Equal(18, Marshal.OffsetOf<Xbox360DeviceState>("Reserved4").ToInt32());
        Assert.Equal(19, Marshal.OffsetOf<Xbox360DeviceState>("Reserved5").ToInt32());
    }
}
