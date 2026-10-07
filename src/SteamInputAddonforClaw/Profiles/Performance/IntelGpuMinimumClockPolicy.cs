namespace SteamInputAddonforClaw.Profiles.Performance;

internal readonly record struct IntelGpuFrequencyRange(double Min, double Max);

internal sealed record IntelGpuMinimumClockCapability(
    bool Available,
    string? UnavailableReason,
    string AdapterName,
    uint VendorId,
    uint DeviceId,
    bool CanControl,
    double HardwareMinMhz,
    double HardwareMaxMhz,
    IReadOnlyList<double> AvailableClocksMhz,
    IReadOnlyList<double> SelectableClocksMhz,
    double? SelectableMinMhz,
    double? SelectableMaxMhz,
    double? RecommendedDefaultMhz);

internal static class IntelGpuMinimumClockPolicy
{
    internal const double MinimumUsefulGamingClockMhz = 1500.0;
    internal const double FrequencyToleranceMhz = 0.1;

    internal static ClockSelection SelectAvailableClocks(IEnumerable<double>? clocks)
    {
        if (clocks is null)
            return ClockSelection.Unavailable("IGCL did not return an available-clock list.");

        var normalized = new List<double>();
        foreach (var value in clocks.Where(static value => double.IsFinite(value) && value > 0).OrderBy(static value => value))
        {
            if (normalized.Count == 0 || Math.Abs(value - normalized[^1]) > FrequencyToleranceMhz)
                normalized.Add(value);
        }

        if (normalized.Count < 3)
            return ClockSelection.Unavailable("IGCL returned fewer than three distinct valid GPU clocks.", normalized);

        var upper = normalized[^3];
        var lowerIndex = normalized.FindIndex(static value => value >= MinimumUsefulGamingClockMhz);
        if (lowerIndex < 0 || normalized[lowerIndex] > upper)
            return ClockSelection.Unavailable("No selectable GPU clock exists between the useful-gaming threshold and the dynamic upper bound.", normalized);

        var selectable = normalized
            .Where(value => value >= normalized[lowerIndex] && value <= upper)
            .ToArray();
        if (selectable.Length == 0)
            return ClockSelection.Unavailable("The available GPU clocks produced an empty selectable range.", normalized);

        return new(
            true,
            null,
            Array.AsReadOnly(normalized.ToArray()),
            Array.AsReadOnly(selectable),
            selectable[0],
            selectable[^1],
            selectable[^1]);
    }

    internal static bool IsHardwareRangeValid(double minimumMhz, double maximumMhz) =>
        double.IsFinite(minimumMhz)
        && double.IsFinite(maximumMhz)
        && maximumMhz > 0
        && minimumMhz <= maximumMhz;

    internal static bool TryGetCanonicalTarget(
        IEnumerable<double> selectableClocksMhz,
        double targetMhz,
        out double canonicalTargetMhz)
    {
        canonicalTargetMhz = default;
        if (!double.IsFinite(targetMhz)) return false;

        foreach (var selectable in selectableClocksMhz)
        {
            if (double.IsFinite(selectable) && Math.Abs(selectable - targetMhz) <= FrequencyToleranceMhz)
            {
                canonicalTargetMhz = selectable;
                return true;
            }
        }

        return false;
    }

    internal static bool TryCreateMinimumRequest(
        IEnumerable<double> selectableClocksMhz,
        double targetMhz,
        IntelGpuFrequencyRange current,
        out double canonicalTargetMhz,
        out IntelGpuFrequencyRange request,
        out string? failureReason)
    {
        canonicalTargetMhz = default;
        request = default;
        failureReason = null;

        if (!TryGetCanonicalTarget(selectableClocksMhz, targetMhz, out canonicalTargetMhz))
        {
            failureReason = "The requested minimum is not one of the currently selectable driver clocks.";
            return false;
        }
        if (!double.IsFinite(current.Min) || !double.IsFinite(current.Max))
        {
            failureReason = "The current IGCL range contains a non-finite limit.";
            return false;
        }
        if (current.Max >= 0 && current.Max < canonicalTargetMhz)
        {
            failureReason = "The requested minimum exceeds the current explicit maximum; the maximum was left unchanged.";
            return false;
        }

        request = new(canonicalTargetMhz, current.Max >= 0 ? current.Max : -1);
        return true;
    }

    internal static bool TryCreateFactoryMinimumReleaseRequest(
        IntelGpuFrequencyRange current,
        out IntelGpuFrequencyRange request)
    {
        request = default;
        if (!double.IsFinite(current.Min) || !double.IsFinite(current.Max))
            return false;

        request = new(-1, current.Max >= 0 ? current.Max : -1);
        return true;
    }

    internal static bool MatchesReadback(
        IntelGpuFrequencyRange request,
        IntelGpuFrequencyRange preWrite,
        IntelGpuFrequencyRange readback) =>
        MatchesRangeSide(request.Min, readback.Min)
        && MatchesRangeSide(preWrite.Max >= 0 ? preWrite.Max : -1, readback.Max);

    private static bool MatchesRangeSide(double requested, double actual) =>
        double.IsFinite(actual)
        && (requested < 0 ? actual < 0 : Math.Abs(requested - actual) <= FrequencyToleranceMhz);

    internal sealed record ClockSelection(
        bool Available,
        string? UnavailableReason,
        IReadOnlyList<double> AvailableClocksMhz,
        IReadOnlyList<double> SelectableClocksMhz,
        double? SelectableMinMhz,
        double? SelectableMaxMhz,
        double? RecommendedDefaultMhz)
    {
        internal static ClockSelection Unavailable(string reason, IReadOnlyList<double>? normalized = null) => new(
            false,
            reason,
            normalized ?? Array.Empty<double>(),
            Array.Empty<double>(),
            null,
            null,
            null);
    }
}
