using SteamInputAddonforClaw.Contracts.ControllerLed;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum MsiClawLedProfileReadProbeOutcome
{
    CandidateReadbackParsed,
    TransportWriteFailed,
    NoReplyOrTimeout,
    UnexpectedReport,
    WrongAddressOrIndex,
    Unavailable,
    Failed
}

internal sealed record MsiClawLedProfileReadProbeResult(
    MsiClawLedProfileReadProbeOutcome Outcome,
    ushort? FirmwareVersion,
    byte? Effect,
    byte? Speed,
    byte? Brightness,
    string Reason)
{
    internal bool ReadResponseValid => Outcome == MsiClawLedProfileReadProbeOutcome.CandidateReadbackParsed;
}

internal sealed class MsiClawLedController(
    IControllerDeviceEnumerator deviceEnumerator,
    IMsiClawControlHidResolver resolver,
    IMsiClawHidDeviceInformationLookup informationLookup,
    IMsiClawRawHidTransport transport)
{
    internal async Task<MsiClawLedProfileReadProbeResult> ReadA2vm230CandidateProfileAsync(
        string modelId,
        Func<MsiClawPhysicalIdentity?> ownedIdentitySource,
        Func<bool> centerMIsExactlyDisabled,
        CancellationToken cancellationToken)
    {
        ushort? firmwareVersion = null;
        AppLog.Info("ControllerLed", "ControllerLedProfileReadProbeStarted",
            ("Model", modelId),
            ("ExpectedFirmware", "0x0230"),
            ("CandidateAddress", $"0x{MsiClawLedProtocol.A2vm230CandidateRgbAddress:X4}"),
            ("ReadIndex", 1),
            ("Length", "0x20"));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(modelId, "msi.claw.a2vm.8", StringComparison.Ordinal))
                return Complete(MsiClawLedProfileReadProbeOutcome.Unavailable, "UnsupportedModel");
            if (!IsCenterMDisabled(centerMIsExactlyDisabled))
                return Complete(MsiClawLedProfileReadProbeOutcome.Unavailable, "CenterMIsNotExactlyDisabled");

            var ownedIdentity = CaptureStrongOwnedIdentity(ownedIdentitySource);
            if (ownedIdentity is null)
                return Complete(MsiClawLedProfileReadProbeOutcome.Unavailable, "OwnedPhysicalSessionUnavailable");

            var control = resolver.Resolve(deviceEnumerator.EnumeratePresentDevices(), MsiClawNativeMode.DirectInput, ownedIdentity);
            if (control is null
                || control.Device.VendorId != MsiClawHardware.VendorId
                || control.Device.ProductId != MsiClawHardware.DirectInputProductId
                || control.UsagePage != MsiClawHardware.DirectInputControlUsagePage
                || control.Usage != MsiClawHardware.DirectInputControlUsage
                || control.VerifiedIdentity.Confidence != MsiClawIdentityConfidence.Strong
                || !MsiClawPhysicalIdentity.From(control.Device).StronglyMatches(ownedIdentity))
                return Complete(MsiClawLedProfileReadProbeOutcome.Unavailable, "ExactPid1902ControlHidUnavailableOrAmbiguous");

            var selector = Windows.Devices.HumanInterfaceDevice.HidDevice.GetDeviceSelector(
                control.UsagePage, control.Usage, MsiClawHardware.VendorId, MsiClawHardware.DirectInputProductId);
            var candidates = await informationLookup.FindAsync(selector, cancellationToken).ConfigureAwait(false);
            var info = WindowsMsiClawModeWriter.SelectDeviceInformation(control, candidates);
            if (info is null)
                return Complete(MsiClawLedProfileReadProbeOutcome.Unavailable, "ExactControlHidPathUnavailableOrAmbiguous");
            if (!transport.TryGetDeviceAttributes(info.Id, out var attributes)
                || attributes.VendorId != MsiClawHardware.VendorId
                || attributes.ProductId != MsiClawHardware.DirectInputProductId)
                return Complete(MsiClawLedProfileReadProbeOutcome.Unavailable, "ControlHidAttributesUnavailableOrUnexpected");

            firmwareVersion = attributes.VersionNumber;
            if (firmwareVersion != 0x0230)
                return Complete(MsiClawLedProfileReadProbeOutcome.Unavailable, "UnsupportedFirmware");

            // Recheck both authorities after endpoint resolution and immediately before the query.
            if (!IsCenterMDisabled(centerMIsExactlyDisabled))
                return Complete(MsiClawLedProfileReadProbeOutcome.Unavailable, "CenterMIsNotExactlyDisabled");
            if (!IsSameStrongOwnedIdentity(ownedIdentitySource, ownedIdentity))
                return Complete(MsiClawLedProfileReadProbeOutcome.Unavailable, "OwnedPhysicalSessionUnavailable");
            cancellationToken.ThrowIfCancellationRequested();

            var request = MsiClawLedProtocol.BuildA2vm230CandidateProfileReadRequest();
            var exchange = await transport.WriteAndReadAsync(
                info.Id,
                request,
                reportLength: 64,
                maxReports: 4,
                timeout: TimeSpan.FromMilliseconds(750),
                cancellationToken,
                preserveShortReports: true).ConfigureAwait(false);
            if (!exchange.WriteSucceeded)
                return Complete(MsiClawLedProfileReadProbeOutcome.TransportWriteFailed, "TransportWriteFailed");
            if (exchange.Reports.Count == 0)
                return Complete(MsiClawLedProfileReadProbeOutcome.NoReplyOrTimeout, "NoReplyOrTimeout");

            MsiClawLedProtocol.CandidateReadParseOutcome? firstFailure = null;
            byte[]? firstRejected = null;
            foreach (var report in exchange.Reports)
            {
                var parseOutcome = MsiClawLedProtocol.ParseA2vm230CandidateProfileReadResponse(report, out var parsed);
                if (parseOutcome == MsiClawLedProtocol.CandidateReadParseOutcome.CandidateReadbackParsed)
                    return Complete(MsiClawLedProfileReadProbeOutcome.CandidateReadbackParsed,
                        parseOutcome.ToString(), report, parsed.Effect, parsed.Speed, parsed.Brightness);

                firstFailure ??= parseOutcome;
                firstRejected ??= report;
            }

            var rejectedOutcome = firstFailure == MsiClawLedProtocol.CandidateReadParseOutcome.WrongAddressOrIndex
                ? MsiClawLedProfileReadProbeOutcome.WrongAddressOrIndex
                : MsiClawLedProfileReadProbeOutcome.UnexpectedReport;
            return Complete(rejectedOutcome, firstFailure?.ToString() ?? "UnexpectedReport", firstRejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Complete(MsiClawLedProfileReadProbeOutcome.Failed, exception.GetType().Name);
        }

        MsiClawLedProfileReadProbeResult Complete(
            MsiClawLedProfileReadProbeOutcome outcome,
            string reason,
            byte[]? response = null,
            byte? effect = null,
            byte? speed = null,
            byte? brightness = null)
        {
            var responseValid = outcome == MsiClawLedProfileReadProbeOutcome.CandidateReadbackParsed;
            AppLog.Info("ControllerLed", "ControllerLedProfileReadProbeCompleted",
                ("Outcome", outcome),
                ("Model", modelId),
                ("Firmware", firmwareVersion is ushort firmware ? $"0x{firmware:X4}" : "Unavailable"),
                ("CandidateAddress", $"0x{MsiClawLedProtocol.A2vm230CandidateRgbAddress:X4}"),
                ("ReadIndex", 1),
                ("Length", "0x20"),
                ("ReplyMatchesRequest", responseValid),
                ("ReadResponseValid", responseValid),
                ("Effect", effect),
                ("Speed", speed),
                ("Brightness", brightness),
                ("RgbTriplets", responseValid && response is { Length: 64 }
                    ? Convert.ToHexString(response.AsSpan(14, 27))
                    : "Unavailable"),
                ("ResponsePrefix", response is { Length: > 0 }
                    ? Convert.ToHexString(response.AsSpan(0, Math.Min(response.Length, 41)))
                    : "Unavailable"),
                ("ProductionEnabled", false),
                ("Reason", reason));
            return new(outcome, firmwareVersion, effect, speed, brightness, reason);
        }
    }

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
        {
            var failureReason = MsiClawLedProtocol.TryResolveRgbAddress(attributes.VersionNumber, out _)
                ? "InvalidSettings"
                : "UnsupportedFirmware";
            if (failureReason == "UnsupportedFirmware")
            {
                AppLog.Warn("ControllerLed", "Static LED settings were not applied because the controller firmware is not in the verified RGB address table; controller ownership remains unchanged.", null,
                    ("Event", "ControllerLedApplyFailed"), ("Reason", failureReason),
                    ("FirmwareVersion", $"0x{attributes.VersionNumber:X4}"));
                return false;
            }

            return Fail(failureReason);
        }

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

    private static bool IsCenterMDisabled(Func<bool> centerMIsExactlyDisabled)
    {
        try { return centerMIsExactlyDisabled?.Invoke() == true; }
        catch { return false; }
    }

    private static MsiClawPhysicalIdentity? CaptureStrongOwnedIdentity(Func<MsiClawPhysicalIdentity?> source)
    {
        try
        {
            var identity = source?.Invoke();
            return identity is { Confidence: MsiClawIdentityConfidence.Strong, VendorId: MsiClawHardware.VendorId, ProductId: MsiClawHardware.DirectInputProductId }
                && (!string.IsNullOrWhiteSpace(identity.PhysicalDeviceKey) || IsUsable(identity.ContainerId))
                    ? identity
                    : null;
        }
        catch { return null; }
    }

    private static bool IsSameStrongOwnedIdentity(
        Func<MsiClawPhysicalIdentity?> source,
        MsiClawPhysicalIdentity expected) =>
        CaptureStrongOwnedIdentity(source) is { } current && current.StronglyMatches(expected);
}
