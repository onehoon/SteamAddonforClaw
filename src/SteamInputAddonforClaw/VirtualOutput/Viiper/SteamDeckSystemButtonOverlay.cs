using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Output-only synthetic Steam Deck <c>Steam</c>/<c>QuickAccess</c> system-button primitive. Holds short pulses
/// intent that <see cref="Apply"/> merges into an otherwise mapper-produced <see cref="SteamDeckDeviceState"/>
/// on the existing continuous Steam Deck publish path -- this is not a second state-publication path
/// and never calls VIIPER directly. Steam pulse timing is software-only and remains unvalidated on
/// physical hardware.
/// </summary>
internal sealed class SteamDeckSystemButtonOverlay
{
    private static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(100);

    private readonly TimeProvider _time;
    private readonly Func<long> _timestampProvider;
    private readonly object _gate = new();
    private DateTimeOffset? _steamPulseExpiresAt;
    private DateTimeOffset? _quickAccessPulseExpiresAt;
    private bool _publishedSteamActive;
    private bool _publishedQuickAccessActive;
    private long _nextPulseId;
    private PulseDiagnostic? _steamPulseDiagnostic;
    private PulseDiagnostic? _quickAccessPulseDiagnostic;

    internal SteamDeckSystemButtonOverlay(TimeProvider? timeProvider = null, Func<long>? timestampProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
    }

    /// <summary>
    /// Records that Quick Access should be asserted for the next <see cref="PulseDuration"/>. Does
    /// not call VIIPER; the merge happens inside <see cref="Apply"/> on the existing publish path. A
    /// repeated call while a pulse is already active restarts/extends it from now -- no queue.
    /// </summary>
    internal void RequestQuickAccessPulse()
    {
        var now = _time.GetUtcNow();
        var diagnosticEnabled = AppLog.IsEnabled(AppLogLevel.Debug);
        long pulseId = 0;
        bool otherActive;
        lock (_gate)
        {
            otherActive = _steamPulseExpiresAt is { } steam && now < steam;
            var alreadyActive = _quickAccessPulseExpiresAt is { } expiry && now < expiry;
            _quickAccessPulseExpiresAt = now + PulseDuration;
            if (diagnosticEnabled)
            {
                if (alreadyActive && _quickAccessPulseDiagnostic is not null)
                {
                    pulseId = _quickAccessPulseDiagnostic.PulseId;
                    _quickAccessPulseDiagnostic.RequestCount++;
                }
                else
                {
                    pulseId = ++_nextPulseId;
                    _quickAccessPulseDiagnostic = new PulseDiagnostic(pulseId, "QuickAccess", _timestampProvider());
                }
            }
        }
        AppLog.Debug("SteamDeck.QuickAccess", "QuickAccess pulse requested",
            ("PulseId", pulseId), ("DurationMs", (long)PulseDuration.TotalMilliseconds), ("OtherSystemButtonActive", otherActive));
    }

    internal void RequestSteamPulse()
    {
        var now = _time.GetUtcNow();
        var diagnosticEnabled = AppLog.IsEnabled(AppLogLevel.Debug);
        long pulseId = 0;
        bool otherActive;
        lock (_gate)
        {
            otherActive = _quickAccessPulseExpiresAt is { } quickAccess && now < quickAccess;
            var alreadyActive = _steamPulseExpiresAt is { } expiry && now < expiry;
            _steamPulseExpiresAt = now + PulseDuration;
            if (diagnosticEnabled)
            {
                if (alreadyActive && _steamPulseDiagnostic is not null)
                {
                    pulseId = _steamPulseDiagnostic.PulseId;
                    _steamPulseDiagnostic.RequestCount++;
                }
                else
                {
                    pulseId = ++_nextPulseId;
                    _steamPulseDiagnostic = new PulseDiagnostic(pulseId, "Steam", _timestampProvider());
                }
            }
        }
        AppLog.Debug("SteamDeck.SystemButton", "Steam pulse requested",
            ("PulseId", pulseId), ("DurationMs", (long)PulseDuration.TotalMilliseconds), ("OtherSystemButtonActive", otherActive));
    }

    /// <summary>Immediately clears any pending/active synthetic Quick Access pulse.</summary>
    internal void Clear()
    {
        lock (_gate)
        {
            _steamPulseExpiresAt = null;
            _quickAccessPulseExpiresAt = null;
            _steamPulseDiagnostic = null;
            _quickAccessPulseDiagnostic = null;
        }
    }

