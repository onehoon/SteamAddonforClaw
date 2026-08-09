using SteamInputAddonforClaw.Input.DirectInput;
using Vortice.DirectInput;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class VorticeDirectInputDeviceEnumeratorTests
{
    [Fact]
    public void GamepadEnumerationConfiguration_UsesGamepadTypeAndAllDevices()
    {
        Assert.Equal(DeviceType.Gamepad, VorticeDirectInputDeviceEnumerator.EnumeratedDeviceType);
        Assert.Equal(DeviceEnumerationFlags.AllDevices, VorticeDirectInputDeviceEnumerator.EnumeratedDeviceFlags);
    }
}
