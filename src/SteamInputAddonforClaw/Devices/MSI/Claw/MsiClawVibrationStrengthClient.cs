using SteamInputAddonforClaw.Contracts.ControllerVibration;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using Windows.Devices.HumanInterfaceDevice;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum MsiClawVibrationProfileWriteProbeMode { ApplyZeroHundred, RestoreFiftyFifty }
internal enum MsiClawVibrationProfileWriteProbeOutcome { Succeeded, Unavailable, Failed }

internal sealed record MsiClawVibrationProfileWriteProbeResult(
    MsiClawVibrationProfileWriteProbeMode Mode,
    MsiClawVibrationProfileWriteProbeOutcome Outcome,
    int LeftPercent,
    int RightPercent,
    string Reason)
{
    internal bool Succeeded => Outcome == MsiClawVibrationProfileWriteProbeOutcome.Succeeded;
}

internal interface IMsiClawVibrationProfileIo
{
    Task<bool> WriteAsync(
        MsiClawControlHidDevice device,
        ReadOnlyMemory<byte> report,
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
            MsiClawHardware.DirectInputProductId);
        var infos = await _lookup.FindAsync(selector, cancellationToken).ConfigureAwait(false);
        return WindowsMsiClawModeWriter.SelectDeviceInformation(device, infos)?.Id;
    }
}

/// <summary>Applies the persisted pair to one freshly resolved, strongly identified PID1902 control HID.
/// Developer-only profile probes share the same transaction gate.</summary>
internal sealed class MsiClawVibrationStrengthClient
{
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

    internal bool IsProductionPairWriteVerified => MsiClawVibrationProfilePolicy.IsProductionPairWriteVerified(_modelId);
    internal string ModelId => _modelId.Value;

