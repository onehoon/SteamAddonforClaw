using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics.SteamController1304;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamController1304HidInteropTests
{
    [Fact]
    public void HidParserSuccessUsesNativeStatusValue()
    {
        Assert.Equal(0x00110000, WindowsSteamController1304ReadOnlyTransport.HidpStatusSuccess);
        Assert.Equal(WindowsSteamController1304ReadOnlyTransport.HidpStatusSuccess, SteamController1304HidInteropContract.HidpStatusSuccess);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FeatureReportRequiresReportIdAndContainsWirelessStateCommand(int length)
    {
        Assert.Null(SteamController1304HidInteropContract.BuildWirelessStateFeatureReport(length));
    }

    [Fact]
    public void FeatureReportPlacesB4AfterReportId()
    {
        var report = SteamController1304HidInteropContract.BuildWirelessStateFeatureReport(65);

        Assert.NotNull(report);
        Assert.Equal(0, report![0]);
        Assert.Equal(0xB4, report[1]);
    }

    [Fact]
    public void CandidateTimeoutsDoNotPreventLaterCandidateFromSucceeding()
    {
        var queried = new List<int>();
        var result = SteamController1304CandidateIteration.Query<int, byte[]>([1, 2, 3], TimeSpan.FromMilliseconds(1), candidate =>
        {
            queried.Add(candidate);
            if (candidate < 3) throw new TimeoutException();
            return [0x03];
        });

        Assert.Equal([1, 2, 3], queried);
        Assert.Equal([0x03], result);
    }

    [Fact]
    public void AllCandidateTimeoutsProduceOneFinalTimeout()
    {
        var queried = new List<int>();

        Assert.Throws<TimeoutException>(() => SteamController1304CandidateIteration.Query<int, byte[]>([1, 2, 3], TimeSpan.FromMilliseconds(1), candidate =>
        {
            queried.Add(candidate);
            throw new TimeoutException();
        }));

        Assert.Equal([1, 2, 3], queried);
    }

    [Fact]
    public void HidpCapsNativeLayoutIsFourteenWords()
    {
        // HIDP_CAPS is 32 USHORT fields in the Windows HID parser ABI.
        Assert.Equal(64, Marshal.SizeOf<HidpCapsLayoutProbe>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCapsLayoutProbe
    {
        public ushort Usage; public ushort UsagePage; public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes; public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps; public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps; public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps; public ushort NumberFeatureDataIndices;
    }
}
