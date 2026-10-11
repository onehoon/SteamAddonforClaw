namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum MsiClawBackButtonProfileReadOutcome
{
    ReadbackParsed,
    NoReplyOrTimeout,
    UnexpectedReport,
    WrongAddressOrIndex,
    TransportUnavailable,
    OwnerUnavailable,
    UnsupportedModel,
    Cancelled,
    Failed
}

internal enum MsiClawBackButtonProfileClassification
{
    HistoricalMarkerPresent,
    HistoricalMarkerAbsent,
    ObservedLinuxRightBraceCandidate,
    ObservedLinuxLeftBraceCandidate,
    OtherFormat,
    NotApplicable
}

internal sealed record MsiClawBackButtonProfileSlotResult(
    string Slot,
    ushort Address,
    byte ReadIndex,
    byte RequestedLength,
    MsiClawBackButtonProfileReadOutcome Outcome,
    MsiClawBackButtonProfileClassification Classification,
    string Status);

internal sealed record MsiClawBackButtonProfileReadSet(
    ushort? FirmwareVersion,
    MsiClawBackButtonProfileSlotResult M1,
    MsiClawBackButtonProfileSlotResult M2)
{
    internal byte[]? M1Payload { get; init; }
    internal byte[]? M2Payload { get; init; }
    internal string? ControlDevicePath { get; init; }
}

internal enum MsiClawBackButtonProductionPrepareOutcome
{
    OwnerUnavailable,
    UnsupportedModelOrFirmware,
    AlreadyPrepared,
    MixedOrUnknownProfilePreserved,
    RestoredProfileReadback,
    PartialVolatileWritePossible,
    Unverified,
    Failed
}

internal sealed record MsiClawBackButtonProductionPrepareSlotResult(
    string Slot,
    ushort Address,
    MsiClawBackButtonProfileReadOutcome BeforeOutcome,
    MsiClawBackButtonProfileClassification BeforeClassification,
    string? BeforePayloadHex,
    bool WriteIssued,
    bool WriteSucceeded,
    MsiClawBackButtonProfileReadOutcome? AfterOutcome,
    MsiClawBackButtonProfileClassification? AfterClassification,
    string? AfterPayloadHex);

internal sealed record MsiClawBackButtonProductionPrepareResult(
    MsiClawBackButtonProductionPrepareOutcome Outcome,
    string ModelId,
    ushort? FirmwareVersion,
    string Status,
    MsiClawBackButtonProductionPrepareSlotResult M1,
    MsiClawBackButtonProductionPrepareSlotResult M2,
    bool RomSyncIssued,
    bool RomSyncSucceeded,
    bool ReadbackObserved)
{
    internal bool RomSyncTransportSucceeded => RomSyncSucceeded;
    internal bool WriteTransportSucceeded => M1.WriteSucceeded && M2.WriteSucceeded;
    internal bool ProfileReadbackVerified =>
        WriteTransportSucceeded
        && RomSyncIssued
        && RomSyncSucceeded
        && ReadbackObserved
        && M1.AfterOutcome == MsiClawBackButtonProfileReadOutcome.ReadbackParsed
        && M2.AfterOutcome == MsiClawBackButtonProfileReadOutcome.ReadbackParsed
        && M1.AfterPayloadHex?.StartsWith("0100", StringComparison.OrdinalIgnoreCase) == true
        && M2.AfterPayloadHex?.StartsWith("0100", StringComparison.OrdinalIgnoreCase) == true;
}
