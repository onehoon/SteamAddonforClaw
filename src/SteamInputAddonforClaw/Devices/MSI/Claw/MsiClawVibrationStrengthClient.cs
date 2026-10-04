using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using Windows.Devices.HumanInterfaceDevice;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal readonly record struct MsiClawVibrationStrengthValues(int LeftPercent, int RightPercent);

internal sealed record MsiClawVibrationStrengthReadResult(
    bool Succeeded,
    MsiClawVibrationStrengthValues? Values,
    string Reason);

internal enum MsiClawVibrationStrengthMutationOutcome { Succeeded, Unavailable, Failed }

internal sealed record MsiClawVibrationStrengthMutationResult(
    MsiClawVibrationStrengthMutationOutcome Outcome,
    MsiClawVibrationStrengthValues? Values,
    string Reason)
{
    internal bool Succeeded => Outcome == MsiClawVibrationStrengthMutationOutcome.Succeeded;
}

internal interface IMsiClawVibrationProfileIo
{
    Task<bool> WriteAsync(
        MsiClawControlHidDevice device,
        ReadOnlyMemory<byte> report,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<byte[]>?> WriteAndReadAsync(
        MsiClawControlHidDevice device,
        ReadOnlyMemory<byte> report,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class WindowsMsiClawVibrationProfileIo : IMsiClawVibrationProfileIo
{
    private readonly IMsiClawHidDeviceInformationLookup _lookup;
    private readonly IMsiClawRawHidTransport _transport;

    internal WindowsMsiClawVibrationProfileIo(
        IMsiClawHidDeviceInformationLookup? lookup = null,
        IMsiClawRawHidTransport? transport = null)
    {
        _lookup = lookup ?? new WindowsMsiClawHidDeviceInformationLookup();
        _transport = transport ?? new WindowsMsiClawRawHidTransport();
    }

    public async Task<bool> WriteAsync(
        MsiClawControlHidDevice device,
        ReadOnlyMemory<byte> report,
        CancellationToken cancellationToken)
    {
        var path = await ResolveCurrentDevicePathAsync(device, cancellationToken).ConfigureAwait(false);
        return path is not null
            && await _transport.WriteAsync(path, report, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<byte[]>?> WriteAndReadAsync(
        MsiClawControlHidDevice device,
        ReadOnlyMemory<byte> report,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var path = await ResolveCurrentDevicePathAsync(device, cancellationToken).ConfigureAwait(false);
        return path is null
            ? null
            : await _transport.WriteAndReadAsync(
                path,
                report,
                MsiClawVibrationProfileCommand.ReportLength,
                maxReports: 4,
                timeout,
                cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ResolveCurrentDevicePathAsync(
        MsiClawControlHidDevice device,
        CancellationToken cancellationToken)
    {
        if (device.VerifiedIdentity.Confidence != MsiClawIdentityConfidence.Strong
            || !MsiClawPhysicalIdentity.From(device.Device).StronglyMatches(device.VerifiedIdentity))
            return null;

        var selector = HidDevice.GetDeviceSelector(
            device.UsagePage,
            device.Usage,
            MsiClawHardware.VendorId,
            device.Device.ProductId ?? 0);
        var infos = await _lookup.FindAsync(selector, cancellationToken).ConfigureAwait(false);
        return WindowsMsiClawModeWriter.SelectDeviceInformation(device, infos)?.Id;
    }
}

/// <summary>Probes candidate MSI profile bytes for diagnostics and permits writes only when the
/// model-specific firmware-address policy verifies their meaning. Each operation resolves a fresh,
/// strongly identified command HID; no device path or firmware value is cached.</summary>
internal sealed class MsiClawVibrationStrengthClient
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(300);
    private readonly IControllerDeviceEnumerator _deviceEnumerator;
    private readonly MsiClawControlHidResolver _resolver;
    private readonly IMsiClawVibrationProfileIo _io;
    private readonly HandheldDeviceModelId _modelId;
    private readonly SemaphoreSlim _transactionGate = new(1, 1);

    internal MsiClawVibrationStrengthClient(
        HandheldDeviceModelId modelId,
        IControllerDeviceEnumerator deviceEnumerator,
        MsiClawControlHidResolver resolver,
        IMsiClawVibrationProfileIo io)
    {
        _modelId = modelId;
        _deviceEnumerator = deviceEnumerator ?? throw new ArgumentNullException(nameof(deviceEnumerator));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _io = io ?? throw new ArgumentNullException(nameof(io));
    }

    internal async Task<MsiClawVibrationStrengthReadResult> CaptureAsync(CancellationToken cancellationToken)
    {
        await _transactionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = ResolveCurrentCommandHid();
            if (device is null)
            {
                var reason = MsiClawVibrationFirmwarePolicy.IsDirectMotorProfileAddressVerified(_modelId)
                    ? "CommandHidNotUniquelyResolved"
                    : "FirmwareAddressMappingUnverified";
                if (reason == "FirmwareAddressMappingUnverified")
                    AppLog.Info("ControllerVibration", "ControllerVibrationProfileProbeUnavailable",
                        ("Model", _modelId.Value), ("Reason", "CommandHidNotUniquelyResolved"), ("VerifiedForProduction", false));
                return new(false, null, reason);
            }

            if (!MsiClawVibrationFirmwarePolicy.IsDirectMotorProfileAddressVerified(_modelId))
            {
                var diagnosticReadSucceeded = await TryReadDiagnosticPairAsync(device, cancellationToken).ConfigureAwait(false);
                AppLog.Info("ControllerVibration", "ControllerVibrationCaptureUnavailable",
                    ("Model", _modelId.Value),
                    ("Reason", "FirmwareAddressMappingUnverified"),
                    ("DiagnosticReadSucceeded", diagnosticReadSucceeded));
                return new(false, null, "FirmwareAddressMappingUnverified");
            }

            var values = await TryReadPairAsync(device, cancellationToken).ConfigureAwait(false);
            if (values is null)
                return new(false, null, "FirmwareProfileReadFailed");

            AppLog.Debug("ControllerVibration", "ControllerVibrationCaptureSucceeded",
                ("LeftPercent", values.Value.LeftPercent), ("RightPercent", values.Value.RightPercent));
            return new(true, values, "FirmwareReadbackVerified");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Info("ControllerVibration", "ControllerVibrationCaptureUnavailable",
                ("Reason", exception.GetType().Name));
            return new(false, null, exception.GetType().Name);
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    internal async Task<MsiClawVibrationStrengthMutationResult> SetAsync(
        int leftPercent,
        int rightPercent,
        Func<bool> centerMIsExactlyDisabled,
        CancellationToken cancellationToken)
    {
        if (leftPercent is < 0 or > 100 || rightPercent is < 0 or > 100)
            return new(MsiClawVibrationStrengthMutationOutcome.Failed, null, "InvalidPercent");

        if (!MsiClawVibrationFirmwarePolicy.IsDirectMotorProfileAddressVerified(_modelId))
            return new(MsiClawVibrationStrengthMutationOutcome.Unavailable, null, "FirmwareAddressMappingUnverified");

        await _transactionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        MsiClawControlHidDevice? transactionDevice = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            transactionDevice = ResolveCurrentCommandHid();
            if (transactionDevice is null)
                return new(MsiClawVibrationStrengthMutationOutcome.Unavailable, null, "CommandHidNotUniquelyResolved");

            var current = await TryReadPairAsync(transactionDevice, cancellationToken).ConfigureAwait(false);
            if (current is null)
                return new(MsiClawVibrationStrengthMutationOutcome.Failed, null, "InitialFirmwareProfileReadFailed");

            if (!IsCenterMDisabled(centerMIsExactlyDisabled))
            {
                AppLog.Info("ControllerVibration", "ControllerVibrationMutationFailed",
                    ("Reason", "CenterMIsNotExactlyDisabled"),
                    ("LeftPercent", current.Value.LeftPercent), ("RightPercent", current.Value.RightPercent));
                return new(MsiClawVibrationStrengthMutationOutcome.Unavailable, current, "CenterMIsNotExactlyDisabled");
            }

            AppLog.Info("ControllerVibration", "ControllerVibrationMutationStarted",
                ("LeftPercent", leftPercent), ("RightPercent", rightPercent));

            if (current.Value.LeftPercent != leftPercent
                && !await CommitChannelAsync(transactionDevice, MsiClawVibrationProfileCommand.LeftMotorAddress, leftPercent,
                    centerMIsExactlyDisabled, cancellationToken).ConfigureAwait(false))
                return await FailedAfterReadbackAsync(transactionDevice, "LeftMotorCommitFailed", cancellationToken).ConfigureAwait(false);

            if (current.Value.RightPercent != rightPercent
                && !await CommitChannelAsync(transactionDevice, MsiClawVibrationProfileCommand.RightMotorAddress, rightPercent,
                    centerMIsExactlyDisabled, cancellationToken).ConfigureAwait(false))
                return await FailedAfterReadbackAsync(transactionDevice, "RightMotorCommitFailed", cancellationToken).ConfigureAwait(false);

            var final = await TryReadPairAsync(transactionDevice, cancellationToken).ConfigureAwait(false);
            if (final is null)
                return Failed("FinalFirmwareProfileReadFailed", null);

            if (final.Value.LeftPercent != leftPercent || final.Value.RightPercent != rightPercent)
                return Failed("FinalFirmwareProfileMismatch", final);

            AppLog.Info("ControllerVibration", "ControllerVibrationMutationSucceeded",
                ("LeftPercent", final.Value.LeftPercent), ("RightPercent", final.Value.RightPercent));
            return new(MsiClawVibrationStrengthMutationOutcome.Succeeded, final, "FirmwareReadbackVerified");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            MsiClawVibrationStrengthValues? actual = null;
            if (transactionDevice is not null)
            {
                try { actual = await TryReadPairAsync(transactionDevice, CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
            AppLog.Info("ControllerVibration", "ControllerVibrationMutationFailed",
                ("Reason", exception.GetType().Name));
            return Failed(exception.GetType().Name, actual);
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    private async Task<bool> CommitChannelAsync(
        MsiClawControlHidDevice device,
        ushort address,
        int requestedPercent,
        Func<bool> centerMIsExactlyDisabled,
        CancellationToken cancellationToken)
    {
        if (!IsCenterMDisabled(centerMIsExactlyDisabled))
            return false;

        var write = MsiClawVibrationProfileCommand.BuildWriteProfile(address, requestedPercent);
        if (!await _io.WriteAsync(device, write, cancellationToken).ConfigureAwait(false))
            return false;

        if (!IsCenterMDisabled(centerMIsExactlyDisabled))
            return false;

        if (!await _io.WriteAsync(device, MsiClawVibrationProfileCommand.BuildSyncToRom(), cancellationToken).ConfigureAwait(false))
            return false;

        var verified = await TryReadValueAsync(device, address, cancellationToken).ConfigureAwait(false);
        return verified == requestedPercent;
    }

    private async Task<MsiClawVibrationStrengthMutationResult> FailedAfterReadbackAsync(
        MsiClawControlHidDevice device,
        string reason,
        CancellationToken cancellationToken)
    {
        var actual = await TryReadPairAsync(device, cancellationToken).ConfigureAwait(false);
        return Failed(reason, actual);
    }

    private static MsiClawVibrationStrengthMutationResult Failed(
        string reason,
        MsiClawVibrationStrengthValues? actual) =>
        new(MsiClawVibrationStrengthMutationOutcome.Failed, actual, reason);

    private MsiClawControlHidDevice? ResolveCurrentCommandHid()
    {
        try
        {
            var devices = _deviceEnumerator.EnumeratePresentDevices();
            var stronglyIdentifiedControllers = devices
                .Where(device => device.Present && MsiClawHardware.IsKnownController(device.VendorId, device.ProductId))
                .Select(MsiClawPhysicalIdentity.From)
                .Where(identity => identity.Confidence == MsiClawIdentityConfidence.Strong)
                .ToArray();
            var physicalKeys = stronglyIdentifiedControllers
                .Select(IdentityKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (physicalKeys.Length != 1)
                return null;

            var candidates = devices.Where(device =>
                device.Present
                && IsVibrationCommandEndpoint(device)
                && string.Equals(IdentityKey(MsiClawPhysicalIdentity.From(device)), physicalKeys[0], StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Length != 1)
                return null;

            var identity = MsiClawPhysicalIdentity.From(candidates[0]);
            return _resolver.ResolveCommand(candidates, identity);
        }
        catch (Exception exception)
        {
            AppLog.Debug("ControllerVibration", "Controller vibration command HID resolution failed.",
                ("Reason", exception.GetType().Name));
            return null;
        }
    }

    private static string IdentityKey(MsiClawPhysicalIdentity identity) =>
        !string.IsNullOrWhiteSpace(identity.PhysicalDeviceKey)
            ? "root:" + identity.PhysicalDeviceKey
            : "container:" + identity.ContainerId?.ToString("D") + "|parent:" + identity.ParentInstanceId;

    private static bool IsVibrationCommandEndpoint(ControllerDeviceInfo device) =>
        device.VendorId == MsiClawHardware.VendorId
        && (device.ProductId == MsiClawHardware.XInputProductId && device.UsagePage == 0xFFA0 && device.Usage == 0x0001
            || device.ProductId == MsiClawHardware.DirectInputProductId
                && device.UsagePage == MsiClawHardware.DirectInputControlUsagePage
                && device.Usage == MsiClawHardware.DirectInputControlUsage)
        && MsiClawPhysicalIdentity.From(device).Confidence == MsiClawIdentityConfidence.Strong;

    private async Task<MsiClawVibrationStrengthValues?> TryReadPairAsync(
        MsiClawControlHidDevice device,
        CancellationToken cancellationToken)
    {
        var left = await TryReadValueAsync(device, MsiClawVibrationProfileCommand.LeftMotorAddress, cancellationToken).ConfigureAwait(false);
        if (left is null)
            return null;
        var right = await TryReadValueAsync(device, MsiClawVibrationProfileCommand.RightMotorAddress, cancellationToken).ConfigureAwait(false);
        return right is null ? null : new(left.Value, right.Value);
    }

    private async Task<bool> TryReadDiagnosticPairAsync(
        MsiClawControlHidDevice device,
        CancellationToken cancellationToken)
    {
        var index0Succeeded = await TryReadDiagnosticIndexAsync(device, 0x00, cancellationToken).ConfigureAwait(false);
        var index1Succeeded = await TryReadDiagnosticIndexAsync(device, 0x01, cancellationToken).ConfigureAwait(false);
        return index0Succeeded && index1Succeeded;
    }

    private async Task<bool> TryReadDiagnosticIndexAsync(
        MsiClawControlHidDevice device,
        byte profileIndex,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<byte[]>? reports;
        try
        {
            reports = await _io.WriteAndReadAsync(
                device,
                MsiClawVibrationProfileCommand.BuildDiagnosticReadProfile(
                    profileIndex,
                    MsiClawVibrationProfileCommand.LeftMotorAddress,
                    length: 0x02),
                ReadTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogDiagnosticIndexProbe(device, profileIndex, null, null, structuralParseSucceeded: false, exception.GetType().Name);
            return false;
        }

        if (reports is null || reports.Count == 0)
        {
            LogDiagnosticIndexProbe(device, profileIndex, null, null, structuralParseSucceeded: false, "NoResponse");
            return false;
        }

        var structuralParseSucceeded = false;
        foreach (var report in reports.Take(4))
        {
            var parsed = MsiClawVibrationProfileCommand.TryParseDiagnosticReadProfileResponse(
                report,
                profileIndex,
                MsiClawVibrationProfileCommand.LeftMotorAddress,
                requestedLength: 0x02,
                out var diagnosticResponse);
            LogDiagnosticIndexProbe(
                device,
                profileIndex,
                report,
                parsed ? diagnosticResponse : null,
                parsed,
                probeFailure: null);
            structuralParseSucceeded |= parsed;
        }
        return structuralParseSucceeded;
    }

    private void LogDiagnosticIndexProbe(
        MsiClawControlHidDevice device,
        byte requestedProfileIndex,
        byte[]? response,
        MsiClawVibrationProfileDiagnosticResponse? parsedResponse,
        bool structuralParseSucceeded,
        string? probeFailure)
    {
        var responsePrefix = response is null
            ? string.Empty
            : string.Join("-", response.Take(11).Select(value => value.ToString("X2")));
        byte? responseIndex = response is { Length: > 5 } ? response[5] : null;
        var responseAddress = response is { Length: > 7 }
            ? $"0x{((response[6] << 8) | response[7]):X4}"
            : null;
        byte? responseLength = response is { Length: > 8 } ? response[8] : null;
        bool? indexEchoMatched = responseIndex is { } actualIndex
            ? actualIndex == requestedProfileIndex
            : null;

        AppLog.Info("ControllerVibration", "ControllerVibrationProfileIndexProbe",
            ("Model", _modelId.Value),
            ("ProductId", device.Device.ProductId is { } productId ? $"0x{productId:X4}" : null),
            ("UsagePage", $"0x{device.UsagePage:X4}"),
            ("Usage", $"0x{device.Usage:X4}"),
            ("RequestIndex", requestedProfileIndex),
            ("Address", $"0x{MsiClawVibrationProfileCommand.LeftMotorAddress:X4}"),
            ("RequestedLength", 2),
            ("ResponsePrefix", responsePrefix),
            ("ResponseReportLength", response?.Length),
            ("ResponseIndex", responseIndex),
            ("IndexEchoMatched", indexEchoMatched),
            ("ResponseAddress", responseAddress),
            ("ResponseLength", responseLength),
            ("CandidateLeft", parsedResponse?.CandidateLeft),
            ("CandidateRight", parsedResponse?.CandidateRight),
            ("StructuralParseSucceeded", structuralParseSucceeded),
            ("VerifiedForProduction", false),
            ("ProbeFailure", probeFailure));
    }

    private async Task<int?> TryReadValueAsync(
        MsiClawControlHidDevice device,
        ushort address,
        CancellationToken cancellationToken)
    {
        var reports = await _io.WriteAndReadAsync(
            device,
            MsiClawVibrationProfileCommand.BuildReadProfile(address),
            ReadTimeout,
            cancellationToken).ConfigureAwait(false);
        if (reports is null)
            return null;

        foreach (var report in reports)
            if (MsiClawVibrationProfileCommand.TryParseReadProfileResponse(report, address, out var percent))
                return percent;

        return null;
    }

    private static bool IsCenterMDisabled(Func<bool> centerMIsExactlyDisabled)
    {
        try { return centerMIsExactlyDisabled(); }
        catch { return false; }
    }
}
