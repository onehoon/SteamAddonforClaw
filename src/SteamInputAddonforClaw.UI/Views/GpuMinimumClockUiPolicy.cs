namespace SteamInputAddonforClaw.Views;

internal static class GpuMinimumClockUiPolicy
{
    private const double ToleranceMhz = 0.1;

    internal static double? ResolveDraftMhz(IReadOnlyList<double> clocks, double? savedMhz, double? recommendedDefaultMhz)
    {
        if (TryGetCanonicalClock(clocks, savedMhz, out var saved)) return saved;
        return TryGetCanonicalClock(clocks, recommendedDefaultMhz, out var fallback) ? fallback : null;
    }

    internal static bool IsSupported(IReadOnlyList<double> clocks, double? mhz) =>
        TryGetIndex(clocks, mhz, out _);

    internal static bool TryGetIndex(IReadOnlyList<double> clocks, double? mhz, out int index)
    {
        index = -1;
        if (mhz is not { } value || !double.IsFinite(value)) return false;
        for (var candidate = 0; candidate < clocks.Count; candidate++)
        {
            if (double.IsFinite(clocks[candidate]) && Math.Abs(clocks[candidate] - value) <= ToleranceMhz)
            {
                index = candidate;
                return true;
            }
        }
        return false;
    }

    internal static bool TryGetClockAtIndex(IReadOnlyList<double> clocks, double sliderIndex, out double mhz)
    {
        mhz = default;
        if (!double.IsFinite(sliderIndex) || clocks.Count == 0) return false;
        var index = (int)Math.Round(sliderIndex, MidpointRounding.AwayFromZero);
        if (index < 0 || index >= clocks.Count || !double.IsFinite(clocks[index])) return false;
        mhz = clocks[index];
        return true;
    }

    internal static bool ShouldCommit(
        IReadOnlyList<double> clocks,
        double? selectedMhz,
        double? savedMhz,
        bool dirty,
        bool enabled,
        bool busy) =>
        dirty && enabled && !busy && TryGetCanonicalClock(clocks, selectedMhz, out var selected)
        && (!TryGetCanonicalClock(clocks, savedMhz, out var saved) || Math.Abs(selected - saved) > ToleranceMhz);

    internal static bool ResolveFailedCommit(ref bool draftDirty, long submittedGeneration, long currentGeneration)
    {
        var preserveDraft = draftDirty && submittedGeneration != currentGeneration;
        if (!preserveDraft) draftDirty = false;
        return preserveDraft;
    }

    internal static string FormatMhz(double? mhz) =>
        mhz is { } value && double.IsFinite(value) ? $"{value:0.##} MHz" : "— MHz";

    private static bool TryGetCanonicalClock(IReadOnlyList<double> clocks, double? mhz, out double canonical)
    {
        canonical = default;
        if (!TryGetIndex(clocks, mhz, out var index)) return false;
        canonical = clocks[index];
        return true;
    }
}
