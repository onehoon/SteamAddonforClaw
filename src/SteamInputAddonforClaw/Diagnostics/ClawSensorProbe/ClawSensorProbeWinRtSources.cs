using System.Globalization;
using Windows.Devices.Sensors;

namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

// One-shot WinRT motion-sensor discovery for the diagnostic probe. A candidate is only produced when
// GetDefault() returns a sensor AND a finite live reading can be obtained (docs/gyro/SD6A_CLAW_SENSOR_PROBE_
// CHARACTERIZATION_WORK_ORDER.md section 5.4): a present-but-unreadable sensor is not diagnostic evidence
// worth selecting over a validated legacy candidate. The full evidence (available/HResult/failure) is
// preserved even when unavailable, so a real WinRT absence/exception stays visible in the finalized report
// instead of collapsing to "no candidate" alongside a healthy legacy path.
internal static class ClawSensorProbeWinRtDiscovery
{
    internal static ClawSensorProbeWinRtEvidence ProbeGyrometer()
    {
        try
        {
            var sensor = Gyrometer.GetDefault();
            if (sensor is null) return new(false, null, "Unavailable", null);
            if (sensor.GetCurrentReading() is null) return new(false, null, "No live reading was returned.", null);
            return new(true, null, null, new ClawSensorProbeCandidate(
                FriendlyName: "WinRT Gyrometer",
                SensorId: sensor.DeviceId,
                TypeGuid: "Unavailable",
                CategoryGuid: "Unavailable",
                MinimumReportInterval: sensor.MinimumReportInterval.ToString(CultureInfo.InvariantCulture),
                Backend: ClawSensorProbeBackend.WinRtGyrometer,
                State: "Ready",
                DevicePath: sensor.DeviceId,
                UnitBasis: ClawSensorProbeUnitBasis.DegreesPerSecond,
                SelectionReason: "WinRT Gyrometer.GetDefault() returned a finite live reading."));
        }
        catch (Exception exception) { return new(false, exception.HResult, exception.GetType().Name, null); }
    }

    internal static ClawSensorProbeWinRtEvidence ProbeAccelerometer()
    {
        try
        {
            var sensor = Accelerometer.GetDefault();
            if (sensor is null) return new(false, null, "Unavailable", null);
            if (sensor.GetCurrentReading() is null) return new(false, null, "No live reading was returned.", null);
            return new(true, null, null, new ClawSensorProbeCandidate(
                FriendlyName: "WinRT Accelerometer",
                SensorId: sensor.DeviceId,
                TypeGuid: "Unavailable",
                CategoryGuid: "Unavailable",
                MinimumReportInterval: sensor.MinimumReportInterval.ToString(CultureInfo.InvariantCulture),
                Backend: ClawSensorProbeBackend.WinRtAccelerometer,
                State: "Ready",
                DevicePath: sensor.DeviceId,
                UnitBasis: ClawSensorProbeUnitBasis.G,
                SelectionReason: "WinRT Accelerometer.GetDefault() returned a finite live reading."));
        }
        catch (Exception exception) { return new(false, exception.HResult, exception.GetType().Name, null); }
    }
}

// A read-once-per-poll handle so the reader loop in ClawSensorProbeReaders can treat a WinRT source the same
// way as a legacy COM sensor: acquire fresh at session start (never persist across restarts), poll via
// GetCurrentReading() so ReadDurationMs stays meaningful, and release/reset on Dispose.
internal interface IClawSensorProbeSourceHandle : IDisposable
{
    ClawSensorReportReadResult Read();
    ClawSensorProbeSourceConfiguration Configuration { get; }
}

internal sealed class ClawSensorProbeWinRtSourceHandle : IClawSensorProbeSourceHandle
{
    private readonly Gyrometer? _gyrometer;
    private readonly Accelerometer? _accelerometer;
    public ClawSensorProbeSourceConfiguration Configuration { get; }

    private ClawSensorProbeWinRtSourceHandle(Gyrometer gyrometer)
    {
        _gyrometer = gyrometer;
        var minimum = gyrometer.MinimumReportInterval;
        var requested = Math.Max(minimum, 1u);
        gyrometer.ReportInterval = requested;
        Configuration = new(ClawSensorProbeBackend.WinRtGyrometer, minimum, requested, gyrometer.ReportInterval);
    }

    private ClawSensorProbeWinRtSourceHandle(Accelerometer accelerometer)
    {
        _accelerometer = accelerometer;
        var minimum = accelerometer.MinimumReportInterval;
        var requested = Math.Max(minimum, 1u);
        accelerometer.ReportInterval = requested;
        Configuration = new(ClawSensorProbeBackend.WinRtAccelerometer, minimum, requested, accelerometer.ReportInterval);
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
    // Legacy Sensor API has no requested/effective report-interval negotiation concept; the sensor's own
    // minimum interval is already visible on the selected candidate (MinimumReportInterval), so it is not
    // duplicated here.
    public ClawSensorProbeSourceConfiguration Configuration { get; } = new(ClawSensorProbeBackend.LegacySensorApi, null, null, null);
    public ClawSensorReportReadResult Read() => ClawSensorProbeSensorApi.ReadXYZ(sensor);
    public void Dispose() { if (sensor != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(sensor); }
}
