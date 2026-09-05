using System.Globalization;
using Windows.Devices.Sensors;

namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

// One-shot WinRT motion-sensor discovery for the diagnostic probe. A candidate is only produced when
// GetDefault() returns a sensor AND a finite live reading can be obtained (docs/gyro/SD6A_CLAW_SENSOR_PROBE_
// CHARACTERIZATION_WORK_ORDER.md section 5.4): a present-but-unreadable sensor is not diagnostic evidence
// worth selecting over a validated legacy candidate.
internal static class ClawSensorProbeWinRtDiscovery
{
    internal static ClawSensorProbeCandidate? TryDiscoverGyrometer()
    {
        try
        {
            var sensor = Gyrometer.GetDefault();
            if (sensor is null || sensor.GetCurrentReading() is null) return null;
            return new ClawSensorProbeCandidate(
                FriendlyName: "WinRT Gyrometer",
                SensorId: sensor.DeviceId,
                TypeGuid: "Unavailable",
                CategoryGuid: "Unavailable",
                MinimumReportInterval: sensor.MinimumReportInterval.ToString(CultureInfo.InvariantCulture),
                Backend: ClawSensorProbeBackend.WinRtGyrometer,
                State: "Ready",
                DevicePath: sensor.DeviceId,
                UnitBasis: ClawSensorProbeUnitBasis.DegreesPerSecond,
                SelectionReason: "WinRT Gyrometer.GetDefault() returned a finite live reading.");
        }
        catch { return null; }
    }

    internal static ClawSensorProbeCandidate? TryDiscoverAccelerometer()
    {
        try
        {
            var sensor = Accelerometer.GetDefault();
            if (sensor is null || sensor.GetCurrentReading() is null) return null;
            return new ClawSensorProbeCandidate(
                FriendlyName: "WinRT Accelerometer",
                SensorId: sensor.DeviceId,
                TypeGuid: "Unavailable",
                CategoryGuid: "Unavailable",
                MinimumReportInterval: sensor.MinimumReportInterval.ToString(CultureInfo.InvariantCulture),
                Backend: ClawSensorProbeBackend.WinRtAccelerometer,
                State: "Ready",
                DevicePath: sensor.DeviceId,
                UnitBasis: ClawSensorProbeUnitBasis.G,
                SelectionReason: "WinRT Accelerometer.GetDefault() returned a finite live reading.");
        }
        catch { return null; }
    }
}

// A read-once-per-poll handle so the reader loop in ClawSensorProbeReaders can treat a WinRT source the same
// way as a legacy COM sensor: acquire fresh at session start (never persist across restarts), poll via
// GetCurrentReading() so ReadDurationMs stays meaningful, and release/reset on Dispose.
internal interface IClawSensorProbeSourceHandle : IDisposable
{
    ClawSensorReportReadResult Read();
}

internal sealed class ClawSensorProbeWinRtSourceHandle : IClawSensorProbeSourceHandle
{
    private readonly Gyrometer? _gyrometer;
    private readonly Accelerometer? _accelerometer;

    private ClawSensorProbeWinRtSourceHandle(Gyrometer gyrometer)
    {
        _gyrometer = gyrometer;
        gyrometer.ReportInterval = Math.Max(gyrometer.MinimumReportInterval, 1);
    }

    private ClawSensorProbeWinRtSourceHandle(Accelerometer accelerometer)
    {
        _accelerometer = accelerometer;
        accelerometer.ReportInterval = Math.Max(accelerometer.MinimumReportInterval, 1);
    }

    internal static IClawSensorProbeSourceHandle OpenGyrometer() =>
        new ClawSensorProbeWinRtSourceHandle(Gyrometer.GetDefault() ?? throw new InvalidOperationException("WinRT Gyrometer is no longer available."));

    internal static IClawSensorProbeSourceHandle OpenAccelerometer() =>
        new ClawSensorProbeWinRtSourceHandle(Accelerometer.GetDefault() ?? throw new InvalidOperationException("WinRT Accelerometer is no longer available."));

    public ClawSensorReportReadResult Read()
    {
        if (_gyrometer is not null)
        {
            var reading = _gyrometer.GetCurrentReading();
            return reading is null ? ClawSensorReportReadResult.NoData() : ClawSensorReportReadResult.Data(reading.AngularVelocityX, reading.AngularVelocityY, reading.AngularVelocityZ, reading.Timestamp);
        }
        var accelReading = _accelerometer!.GetCurrentReading();
        return accelReading is null ? ClawSensorReportReadResult.NoData() : ClawSensorReportReadResult.Data(accelReading.AccelerationX, accelReading.AccelerationY, accelReading.AccelerationZ, accelReading.Timestamp);
    }

    public void Dispose()
    {
        // Return the sensor to its default report interval before releasing (per WinRT guidance) rather than
        // leaving an elevated cadence active for other listeners after this diagnostic session ends.
        try { if (_gyrometer is not null) _gyrometer.ReportInterval = 0; }
        catch { /* best-effort power-conservation reset; session is already tearing down */ }
        try { if (_accelerometer is not null) _accelerometer.ReportInterval = 0; }
        catch { /* best-effort power-conservation reset; session is already tearing down */ }
    }
}

internal sealed class ClawSensorProbeLegacySourceHandle(IntPtr sensor) : IClawSensorProbeSourceHandle
{
    public ClawSensorReportReadResult Read() => ClawSensorProbeSensorApi.ReadXYZ(sensor);
    public void Dispose() { if (sensor != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(sensor); }
}
