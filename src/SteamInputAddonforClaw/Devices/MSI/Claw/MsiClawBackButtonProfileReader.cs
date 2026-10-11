using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed record MsiClawBackButtonProfileReadSet(
    ushort? FirmwareVersion,
    FrontendMsiBackButtonProfileSlotResult M1,
    FrontendMsiBackButtonProfileSlotResult M2)
{
    internal byte[]? M1Payload { get; init; }
    internal byte[]? M2Payload { get; init; }
    internal string? ControlDevicePath { get; init; }
}

internal sealed class MsiClawBackButtonProfileReader(
    IControllerDeviceEnumerator deviceEnumerator,
    IMsiClawControlHidResolver resolver,
    IMsiClawHidDeviceInformationLookup informationLookup,
    IMsiClawRawHidTransport transport)
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromMilliseconds(750);
    private const int MaximumReports = 4;

    internal async Task<MsiClawBackButtonProfileReadSet> ReadAsync(
        string modelId,
        Func<MsiClawPhysicalIdentity?> ownedIdentitySource,
        Func<bool> centerMIsExactlyDisabled,
        CancellationToken cancellationToken,
        bool firmwareMutationIssued = false)
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
            ("FirmwareMutationIssued", firmwareMutationIssued));

        byte[]? m1Payload = null;
        byte[]? m2Payload = null;
        string? controlDevicePath = null;
        var m1 = await ReadSlotAsync("M1", MsiClawBackButtonProfileProtocol.M1Address, payload => m1Payload = payload).ConfigureAwait(false);
        var m2 = await ReadSlotAsync("M2", MsiClawBackButtonProfileProtocol.M2Address, payload => m2Payload = payload).ConfigureAwait(false);
        AppLog.Info("ControllerBackButton", "ControllerBackButtonProfileReadSummary",
            ("Model", modelId),
            ("Firmware", firmwareVersion is ushort version ? $"0x{version:X4}" : "Unavailable"),
            ("M1Outcome", m1.Outcome),
            ("M1Classification", m1.Classification),
            ("M2Outcome", m2.Outcome),
            ("M2Classification", m2.Classification),
            ("A2vm7ResearchOnly", a2vm7ResearchOnly),
            ("FirmwareMutationIssued", firmwareMutationIssued));
        return new(firmwareVersion, m1, m2)
        {
            M1Payload = m1Payload,
            M2Payload = m2Payload,
            ControlDevicePath = controlDevicePath,
        };

        async Task<FrontendMsiBackButtonProfileSlotResult> ReadSlotAsync(
            string slot,
            ushort address,
            Action<byte[]> capturePayload)
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
                if (controlDevicePath is not null && !string.Equals(controlDevicePath, info.Id, StringComparison.OrdinalIgnoreCase))
                    return Complete(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, "ControlHidPathChangedBetweenSlotReads");
                controlDevicePath = info.Id;
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
                        capturePayload(parsed.Payload);
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
                    ("FirmwareMutationIssued", firmwareMutationIssued));
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
                    ("FirmwareMutationIssued", firmwareMutationIssued));
                return result;
            }
        }
    }

    internal async Task<FrontendMsiBackButtonManualRestoreResult> RestoreKnownDirectInputProfileAsync(
        string modelId,
        Func<MsiClawPhysicalIdentity?> ownedIdentitySource,
        Func<bool> centerMIsExactlyDisabled,
        Func<bool> runtimeOperationIsSafe,
        CancellationToken cancellationToken)
    {
        var identity = CaptureStrongOwnedIdentity(ownedIdentitySource);
        AppLog.Info("ControllerBackButton", "Explicit M1/M2 DirectInput restore requested.",
            ("Event", "ControllerBackButtonManualRestoreStarted"),
            ("Model", modelId),
            ("PID", "0x1902"),
            ("UsagePage", "0xFFF0"),
            ("Usage", "0x0040"),
            ("CenterMDisabled", IsCenterMDisabled(centerMIsExactlyDisabled)));

        if (!IsManualRestoreModel(modelId))
            return Complete(FrontendMsiBackButtonManualRestoreOutcome.NotEligible, null,
                "This model is read-only for manual M1/M2 restore; A2VM 7 is not write-enabled.");
        if (identity is null || !IsRuntimeSafe(runtimeOperationIsSafe)
            || !IsCenterMDisabled(centerMIsExactlyDisabled))
            return Complete(FrontendMsiBackButtonManualRestoreOutcome.Unavailable, null,
                "A healthy Addon-owned strong PID1902 session with Center M exactly Disabled is required.");
        if (cancellationToken.IsCancellationRequested)
            return Complete(FrontendMsiBackButtonManualRestoreOutcome.Cancelled, null,
                "Cancelled before profile reads or mutation.");

        var before = await ReadAsync(modelId, ownedIdentitySource, centerMIsExactlyDisabled, cancellationToken)
            .ConfigureAwait(false);
        var m1BeforeHex = ToHex(before.M1Payload);
        var m2BeforeHex = ToHex(before.M2Payload);
        AppLog.Info("ControllerBackButton", "Manual M1/M2 restore pre-read completed.",
            ("Event", "ControllerBackButtonManualRestorePreRead"),
            ("Model", modelId),
            ("Firmware", before.FirmwareVersion is ushort beforeVersion ? $"0x{beforeVersion:X4}" : "Unavailable"),
            ("M1Outcome", before.M1.Outcome), ("M1Classification", before.M1.Classification), ("M1OriginalBytes", m1BeforeHex ?? "Unavailable"),
            ("M2Outcome", before.M2.Outcome), ("M2Classification", before.M2.Classification), ("M2OriginalBytes", m2BeforeHex ?? "Unavailable"));

        if (cancellationToken.IsCancellationRequested)
            return Complete(FrontendMsiBackButtonManualRestoreOutcome.Cancelled, before,
                "Cancelled before the first profile write.");
        if (before.M1.Outcome != FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed
            || before.M2.Outcome != FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed
            || before.M1Payload is not { Length: MsiClawBackButtonProfileProtocol.PayloadLength }
            || before.M2Payload is not { Length: MsiClawBackButtonProfileProtocol.PayloadLength }
            || string.IsNullOrWhiteSpace(before.ControlDevicePath))
            return Complete(FrontendMsiBackButtonManualRestoreOutcome.NotEligible, before,
                "Both exact PID1902 profile slots must be read successfully before any write.");

        if (!IsAllowedManualRestoreFirmware(modelId, before.FirmwareVersion))
            return Complete(FrontendMsiBackButtonManualRestoreOutcome.NotEligible, before,
                "This exact model and firmware pair is not write-enabled.");

        var m1Prepared = IsKnownDirectInputProfile(before.M1Payload);
        var m2Prepared = IsKnownDirectInputProfile(before.M2Payload);
        if (m1Prepared && m2Prepared)
            return Complete(FrontendMsiBackButtonManualRestoreOutcome.AlreadyPrepared, before,
                "Both profile slots already have the known DirectInput header; no write was issued.");

        if (!IsObservedCandidate(before.M1Payload, MsiClawBackButtonProfileProtocol.M1Address, isM1: true)
            || !IsObservedCandidate(before.M2Payload, MsiClawBackButtonProfileProtocol.M2Address, isM1: false))
            return Complete(FrontendMsiBackButtonManualRestoreOutcome.NotEligible, before,
                "UnknownProfile: unknown, custom, historical, or mixed M1/M2 profile bytes were preserved; no write was issued.");

        if (!IsSameStrongOwnedIdentity(ownedIdentitySource, identity)
            || !IsRuntimeSafe(runtimeOperationIsSafe)
            || !IsCenterMDisabled(centerMIsExactlyDisabled))
            return Complete(FrontendMsiBackButtonManualRestoreOutcome.Unavailable, before,
                "Runtime ownership or Center M authority changed before the first profile write.");

        var endpoint = await ResolveManualRestoreEndpointAsync(
            identity, before.ControlDevicePath, before.FirmwareVersion!.Value,
            ownedIdentitySource, centerMIsExactlyDisabled, runtimeOperationIsSafe).ConfigureAwait(false);
        if (endpoint is null)
            return Complete(FrontendMsiBackButtonManualRestoreOutcome.Unavailable, before,
                "The exact owned PID1902 control HID or firmware changed before mutation.");
        if (cancellationToken.IsCancellationRequested)
            return Complete(FrontendMsiBackButtonManualRestoreOutcome.Cancelled, before,
                "Cancelled before the first profile write.");

        var m1Write = MsiClawBackButtonProfileProtocol.BuildDirectInputRestoreWriteRequest(MsiClawBackButtonProfileProtocol.M1Address);
        var m2Write = MsiClawBackButtonProfileProtocol.BuildDirectInputRestoreWriteRequest(MsiClawBackButtonProfileProtocol.M2Address);
        var sync = MsiClawBackButtonProfileProtocol.BuildSyncToRomRequest();
        var m1Issued = false;
        var m1Succeeded = false;
        var m2Issued = false;
        var m2Succeeded = false;
        var syncIssued = false;
        var syncSucceeded = false;
        MsiClawBackButtonProfileReadSet? after = null;
        try
        {
            // The caller token is an admission/cancel-before-first-write token only. Once the
            // first firmware command is issued, only current Runtime ownership gates can stop
            // subsequent commands; frontend disconnect must not create an accidental half-pair.
            m1Issued = true;
            m1Succeeded = await WriteBoundedAsync(endpoint.DevicePath, m1Write).ConfigureAwait(false);
            LogCommand("M1Write", MsiClawBackButtonProfileProtocol.M1Address, m1Write, m1Succeeded);
            if (!m1Succeeded)
                return await FinishAsync(FrontendMsiBackButtonManualRestoreOutcome.WriteFailed,
                    "The M1 write transport failed; M2 and ROM sync were not issued.").ConfigureAwait(false);

            endpoint = await ResolveManualRestoreEndpointAsync(
                identity, before.ControlDevicePath, before.FirmwareVersion.Value,
                ownedIdentitySource, centerMIsExactlyDisabled, runtimeOperationIsSafe).ConfigureAwait(false);
            if (endpoint is null)
                return await FinishAsync(FrontendMsiBackButtonManualRestoreOutcome.PartialVolatileWritePossible,
                    "The owned session changed after M1; M2 and ROM sync were not issued.").ConfigureAwait(false);

            m2Issued = true;
            m2Succeeded = await WriteBoundedAsync(endpoint.DevicePath, m2Write).ConfigureAwait(false);
            LogCommand("M2Write", MsiClawBackButtonProfileProtocol.M2Address, m2Write, m2Succeeded);
            if (!m2Succeeded)
                return await FinishAsync(FrontendMsiBackButtonManualRestoreOutcome.PartialVolatileWritePossible,
                    "M2 transport failed after the M1 write; ROM sync was not issued.").ConfigureAwait(false);

            endpoint = await ResolveManualRestoreEndpointAsync(
                identity, before.ControlDevicePath, before.FirmwareVersion.Value,
                ownedIdentitySource, centerMIsExactlyDisabled, runtimeOperationIsSafe).ConfigureAwait(false);
            if (endpoint is null)
                return await FinishAsync(FrontendMsiBackButtonManualRestoreOutcome.PartialVolatileWritePossible,
                    "The owned session changed after both profile writes; ROM sync was not issued.").ConfigureAwait(false);

            syncIssued = true;
            syncSucceeded = await WriteBoundedAsync(endpoint.DevicePath, sync).ConfigureAwait(false);
            LogCommand("SyncToROM", null, sync, syncSucceeded);
            return await FinishAsync(FrontendMsiBackButtonManualRestoreOutcome.Unverified,
                syncSucceeded
                    ? "Both profile writes and ROM sync were accepted; verifying readback and physical input separately."
                    : "Both profile writes were accepted, but ROM sync transport failed; result is unverified.").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerBackButton", "Manual M1/M2 restore stopped after an exception.", exception,
                ("Event", "ControllerBackButtonManualRestoreFailed"),
                ("Model", modelId), ("Firmware", $"0x{before.FirmwareVersion:X4}"),
                ("M1WriteIssued", m1Issued), ("M1WriteSucceeded", m1Succeeded),
                ("M2WriteIssued", m2Issued), ("M2WriteSucceeded", m2Succeeded),
                ("RomSyncIssued", syncIssued), ("RomSyncSucceeded", syncSucceeded));
            var outcome = m1Succeeded && !m2Succeeded || m1Succeeded && m2Succeeded && !syncIssued
                ? FrontendMsiBackButtonManualRestoreOutcome.PartialVolatileWritePossible
                : FrontendMsiBackButtonManualRestoreOutcome.Failed;
            return await FinishAsync(outcome, $"Manual restore stopped: {exception.GetType().Name}.").ConfigureAwait(false);
        }

        async Task<FrontendMsiBackButtonManualRestoreResult> FinishAsync(
            FrontendMsiBackButtonManualRestoreOutcome requestedOutcome,
            string status)
        {
            if ((m1Issued || m2Issued || syncIssued)
                && IsRuntimeSafe(runtimeOperationIsSafe)
                && IsSameStrongOwnedIdentity(ownedIdentitySource, identity)
                    && IsCenterMDisabled(centerMIsExactlyDisabled))
            {
                after = await ReadAsync(modelId, ownedIdentitySource, centerMIsExactlyDisabled, CancellationToken.None,
                    firmwareMutationIssued: true)
                    .ConfigureAwait(false);
            }

            var readbackObserved = after?.M1.Outcome == FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed
                && after.M2.Outcome == FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed;
            var matchesPrepared = readbackObserved
                && IsKnownDirectInputProfile(after!.M1Payload)
                && IsKnownDirectInputProfile(after.M2Payload);
            var profileReadbackVerified = m1Succeeded && m2Succeeded && syncIssued && syncSucceeded && matchesPrepared;
            var finalOutcome = requestedOutcome;
            var finalStatus = requestedOutcome == FrontendMsiBackButtonManualRestoreOutcome.Unverified && profileReadbackVerified
                ? "Both DirectInput profile headers read back; physical M1/M2 input is not yet verified."
                : status;

            var result = new FrontendMsiBackButtonManualRestoreResult(
                finalOutcome, modelId, before.FirmwareVersion, finalStatus,
                ManualSlot("M1", MsiClawBackButtonProfileProtocol.M1Address, before.M1, before.M1Payload,
                    m1Issued, m1Succeeded, after?.M1, after?.M1Payload),
                ManualSlot("M2", MsiClawBackButtonProfileProtocol.M2Address, before.M2, before.M2Payload,
                    m2Issued, m2Succeeded, after?.M2, after?.M2Payload),
                syncIssued, syncSucceeded, readbackObserved,
                FrontendRawDirectInputButtonTransitionResult.Unavailable("Raw input confirmation has not run."));
            AppLog.Info("ControllerBackButton", "Manual M1/M2 DirectInput restore completed.",
                ("Event", "ControllerBackButtonManualRestoreCompleted"),
                ("Outcome", result.Outcome), ("Model", modelId),
                ("Firmware", result.FirmwareVersion is ushort resultVersion ? $"0x{resultVersion:X4}" : "Unavailable"),
                ("M1Before", result.M1.BeforePayloadHex ?? "Unavailable"),
                ("M2Before", result.M2.BeforePayloadHex ?? "Unavailable"),
                ("M1WriteIssued", result.M1.WriteIssued), ("M1WriteTransportSucceeded", result.M1.WriteTransportSucceeded),
                ("M2WriteIssued", result.M2.WriteIssued), ("M2WriteTransportSucceeded", result.M2.WriteTransportSucceeded),
                ("RomSyncIssued", result.RomSyncIssued), ("RomSyncTransportSucceeded", result.RomSyncTransportSucceeded),
                ("M1After", result.M1.AfterPayloadHex ?? "Unavailable"),
                ("M2After", result.M2.AfterPayloadHex ?? "Unavailable"),
                ("ReadbackObserved", result.ReadbackObserved),
                ("ProfileReadbackVerified", result.ProfileReadbackVerified),
                ("Status", result.Status));
            return result;
        }

        void LogCommand(string command, ushort? address, byte[] packet, bool succeeded) =>
            AppLog.Info("ControllerBackButton", "Manual M1/M2 firmware command completed.",
                ("Event", "ControllerBackButtonManualRestoreCommand"),
                ("Command", command), ("Address", address is ushort value ? $"0x{value:X4}" : "N/A"),
                ("PacketHex", Convert.ToHexString(packet)), ("TransportSucceeded", succeeded));

        FrontendMsiBackButtonManualRestoreResult Complete(
            FrontendMsiBackButtonManualRestoreOutcome outcome,
            MsiClawBackButtonProfileReadSet? read,
            string status)
        {
            var m1 = read?.M1 ?? EmptySlot("M1", MsiClawBackButtonProfileProtocol.M1Address, status);
            var m2 = read?.M2 ?? EmptySlot("M2", MsiClawBackButtonProfileProtocol.M2Address, status);
            return new(outcome, modelId, read?.FirmwareVersion, status,
                ManualSlot("M1", MsiClawBackButtonProfileProtocol.M1Address, m1, read?.M1Payload, false, false, null, null),
                ManualSlot("M2", MsiClawBackButtonProfileProtocol.M2Address, m2, read?.M2Payload, false, false, null, null),
                false, false, false,
                FrontendRawDirectInputButtonTransitionResult.Unavailable(status));
        }
    }

    private async Task<MsiClawManualRestoreEndpoint?> ResolveManualRestoreEndpointAsync(
        MsiClawPhysicalIdentity expectedIdentity,
        string expectedDevicePath,
        ushort expectedFirmware,
        Func<MsiClawPhysicalIdentity?> ownedIdentitySource,
        Func<bool> centerMIsExactlyDisabled,
        Func<bool> runtimeOperationIsSafe)
    {
        if (!IsRuntimeSafe(runtimeOperationIsSafe)
            || !IsCenterMDisabled(centerMIsExactlyDisabled)
            || !IsSameStrongOwnedIdentity(ownedIdentitySource, expectedIdentity))
            return null;

        var control = ResolveCurrentControl(expectedIdentity);
        if (control is null)
            return null;
        var selector = Windows.Devices.HumanInterfaceDevice.HidDevice.GetDeviceSelector(
            control.UsagePage, control.Usage, MsiClawHardware.VendorId, MsiClawHardware.DirectInputProductId);
        var candidates = await informationLookup.FindAsync(selector, CancellationToken.None).ConfigureAwait(false);
        var info = WindowsMsiClawModeWriter.SelectDeviceInformation(control, candidates);
        if (info is null
            || !string.Equals(info.Id, expectedDevicePath, StringComparison.OrdinalIgnoreCase)
            || !TryReadFirmware(info.Id, out var attributes)
            || attributes.VersionNumber != expectedFirmware)
            return null;

        // Recheck the strong physical owner and DirectInput control collection after the async
        // device lookup; a PnP or mode transition during discovery must fail closed.
        var currentControl = ResolveCurrentControl(expectedIdentity);
        return IsRuntimeSafe(runtimeOperationIsSafe)
            && IsCenterMDisabled(centerMIsExactlyDisabled)
            && IsSameStrongOwnedIdentity(ownedIdentitySource, expectedIdentity)
            && currentControl is not null
            && string.Equals(currentControl.Device.InstanceId, control.Device.InstanceId, StringComparison.OrdinalIgnoreCase)
            && currentControl.UsagePage == MsiClawHardware.DirectInputControlUsagePage
            && currentControl.Usage == MsiClawHardware.DirectInputControlUsage
                ? new(info.Id, attributes.VersionNumber)
                : null;
    }

    private async Task<bool> WriteBoundedAsync(string devicePath, byte[] request)
    {
        using var timeout = new CancellationTokenSource(WriteTimeout);
        try
        {
            return await transport.WriteAsync(devicePath, request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return false;
        }
    }

    private static FrontendMsiBackButtonManualRestoreSlotResult ManualSlot(
        string slot,
        ushort address,
        FrontendMsiBackButtonProfileSlotResult before,
        byte[]? beforePayload,
        bool writeIssued,
        bool writeSucceeded,
        FrontendMsiBackButtonProfileSlotResult? after,
        byte[]? afterPayload) =>
        new(slot, address, before.Outcome, before.Classification, ToHex(beforePayload),
            writeIssued, writeSucceeded, after?.Outcome, after?.Classification, ToHex(afterPayload));

    private static FrontendMsiBackButtonProfileSlotResult EmptySlot(string slot, ushort address, string status) =>
        new(slot, address, MsiClawBackButtonProfileProtocol.ReadIndex, MsiClawBackButtonProfileProtocol.PayloadLength,
            FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable,
            FrontendMsiBackButtonProfileClassification.NotApplicable, status);

    private static string? ToHex(byte[]? payload) => payload is { Length: MsiClawBackButtonProfileProtocol.PayloadLength }
        ? Convert.ToHexString(payload)
        : null;

    private static bool IsObservedCandidate(byte[]? payload, ushort address, bool isM1) =>
        payload is { Length: MsiClawBackButtonProfileProtocol.PayloadLength }
        && MsiClawBackButtonProfileProtocol.ClassifyPayload(payload, address) == (isM1
            ? FrontendMsiBackButtonProfileClassification.ObservedLinuxRightBraceCandidate
            : FrontendMsiBackButtonProfileClassification.ObservedLinuxLeftBraceCandidate);

    private static bool IsKnownDirectInputProfile(byte[]? payload) =>
        payload is { Length: MsiClawBackButtonProfileProtocol.PayloadLength }
        && payload[0] == 0x01 && payload[1] == 0x00;

    private static bool IsManualRestoreModel(string modelId) => modelId is "msi.claw.a2vm.8" or "msi.claw.cg3em";

    private static bool IsAllowedManualRestoreFirmware(string modelId, ushort? firmware) =>
        modelId == "msi.claw.a2vm.8" && firmware == 0x0230
        || modelId == "msi.claw.cg3em" && firmware is 0x0414 or 0x0419;

    private static bool IsRuntimeSafe(Func<bool> source)
    {
        try { return source?.Invoke() == true; }
        catch { return false; }
    }

    private sealed record MsiClawManualRestoreEndpoint(string DevicePath, ushort FirmwareVersion);

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
