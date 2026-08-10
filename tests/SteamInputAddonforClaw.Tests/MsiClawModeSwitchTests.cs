using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawModeSwitchTests
{
    [Theory]
    [InlineData(0, 0x01)]
    [InlineData(1, 0x02)]
    public void Mode_command_is_exact_64_byte_report(int modeValue, byte modeByte)
    {
        var mode = (MsiClawNativeMode)modeValue;
        var report = MsiClawModeCommand.Build(mode);
        Assert.Equal(64, report.Length);
        Assert.Equal(new byte[] { 0x0F, 0x00, 0x00, 0x3C, 0x24, modeByte, 0x00 }, report[..7]);
        Assert.All(report[7..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void Strong_identity_requires_container_and_parent()
    {
        var strong = MsiClawPhysicalIdentity.From(Device(Guid.NewGuid(), "USB\\ROOT", "HID\\MSI"));
        var weak = MsiClawPhysicalIdentity.From(Device(null, null, "HID\\MSI"));
        Assert.Equal(MsiClawIdentityConfidence.Strong, strong.Confidence);
        Assert.Equal(MsiClawIdentityConfidence.Indeterminate, weak.Confidence);
        Assert.False(strong.StronglyMatches(weak));
    }

    [Fact]
    public async Task Mode_controller_requires_observed_old_and_target_devices()
    {
        var identity = Guid.NewGuid();
        var oldDevice = Device(identity, "USB\\ROOT", "HID\\MSI", 0x1901, 0xFFA0, 0x0001);
        var newDevice = Device(identity, "USB\\ROOT", "HID\\MSI", 0x1902, 0xFFF0, 0x0040);
        var enumerator = new SequenceEnumerator([oldDevice], [newDevice]);
        var writer = new RecordingWriter();
        var controller = new MsiClawModeController(enumerator, new MsiClawControlHidResolver(), writer, TimeSpan.FromSeconds(1), TimeSpan.Zero);
        var result = await controller.SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(oldDevice), CancellationToken.None);
        Assert.True(result.Succeeded); Assert.True(result.OldPidDisappeared); Assert.True(result.TargetPidAppeared); Assert.True(result.IdentityVerified); Assert.Equal(MsiClawNativeMode.DirectInput, writer.Mode);
    }

    private static ControllerDeviceInfo Device(Guid? container, string? parent, string instance, ushort pid = 0x1901, ushort usagePage = 0, ushort usage = 0) => new(instance, container, parent, parent is null ? [] : [parent], "HID", [], [], "HIDClass", null, null, 0x0DB0, pid, true, UsagePage: usagePage, Usage: usage);

    private sealed class SequenceEnumerator(params IReadOnlyList<ControllerDeviceInfo>[] states) : IControllerDeviceEnumerator
    { private int _index; public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => states[Math.Min(_index++, states.Length - 1)]; }
    private sealed class RecordingWriter : IMsiClawModeWriter
    { public MsiClawNativeMode Mode { get; private set; } public Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken) { Mode = mode; return Task.FromResult(true); } }
}
