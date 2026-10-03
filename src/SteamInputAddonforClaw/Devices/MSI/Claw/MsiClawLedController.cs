using SteamInputAddonforClaw.Contracts.ControllerLed;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawLedController(
    IControllerDeviceEnumerator deviceEnumerator,
    IMsiClawControlHidResolver resolver,
    IMsiClawHidDeviceInformationLookup informationLookup,
    IMsiClawRawHidTransport transport)
{
    internal async Task<bool> ApplyAsync(
        ControllerLedSettings settings,
        MsiClawPhysicalIdentity? expectedIdentity,
        CancellationToken cancellationToken)
    {
        if (ControllerLedSettingsValidation.Validate(settings) is { } invalid)
            return Fail(invalid);
        if (expectedIdentity is null || expectedIdentity.Confidence != MsiClawIdentityConfidence.Strong
            || (string.IsNullOrWhiteSpace(expectedIdentity.PhysicalDeviceKey) && !IsUsable(expectedIdentity.ContainerId)))
            return Fail("StrongPhysicalIdentityUnavailable");

        var control = resolver.Resolve(deviceEnumerator.EnumeratePresentDevices(), MsiClawNativeMode.DirectInput, expectedIdentity);
        if (control is null
            || control.Device.VendorId != MsiClawHardware.VendorId
            || control.Device.ProductId != MsiClawHardware.DirectInputProductId
            || control.UsagePage != MsiClawHardware.DirectInputControlUsagePage
            || control.Usage != MsiClawHardware.DirectInputControlUsage
            || !MsiClawPhysicalIdentity.From(control.Device).StronglyMatches(expectedIdentity))
            return Fail("ExactPid1902ControlHidUnavailableOrAmbiguous");

        var selector = Windows.Devices.HumanInterfaceDevice.HidDevice.GetDeviceSelector(
            control.UsagePage, control.Usage, MsiClawHardware.VendorId, MsiClawHardware.DirectInputProductId);
        var candidates = await informationLookup.FindAsync(selector, cancellationToken).ConfigureAwait(false);
        var info = WindowsMsiClawModeWriter.SelectDeviceInformation(control, candidates);
        if (info is null)
            return Fail("ExactControlHidPathUnavailableOrAmbiguous");
        if (!transport.TryGetDeviceAttributes(info.Id, out var attributes)
            || attributes.VendorId != MsiClawHardware.VendorId
            || attributes.ProductId != MsiClawHardware.DirectInputProductId)
            return Fail("ControlHidAttributesUnavailableOrUnexpected");
        if (!MsiClawLedProtocol.TryBuildStaticWrites(attributes.VersionNumber, settings, out var writes))
            return Fail(MsiClawLedProtocol.TryResolveRgbAddress(attributes.VersionNumber, out _) ? "InvalidSettings" : "UnsupportedFirmware");

        for (var index = 0; index < writes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await transport.WriteAsync(info.Id, writes[index], cancellationToken).ConfigureAwait(false))
                continue;
            return Fail($"ProfileWriteFailed:{index + 1}");
        }

        AppLog.Info("ControllerLed", "Static LED settings applied to the owned PID1902 control HID.",
            ("FirmwareVersion", $"0x{attributes.VersionNumber:X4}"),
            ("RgbAddress", MsiClawLedProtocol.TryResolveRgbAddress(attributes.VersionNumber, out var rgbAddress) ? $"0x{rgbAddress:X4}" : "Unknown"),
            ("Enabled", settings.Enabled), ("Brightness", settings.Enabled ? settings.Brightness : 0), ("WriteCount", writes.Count));
        return true;

        static bool Fail(string failureReason)
        {
            AppLog.Warn("ControllerLed", "Static LED settings were not applied; controller ownership remains unchanged.", null,
                ("Event", "ControllerLedApplyFailed"), ("Reason", failureReason));
            return false;
        }
    }

    private static bool IsUsable(Guid? value) => value is Guid guid && guid != Guid.Empty && guid != new Guid("00000000-0000-0000-ffff-ffffffffffff");
}
