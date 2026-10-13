using SteamInputAddonforClaw.Contracts.ControllerVibration;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using Windows.Devices.HumanInterfaceDevice;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

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

/// <summary>Applies the persisted pair to one freshly resolved, strongly identified PID1902 control HID.</summary>
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
    internal string ProductionUnavailableReason => MsiClawVibrationProfilePolicy.GetProductionUnavailableReason(_modelId);

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

}
