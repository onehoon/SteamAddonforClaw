using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed record MsiClawBackButtonProfileReadSet(
    ushort? FirmwareVersion,
    FrontendMsiBackButtonProfileSlotResult M1,
    FrontendMsiBackButtonProfileSlotResult M2);

internal sealed class MsiClawBackButtonProfileReader(
    IControllerDeviceEnumerator deviceEnumerator,
    IMsiClawControlHidResolver resolver,
    IMsiClawHidDeviceInformationLookup informationLookup,
    IMsiClawRawHidTransport transport)
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMilliseconds(750);
    private const int MaximumReports = 4;

    internal async Task<MsiClawBackButtonProfileReadSet> ReadAsync(
        string modelId,
        Func<MsiClawPhysicalIdentity?> ownedIdentitySource,
        Func<bool> centerMIsExactlyDisabled,
        CancellationToken cancellationToken)
    {
        ushort? firmwareVersion = null;
        var modelSupported = IsSupportedModel(modelId);
        var a2vm7ResearchOnly = string.Equals(modelId, "msi.claw.a2vm.7", StringComparison.Ordinal);
        AppLog.Info("ControllerBackButton", "ControllerBackButtonProfileReadStarted",
            ("Model", modelId),
            ("ReadIndex", MsiClawBackButtonProfileProtocol.ReadIndex),
            ("SlotCount", 2),
            ("RequestedLength", MsiClawBackButtonProfileProtocol.PayloadLength),
            ("A2vm7ResearchOnly", a2vm7ResearchOnly),
            ("FirmwareMutationIssued", false));

        var m1 = await ReadSlotAsync("M1", MsiClawBackButtonProfileProtocol.M1Address).ConfigureAwait(false);
        var m2 = await ReadSlotAsync("M2", MsiClawBackButtonProfileProtocol.M2Address).ConfigureAwait(false);
        AppLog.Info("ControllerBackButton", "ControllerBackButtonProfileReadSummary",
            ("Model", modelId),
            ("Firmware", firmwareVersion is ushort version ? $"0x{version:X4}" : "Unavailable"),
            ("M1Outcome", m1.Outcome),
            ("M1Classification", m1.Classification),
            ("M2Outcome", m2.Outcome),
            ("M2Classification", m2.Classification),
            ("A2vm7ResearchOnly", a2vm7ResearchOnly),
            ("FirmwareMutationIssued", false));
        return new(firmwareVersion, m1, m2);

        async Task<FrontendMsiBackButtonProfileSlotResult> ReadSlotAsync(string slot, ushort address)
        {
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            byte[]? payload = null;
            var classification = FrontendMsiBackButtonProfileClassification.NotApplicable;
            FrontendMsiBackButtonProfileReadOutcome outcome;
            string reason;
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.Cancelled, "Cancelled");
                if (!modelSupported)
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.UnsupportedModel, "UnsupportedModel");
                if (!IsCenterMDisabled(centerMIsExactlyDisabled))
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, "CenterMIsNotExactlyDisabled");

                var ownedIdentity = CaptureStrongOwnedIdentity(ownedIdentitySource);
                if (ownedIdentity is null)
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, "OwnedPhysicalSessionUnavailable");

                var control = ResolveCurrentControl(ownedIdentity);
                if (control is null)
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, "ExactPid1902ControlHidUnavailableOrAmbiguous");
                var selector = Windows.Devices.HumanInterfaceDevice.HidDevice.GetDeviceSelector(
                    control.UsagePage, control.Usage, MsiClawHardware.VendorId, MsiClawHardware.DirectInputProductId);
                var candidates = await informationLookup.FindAsync(selector, cancellationToken).ConfigureAwait(false);
                var info = WindowsMsiClawModeWriter.SelectDeviceInformation(control, candidates);
                if (info is null)
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, "ExactControlHidPathUnavailableOrAmbiguous");
                if (!TryReadFirmware(info.Id, out var attributes))
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.TransportUnavailable, "ControlHidAttributesUnavailableOrUnexpected");
                if (firmwareVersion is ushort previousVersion && previousVersion != attributes.VersionNumber)
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, "FirmwareVersionChangedBetweenSlotReads");
                firmwareVersion = attributes.VersionNumber;

                // Re-resolve the exact current owner and endpoint immediately before each query.
                if (!IsCenterMDisabled(centerMIsExactlyDisabled)
                    || !IsSameStrongOwnedIdentity(ownedIdentitySource, ownedIdentity))
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, "OwnerOrCenterMChangedBeforeQuery");
                var currentControl = ResolveCurrentControl(ownedIdentity);
                if (currentControl is null
                    || !string.Equals(currentControl.Device.InstanceId, control.Device.InstanceId, StringComparison.OrdinalIgnoreCase)
                    || currentControl.UsagePage != control.UsagePage
                    || currentControl.Usage != control.Usage)
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, "ControlHidChangedBeforeQuery");
                var currentInfoCandidates = await informationLookup.FindAsync(selector, cancellationToken).ConfigureAwait(false);
                var currentInfo = WindowsMsiClawModeWriter.SelectDeviceInformation(currentControl, currentInfoCandidates);
                if (currentInfo is null || !string.Equals(currentInfo.Id, info.Id, StringComparison.OrdinalIgnoreCase))
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, "ControlHidPathChangedBeforeQuery");
                if (!TryReadFirmware(info.Id, out var currentAttributes)
                    || currentAttributes.VersionNumber != attributes.VersionNumber)
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.TransportUnavailable, "ControlHidAttributesChangedBeforeQuery");

                cancellationToken.ThrowIfCancellationRequested();
                var request = MsiClawBackButtonProfileProtocol.BuildReadRequest(address);
                if (request[4] != 0x04)
                    throw new InvalidOperationException("The M1/M2 profile diagnostic only permits ReadProfile requests.");
                var exchange = await transport.WriteAndReadAsync(
                    info.Id,
                    request,
                    reportLength: MsiClawBackButtonProfileProtocol.ReportLength,
                    maxReports: MaximumReports,
                    timeout: QueryTimeout,
                    cancellationToken,
                    preserveShortReports: true).ConfigureAwait(false);
                if (!exchange.WriteSucceeded)
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.TransportUnavailable, "ReadProfileTransportWriteFailed");
                if (exchange.Reports.Count == 0)
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.NoReplyOrTimeout, "NoReplyOrTimeout");

                MsiClawBackButtonProfileProtocol.ParseOutcome? firstFailure = null;
                byte[]? firstRejected = null;
                foreach (var report in exchange.Reports)
                {
                    var parse = MsiClawBackButtonProfileProtocol.ParseReadResponse(report, address, out var parsed);
                    if (parse == MsiClawBackButtonProfileProtocol.ParseOutcome.ReadbackParsed)
                    {
                        payload = parsed.Payload;
                        classification = parsed.Classification;
                        return Complete(FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed,
                            $"ReadbackParsed/{classification}");
                    }
                    firstFailure ??= parse;
                    firstRejected ??= report;
                }

                reason = firstFailure?.ToString() ?? "UnexpectedReport";
                var rejectedOutcome = firstFailure == MsiClawBackButtonProfileProtocol.ParseOutcome.WrongAddressOrIndex
                    ? FrontendMsiBackButtonProfileReadOutcome.WrongAddressOrIndex
                    : FrontendMsiBackButtonProfileReadOutcome.UnexpectedReport;
                AppLog.Info("ControllerBackButton", "ControllerBackButtonProfileReadRejectedReport",
                    ("Model", modelId), ("Firmware", $"0x{attributes.VersionNumber:X4}"),
                    ("Slot", slot), ("Address", $"0x{address:X4}"),
                    ("ReadIndex", MsiClawBackButtonProfileProtocol.ReadIndex),
                    ("Reason", reason),
                    ("ResponsePrefix", firstRejected is { Length: > 0 }
                        ? Convert.ToHexString(firstRejected.AsSpan(0, Math.Min(firstRejected.Length, 18)))
                        : "Unavailable"),
                    ("FirmwareMutationIssued", false));
                return Complete(rejectedOutcome, reason);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Complete(FrontendMsiBackButtonProfileReadOutcome.Cancelled, "Cancelled");
            }
            catch (Exception exception)
            {
                return Complete(FrontendMsiBackButtonProfileReadOutcome.Failed, exception.GetType().Name);
            }

            FrontendMsiBackButtonProfileSlotResult Complete(
                FrontendMsiBackButtonProfileReadOutcome completedOutcome,
                string completedReason)
            {
                outcome = completedOutcome;
                reason = completedReason;
                var result = new FrontendMsiBackButtonProfileSlotResult(
                    slot, address, MsiClawBackButtonProfileProtocol.ReadIndex,
                    MsiClawBackButtonProfileProtocol.PayloadLength, outcome, classification, reason);
                AppLog.Info("ControllerBackButton", "ControllerBackButtonProfileSlotReadCompleted",
                    ("Model", modelId),
                    ("Firmware", firmwareVersion is ushort version ? $"0x{version:X4}" : "Unavailable"),
                    ("PID", "0x1902"),
                    ("UsagePage", "0xFFF0"),
                    ("Usage", "0x0040"),
                    ("Slot", slot),
                    ("Address", $"0x{address:X4}"),
                    ("ReadIndex", result.ReadIndex),
                    ("RequestedLength", result.RequestedLength),
                    ("Outcome", result.Outcome),
                    ("Classification", result.Classification),
                    ("OriginalBytes", payload is { Length: 8 } ? Convert.ToHexString(payload) : "Unavailable"),
                    ("Reason", reason),
                    ("ElapsedMs", (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds),
                    ("FirmwareMutationIssued", false));
                return result;
            }
        }
    }

    private MsiClawControlHidDevice? ResolveCurrentControl(MsiClawPhysicalIdentity identity)
    {
        var control = resolver.Resolve(deviceEnumerator.EnumeratePresentDevices(), MsiClawNativeMode.DirectInput, identity);
        return control is not null
            && control.Device.VendorId == MsiClawHardware.VendorId
            && control.Device.ProductId == MsiClawHardware.DirectInputProductId
            && control.UsagePage == MsiClawHardware.DirectInputControlUsagePage
            && control.Usage == MsiClawHardware.DirectInputControlUsage
            && control.VerifiedIdentity.Confidence == MsiClawIdentityConfidence.Strong
            && MsiClawPhysicalIdentity.From(control.Device).StronglyMatches(identity)
                ? control
                : null;
    }

    private bool TryReadFirmware(string devicePath, out MsiClawHidDeviceAttributes attributes)
    {
        if (transport.TryGetDeviceAttributes(devicePath, out attributes)
            && attributes.VendorId == MsiClawHardware.VendorId
            && attributes.ProductId == MsiClawHardware.DirectInputProductId)
            return true;
        attributes = default;
        return false;
    }

    private static bool IsSupportedModel(string modelId) => modelId is
        "msi.claw.a2vm.8" or "msi.claw.a2vm.7" or "msi.claw.cg3em";

    private static bool IsCenterMDisabled(Func<bool> source)
    {
        try { return source?.Invoke() == true; }
        catch { return false; }
    }

    private static MsiClawPhysicalIdentity? CaptureStrongOwnedIdentity(Func<MsiClawPhysicalIdentity?> source)
    {
        try
        {
            var identity = source?.Invoke();
            return identity is { Confidence: MsiClawIdentityConfidence.Strong, VendorId: MsiClawHardware.VendorId, ProductId: MsiClawHardware.DirectInputProductId }
                && (!string.IsNullOrWhiteSpace(identity.PhysicalDeviceKey)
                    || identity.ContainerId is Guid id && id != Guid.Empty && id != new Guid("00000000-0000-0000-ffff-ffffffffffff"))
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