    internal async Task<bool> ApplyAsync(
        ControllerVibrationSettings settings,
        MsiClawPhysicalIdentity expectedIdentity,
        CancellationToken cancellationToken)
    {
        if (ControllerVibrationSettingsValidation.Validate(settings) is { } invalid)
            return FailApply(invalid);
        if (!IsProductionPairWriteVerified)
            return FailApply("ProductionPairWriteNotVerifiedForModel");
        if (!IsStrongOwnedPid1902Identity(expectedIdentity))
            return FailApply("StrongOwnedPid1902IdentityUnavailable");

        await _transactionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = ResolveCurrentPid1902ControlHid(expectedIdentity);
            if (device is null)
                return FailApply("ExactPid1902ControlHidUnavailableOrAmbiguous");

            var report = MsiClawVibrationProfileCommand.BuildMotorPairWrite(
                settings.LeftPercent, settings.RightPercent);
            var succeeded = await _io.WriteAsync(device, report, cancellationToken).ConfigureAwait(false);
            if (!succeeded)
                return FailApply("PairWriteFailed");

            AppLog.Debug("ControllerVibration", "Controller vibration pair write transport succeeded.",
                ("Model", _modelId.Value), ("Left", settings.LeftPercent), ("Right", settings.RightPercent));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return FailApply(exception.GetType().Name);
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    internal async Task<MsiClawVibrationProfileWriteProbeResult> RunDiagnosticMotorPairWriteAsync(
        MsiClawVibrationProfileWriteProbeMode mode,
        Func<bool> centerMIsExactlyDisabled,
        CancellationToken cancellationToken)
    {
        var pair = mode switch
        {
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred => (Left: 0, Right: 100),
            MsiClawVibrationProfileWriteProbeMode.RestoreFiftyFifty => (Left: 50, Right: 50),
            _ => ((int Left, int Right)?)null
        };
        if (pair is null)
            return DiagnosticProbeResult(mode, MsiClawVibrationProfileWriteProbeOutcome.Failed, 0, 0, "InvalidProbeMode");

        await _transactionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_modelId.Value != "msi.claw.cg3em")
                return DiagnosticProbeUnavailable(mode, pair.Value, "UnsupportedModel");

            if (!IsCenterMDisabled(centerMIsExactlyDisabled))
                return DiagnosticProbeUnavailable(mode, pair.Value, "CenterMIsNotExactlyDisabled");

            var device = ResolveCurrentPid1902ControlHid(null);
            if (device is null)
                return DiagnosticProbeUnavailable(mode, pair.Value, "Pid1902ControlHidNotUniquelyResolved");

            // Recheck immediately before the one write so a changed startup authority cannot
            // authorize this developer-only mutation using an earlier observation.
            if (!IsCenterMDisabled(centerMIsExactlyDisabled))
                return DiagnosticProbeUnavailable(mode, pair.Value, "CenterMIsNotExactlyDisabled");

            var report = MsiClawVibrationProfileCommand.BuildMotorPairWrite(
                pair.Value.Left, pair.Value.Right);
            AppLog.Info("ControllerVibration", "ControllerVibrationProfileWriteProbeStarted",
                ("Model", _modelId.Value),
                ("ProductId", $"0x{device.Device.ProductId:X4}"),
                ("ProfileIndex", 1),
                ("Address", "0x0022"),
                ("Length", 2),
                ("Left", pair.Value.Left),
                ("Right", pair.Value.Right),
                ("SyncToRom", false),
                ("VerifiedForProduction", false));

            var transportSucceeded = await _io.WriteAsync(device, report, cancellationToken).ConfigureAwait(false);
            AppLog.Info("ControllerVibration", "ControllerVibrationProfileWriteProbeCompleted",
                ("Left", pair.Value.Left),
                ("Right", pair.Value.Right),
                ("TransportSucceeded", transportSucceeded),
                ("SyncToRom", false),
                ("VerifiedForProduction", false));

            return transportSucceeded
                ? DiagnosticProbeResult(mode, MsiClawVibrationProfileWriteProbeOutcome.Succeeded,
                    pair.Value.Left, pair.Value.Right, "TransportWriteSucceeded")
                : DiagnosticProbeResult(mode, MsiClawVibrationProfileWriteProbeOutcome.Failed,
                    pair.Value.Left, pair.Value.Right, "TransportWriteFailed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Info("ControllerVibration", "ControllerVibrationProfileWriteProbeCompleted",
                ("Left", pair.Value.Left),
                ("Right", pair.Value.Right),
                ("TransportSucceeded", false),
                ("SyncToRom", false),
                ("VerifiedForProduction", false),
                ("Reason", exception.GetType().Name));
            return DiagnosticProbeResult(mode, MsiClawVibrationProfileWriteProbeOutcome.Failed,
                pair.Value.Left, pair.Value.Right, exception.GetType().Name);
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    private MsiClawControlHidDevice? ResolveCurrentPid1902ControlHid(MsiClawPhysicalIdentity? expectedIdentity)
    {
        if (expectedIdentity is not null && !IsStrongOwnedPid1902Identity(expectedIdentity))
            return null;

        try
        {
            var devices = _deviceEnumerator.EnumeratePresentDevices()
                .Where(device => device.Present
                    && device.VendorId == MsiClawHardware.VendorId
                    && device.ProductId == MsiClawHardware.DirectInputProductId
                    && device.UsagePage == MsiClawHardware.DirectInputControlUsagePage
                    && device.Usage == MsiClawHardware.DirectInputControlUsage)
                .Where(device => expectedIdentity is null
                    ? MsiClawPhysicalIdentity.From(device).Confidence == MsiClawIdentityConfidence.Strong
                    : MsiClawPhysicalIdentity.From(device).StronglyMatches(expectedIdentity))
                .ToArray();
            if (devices.Length != 1)
                return null;

            var identity = expectedIdentity ?? MsiClawPhysicalIdentity.From(devices[0]);
            var resolved = _resolver.ResolveCommand(devices, identity);
            return resolved is not null
                && resolved.Device.VendorId == MsiClawHardware.VendorId
                && resolved.Device.ProductId == MsiClawHardware.DirectInputProductId
                && resolved.UsagePage == MsiClawHardware.DirectInputControlUsagePage
                && resolved.Usage == MsiClawHardware.DirectInputControlUsage
                && resolved.VerifiedIdentity.Confidence == MsiClawIdentityConfidence.Strong
                && MsiClawPhysicalIdentity.From(resolved.Device).StronglyMatches(identity)
                ? resolved
                : null;
        }
        catch (Exception exception)
        {
            AppLog.Debug("ControllerVibration", "Controller vibration control HID resolution failed.",
                ("Reason", exception.GetType().Name));
            return null;
        }
    }

    private static bool IsStrongOwnedPid1902Identity(MsiClawPhysicalIdentity identity) =>
        identity.Confidence == MsiClawIdentityConfidence.Strong
        && identity.VendorId == MsiClawHardware.VendorId
        && identity.ProductId == MsiClawHardware.DirectInputProductId
        && (!string.IsNullOrWhiteSpace(identity.PhysicalDeviceKey) || MsiClawPhysicalIdentity.IsUsableContainer(identity.ContainerId));

    private bool FailApply(string reason)
    {
        AppLog.Warn("ControllerVibration", "Persisted vibration pair was not applied; controller ownership remains unchanged.", null,
            ("Event", "ControllerVibrationSettingsApplyFailed"), ("Model", _modelId.Value), ("Reason", reason));
        return false;
    }

    private MsiClawVibrationProfileWriteProbeResult DiagnosticProbeUnavailable(
        MsiClawVibrationProfileWriteProbeMode mode,
        (int Left, int Right) pair,
        string reason)
    {
        AppLog.Info("ControllerVibration", "ControllerVibrationProfileWriteProbeUnavailable",
            ("Model", _modelId.Value),
            ("Reason", reason),
            ("Left", pair.Left),
            ("Right", pair.Right),
            ("SyncToRom", false),
            ("VerifiedForProduction", false));
        return DiagnosticProbeResult(mode, MsiClawVibrationProfileWriteProbeOutcome.Unavailable,
            pair.Left, pair.Right, reason);
    }

    private static MsiClawVibrationProfileWriteProbeResult DiagnosticProbeResult(
        MsiClawVibrationProfileWriteProbeMode mode,
        MsiClawVibrationProfileWriteProbeOutcome outcome,
        int left,
        int right,
        string reason) => new(mode, outcome, left, right, reason);

    private static bool IsCenterMDisabled(Func<bool> centerMIsExactlyDisabled)
    {
        try { return centerMIsExactlyDisabled?.Invoke() == true; }
        catch { return false; }
    }

}
