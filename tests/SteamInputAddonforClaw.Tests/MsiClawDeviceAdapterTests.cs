using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawDeviceAdapterTests
{
    [Fact]
    public void Adapter_ExposesStableDescriptorAndAuxiliaryControls()
    {
        var adapter = new MsiClawDeviceAdapter();

        Assert.Equal("msi.claw", adapter.Descriptor.Id.Value);
        Assert.Equal(["msi.claw.m1", "msi.claw.m2"], adapter.AuxiliaryControls.Controls.Select(control => control.Id.Value));
        Assert.Equal(0, adapter.AuxiliaryControls.GetIndex(new AuxiliaryControlId("msi.claw.m1")));
        Assert.Equal(1, adapter.AuxiliaryControls.GetIndex(new AuxiliaryControlId("msi.claw.m2")));
    }

    [Theory]
    [InlineData(0x1901)]
    [InlineData(0x1902)]
    [InlineData(0x1903)]
    public void Probe_KnownMsiController_AndMatcher_ReportMatch(int productId)
    {
        var adapter = new MsiClawDeviceAdapter();
        var probe = adapter.Probe(new DeviceProbeContext([new DeviceProbePnpDevice("HID\\MSI", vendorId: 0x0DB0, productId: (ushort)productId)]));
        var match = adapter.InternalControllerMatcher.Match(new InternalControllerMatchContext(Device(0x0DB0, (ushort)productId), []));

        Assert.Equal(DeviceProbeStatus.Match, probe.Status);
        Assert.Equal(InternalControllerMatchStatus.Match, match.Status);
    }

    [Fact]
    public void Probe_AndMatcher_RejectUnrelatedOrNotPresentDevices()
    {
        var adapter = new MsiClawDeviceAdapter();

        Assert.Equal(DeviceProbeStatus.NoMatch, adapter.Probe(new DeviceProbeContext([new DeviceProbePnpDevice("HID\\OTHER", vendorId: 0x045E, productId: 0x1902)])).Status);
        Assert.Equal(InternalControllerMatchStatus.NoMatch, adapter.InternalControllerMatcher.Match(new InternalControllerMatchContext(Device(0x0DB0, 0x1902, present: false), [])).Status);
    }

    [Fact]
    public void Classifier_WhenInternalMatcherThrows_IsIndeterminate()
    {
        var classifier = new ControllerDeviceClassifier(new ThrowingMatcher());

        Assert.Equal(ControllerDeviceClassification.Indeterminate, classifier.Classify(Device(0x045E, 0x028E)));
    }

    private static ControllerDeviceInfo Device(ushort vendorId, ushort productId, bool present = true) =>
        new("HID\\TEST", null, null, [], "HID", ["HID_DEVICE_UP:0001_U:0005"], [], "HIDClass", null, null, vendorId, productId, present);

    private sealed class ThrowingMatcher : IInternalControllerMatcher
    {
        public InternalControllerMatchResult Match(InternalControllerMatchContext context) => throw new InvalidOperationException();
    }
}
