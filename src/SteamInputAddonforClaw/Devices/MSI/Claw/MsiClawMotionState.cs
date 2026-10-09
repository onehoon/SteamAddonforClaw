namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed record MsiClawMotionState(
    double GyroXDegPerSecond,
    double GyroYDegPerSecond,
    double GyroZDegPerSecond,
    double AccelXG,
    double AccelYG,
    double AccelZG,
    long GyroReceiveTicks,
    long AccelReceiveTicks,
    DateTimeOffset? GyroSensorTimestamp,
    DateTimeOffset? AccelSensorTimestamp,
    bool HasGyro,
    bool HasAccelerometer,
    string GyroSource,
    string AccelerometerSource)
{
    internal static readonly TimeSpan GyroscopeFreshnessLimit = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan AccelerometerFreshnessLimit = TimeSpan.FromMilliseconds(500);

    internal static MsiClawMotionState Unavailable { get; } = new(
        0, 0, 0, 0, 0, 0,
        0, 0, null, null,
        false, false,
        "Unavailable", "Unavailable");

    internal bool IsUsableForSteamDeckImu => HasGyro && HasAccelerometer;

    internal MsiClawMotionState WithFreshness(long nowTicks, long timestampFrequency)
    {
        if (timestampFrequency <= 0)
            return Unavailable;

        var gyroFresh = HasGyro && IsFresh(GyroReceiveTicks, nowTicks, timestampFrequency, GyroscopeFreshnessLimit);
        var accelerometerFresh = HasAccelerometer && IsFresh(AccelReceiveTicks, nowTicks, timestampFrequency, AccelerometerFreshnessLimit);

        return this with
        {
            GyroXDegPerSecond = gyroFresh ? GyroXDegPerSecond : 0,
            GyroYDegPerSecond = gyroFresh ? GyroYDegPerSecond : 0,
            GyroZDegPerSecond = gyroFresh ? GyroZDegPerSecond : 0,
            GyroReceiveTicks = gyroFresh ? GyroReceiveTicks : 0,
            GyroSensorTimestamp = gyroFresh ? GyroSensorTimestamp : null,
            HasGyro = gyroFresh,
            AccelXG = accelerometerFresh ? AccelXG : 0,
            AccelYG = accelerometerFresh ? AccelYG : 0,
            AccelZG = accelerometerFresh ? AccelZG : 0,
            AccelReceiveTicks = accelerometerFresh ? AccelReceiveTicks : 0,
            AccelSensorTimestamp = accelerometerFresh ? AccelSensorTimestamp : null,
            HasAccelerometer = accelerometerFresh
        };
    }

    internal static bool TryNormalizeSensorAxes(
        double rawX,
        double rawY,
        double rawZ,
        out double normalizedX,
        out double normalizedY,
        out double normalizedZ)
    {
        if (!double.IsFinite(rawX) || !double.IsFinite(rawY) || !double.IsFinite(rawZ))
        {
            normalizedX = 0;
            normalizedY = 0;
            normalizedZ = 0;
            return false;
        }

        normalizedX = rawX;
        normalizedY = rawZ;
        normalizedZ = -rawY;
        return true;
    }

    private static bool IsFresh(long receivedTicks, long nowTicks, long timestampFrequency, TimeSpan freshnessLimit)
    {
        if (receivedTicks > nowTicks)
            return false;

        var ageMilliseconds = (nowTicks - receivedTicks) * 1000d / timestampFrequency;
        return double.IsFinite(ageMilliseconds) && ageMilliseconds <= freshnessLimit.TotalMilliseconds;
    }
}