    /// <summary>
    /// Called from the existing regular Steam Deck state publication path, after
    /// <see cref="SteamDeckDeviceStateMapper.Map"/> and before the VIIPER <c>SetState</c> call. Sets
    /// only <see cref="SteamDeckDeviceState.QuickAccess"/> while a pulse is active (check-and-clear if
    /// it has expired) and leaves every other field of <paramref name="state"/> untouched.
    /// </summary>
    internal SteamDeckDeviceState Apply(SteamDeckDeviceState state)
    {
        bool steamActive, quickAccessActive, steamEdge, quickAccessEdge;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            steamActive = _steamPulseExpiresAt is { } steam && now < steam;
            quickAccessActive = _quickAccessPulseExpiresAt is { } quick && now < quick;
            if (!steamActive) _steamPulseExpiresAt = null;
            if (!quickAccessActive) _quickAccessPulseExpiresAt = null;
            steamEdge = steamActive != _publishedSteamActive;
            quickAccessEdge = quickAccessActive != _publishedQuickAccessActive;
            _publishedSteamActive = steamActive;
            _publishedQuickAccessActive = quickAccessActive;
        }

        if (steamEdge) AppLog.Debug("SteamDeck.SystemButton", steamActive ? "Steam asserted" : "Steam cleared");
        if (quickAccessEdge) AppLog.Debug("SteamDeck.SystemButton", quickAccessActive ? "QuickAccess asserted" : "QuickAccess cleared");

        state.Steam = steamActive ? (byte)1 : (byte)0;
        state.QuickAccess = quickAccessActive ? (byte)1 : (byte)0;
        return state;
    }

    /// <summary>Records the result of the existing canonical SetState call; no report is created here.</summary>
    internal void RecordPublishResult(SteamDeckDeviceState state, bool accepted, long timestamp)
    {
        PulseSummary? completedSteam = null;
        PulseSummary? completedQuickAccess = null;
        lock (_gate)
        {
            completedSteam = RecordButtonPublish(_steamPulseDiagnostic, state.Steam != 0, accepted, timestamp, ref _steamPulseDiagnostic);
            completedQuickAccess = RecordButtonPublish(_quickAccessPulseDiagnostic, state.QuickAccess != 0, accepted, timestamp, ref _quickAccessPulseDiagnostic);
        }

        if (completedSteam is { } steam) LogSummary(steam);
        if (completedQuickAccess is { } quickAccess) LogSummary(quickAccess);
    }

    private static PulseSummary? RecordButtonPublish(PulseDiagnostic? diagnostic, bool asserted, bool accepted, long timestamp, ref PulseDiagnostic? current)
    {
        if (diagnostic is null) return null;
        if (!accepted)
        {
            diagnostic.SetStateFailures++;
            var failed = BuildSummary(diagnostic, releasePublished: false);
            current = null;
            return failed;
        }
        if (asserted)
        {
            diagnostic.ActiveSetStateCount++;
            if (!diagnostic.FirstAssertedTimestamp.HasValue) diagnostic.FirstAssertedTimestamp = timestamp;
            diagnostic.LastAssertedTimestamp = timestamp;
            return null;
        }

        var summary = BuildSummary(diagnostic, diagnostic.FirstAssertedTimestamp.HasValue);
        current = null;
        return summary;
    }

    private static PulseSummary BuildSummary(PulseDiagnostic diagnostic, bool releasePublished) => new(
        diagnostic.PulseId,
        diagnostic.Button,
        diagnostic.RequestCount,
        diagnostic.ActiveSetStateCount,
        diagnostic.FirstAssertedTimestamp.HasValue ? Stopwatch.GetElapsedTime(diagnostic.RequestTimestamp, diagnostic.FirstAssertedTimestamp.Value).TotalMilliseconds : (double?)null,
        diagnostic.FirstAssertedTimestamp.HasValue ? Stopwatch.GetElapsedTime(diagnostic.FirstAssertedTimestamp.Value, diagnostic.LastAssertedTimestamp).TotalMilliseconds : (double?)null,
        releasePublished,
        diagnostic.SetStateFailures);

    private static void LogSummary(PulseSummary summary)
    {
        AppLog.Debug("SteamDeck.SystemButton", "PulseCompleted",
            ("PulseId", summary.PulseId), ("Button", summary.Button),
            ("RequestCount", summary.RequestCount),
            ("ActiveSetStateCount", summary.ActiveSetStateCount),
            ("FirstPublishDelayMs", summary.FirstPublishDelayMs ?? -1.0),
            ("PublishedHighDurationMs", summary.PublishedHighDurationMs ?? -1.0),
            ("ReleasePublished", summary.ReleasePublished), ("SetStateFailures", summary.SetStateFailures));
    }

    private sealed class PulseDiagnostic(long pulseId, string button, long requestTimestamp)
    {
        internal readonly long PulseId = pulseId;
        internal readonly string Button = button;
        internal readonly long RequestTimestamp = requestTimestamp;
        internal int RequestCount = 1;
        internal int ActiveSetStateCount;
        internal int SetStateFailures;
        internal long? FirstAssertedTimestamp;
        internal long LastAssertedTimestamp;
    }

    private readonly record struct PulseSummary(long PulseId, string Button, int RequestCount, int ActiveSetStateCount,
        double? FirstPublishDelayMs, double? PublishedHighDurationMs, bool ReleasePublished, int SetStateFailures);
}
