using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

// SF-V2-07 section 18/48: pure, WinUI-free helpers over the shared page payload. The renderer
// treats RowId/SectionId/CommitGroupId as opaque stable identities; it never reconstructs Device
// labels/order/options and never inspects known TDP limit tuples. Kept free of WinUI so it is
// unit-testable without a XAML host, mirroring the OverlayToggleModel/OverlaySliderModel split.
internal static class QuickSettingsRowRendering
{
    internal static bool IsWellFormed(QuickSettingsRow row) => row.ControlKind switch
    {
        QuickSettingsControlKind.Toggle =>
            row.Value is null || (row.Value.Kind == QuickSettingsValueKind.Boolean && row.Value.IsStructurallyValid),
        QuickSettingsControlKind.Slider => IsWellFormedSlider(row),
        _ => false,
    };

    // Section 48: a missing or unknown discrete product value must fail the row closed rather than
    // render an arbitrary option (index 0) -- this is a normal supported Runtime read-failure shape
    // (e.g. CPU Boost/Power Mode enabled with neither a desired nor a known current side), not a
    // theoretical case. Requiring the value up front, before the Numeric/Discrete split, keeps both
    // kinds equally fail-closed instead of letting Numeric silently fall back to its Minimum.
    private static bool IsWellFormedSlider(QuickSettingsRow row)
    {
        if (row.SliderSpec is not { } spec) return false;
        if (row.Value is not { Kind: QuickSettingsValueKind.Integer, IntegerValue: { } current } value || !value.IsStructurallyValid)
            return false;

        return spec.Kind switch
        {
            QuickSettingsSliderKind.Numeric => spec.Minimum <= spec.Maximum && spec.Step > 0,
            QuickSettingsSliderKind.Discrete => spec.Options is { Count: > 0 } options && FindDiscreteIndex(options, current) >= 0,
            _ => false,
        };
    }

    // Section 18: the product value is option.Value, never the option's ordinal position -- option
    // values are never assumed contiguous or zero-based.
    internal static int FindDiscreteIndex(IReadOnlyList<QuickSettingsDiscreteOption> options, int value)
    {
        for (var i = 0; i < options.Count; i++)
            if (options[i].Value == value) return i;
        return -1;
    }
}

// Section 20: a small typed discriminated pending-draft identity -- row-keyed when a slider has no
// CommitGroupId, group-keyed when it does. Never a product string such as "tdp" or "cpu-ac".
internal readonly struct QuickSettingsPendingKey : IEquatable<QuickSettingsPendingKey>
{
    private readonly bool _isGroup;
    private readonly QuickSettingsRowId _rowId;
    private readonly QuickSettingsCommitGroupId _groupId;

    private QuickSettingsPendingKey(bool isGroup, QuickSettingsRowId rowId, QuickSettingsCommitGroupId groupId)
    {
        _isGroup = isGroup;
        _rowId = rowId;
        _groupId = groupId;
    }

    internal static QuickSettingsPendingKey ForRow(QuickSettingsRowId rowId) => new(false, rowId, default);
    internal static QuickSettingsPendingKey ForGroup(QuickSettingsCommitGroupId groupId) => new(true, default, groupId);
    internal static QuickSettingsPendingKey For(QuickSettingsRow row) =>
        row.CommitGroupId is { } group ? ForGroup(group) : ForRow(row.RowId);

    public bool Equals(QuickSettingsPendingKey other) =>
        _isGroup == other._isGroup && _rowId == other._rowId && _groupId == other._groupId;
    public override bool Equals(object? obj) => obj is QuickSettingsPendingKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_isGroup, _rowId, _groupId);
}

// SF-V2-07/09 section 9/23: the smallest page-local Quick Settings binder -- one instance per
// PageId (Device or Profile). It owns only surface-local facts -- the latest authoritative page,
// rendered rows' pending drafts, and one narrow immediate-mutation-busy fact -- never hardware
// state, persistence, Runtime feature state, or controller/capture authority. Zero WinUI
// dependency: OverlayWindow supplies a UI-thread marshal for the one asynchronous callback (a
// delayed commit's settlement) and otherwise calls this synchronously from the UI thread.
//
// SF-V2-09 section 17: Device has a static (Device, null) context for its whole lifetime; Profile's
// AppId context changes as the active Steam game changes. `_expectedPageId` is the narrow page
// identity invariant (a binding built for one PageId must never accept the other); the AppId half
// of the (PageId, AppId) context is read off `_authoritativePage.AppId` -- there is no separate
// epoch/state machine.
internal sealed class OverlayQuickSettingsPageBinding : IDisposable
{
    private sealed class PendingEntry
    {
        internal required Dictionary<QuickSettingsRowId, QuickSettingsValue> Values { get; init; }
        internal required IReadOnlyList<QuickSettingsRowId> Order { get; init; }
        internal required QuickSettingsSectionId SectionId { get; init; }
        internal required OverlayDelayedSliderCommit Commit { get; init; }
    }

    private readonly QuickSettingsPageId _expectedPageId;
    private readonly Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> _mutate;
    private readonly Action<Action> _uiThreadMarshal;
    private readonly Func<TimeSpan, CancellationToken, Task>? _delayAsync;
    private readonly Dictionary<QuickSettingsPendingKey, PendingEntry> _pending = new();

    private QuickSettingsPageSnapshot _authoritativePage;
    private bool _mutationBusy;
    private bool _disposed;

    internal OverlayQuickSettingsPageBinding(
        QuickSettingsPageId pageId,
        Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> mutate,
        Action<Action> uiThreadMarshal,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _expectedPageId = pageId;
        _mutate = mutate;
        _uiThreadMarshal = uiThreadMarshal;
        _delayAsync = delayAsync;
        _authoritativePage = QuickSettingsPageSnapshot.Unavailable(pageId);
    }

    // Raised only after an asynchronous delayed-commit settlement changes state (sections 30-32).
    // The synchronous entry points below do not raise this -- their own caller already knows to
    // re-render right after calling them, on the same call stack.
    internal event Action? SettledAsynchronously;

    internal QuickSettingsPageSnapshot AuthoritativePage => _authoritativePage;

    internal string? LastLocalFailureMessage { get; private set; }

    internal IReadOnlyCollection<QuickSettingsPendingKey> PendingKeys => _pending.Keys.ToArray();

    // Section 28: a pending draft wins over the latest authoritative row value for every row it
    // contains; every unrelated row adopts the latest authoritative page immediately.
    internal QuickSettingsPageSnapshot BuildEffectivePage()
    {
        if (_pending.Count == 0) return _authoritativePage;
        var sections = _authoritativePage.Sections
            .Select(section => section with { Rows = section.Rows.Select(WithEffectiveValue).ToArray() })
            .ToArray();
        return _authoritativePage with { Sections = sections };
    }

    private QuickSettingsRow WithEffectiveValue(QuickSettingsRow row) =>
        TryGetPendingValue(row.RowId, out var pending) ? row with { Value = pending } : row;

    internal bool TryGetPendingValue(QuickSettingsRowId rowId, out QuickSettingsValue value)
    {
        foreach (var entry in _pending.Values)
            if (entry.Values.TryGetValue(rowId, out var found)) { value = found; return true; }
        value = null!;
        return false;
    }

    internal QuickSettingsRow? FindRow(QuickSettingsRowId rowId) =>
        _authoritativePage.Sections.SelectMany(s => s.Rows).FirstOrDefault(r => r.RowId == rowId);

    internal QuickSettingsSection? FindSectionForRow(QuickSettingsRowId rowId) =>
        _authoritativePage.Sections.FirstOrDefault(s => s.Rows.Any(r => r.RowId == rowId));

    // Section 29: a fresh authoritative page never clears an individual pending draft just because
    // it exists -- the caller re-renders via BuildEffectivePage(), which keeps overlaying valid
    // drafts over the new authority. SF-V2-09 section 17.1/17.2/18/19 add three narrow safety facts
    // on top of that: an unexpected PageId is ignored/fail-closed rather than corrupting this page's
    // state; an AppId context change retires ALL local pending work for the old context (an
    // already-submitted Runtime request is not magically cancelled, but its local settlement is
    // suppressed -- section 17.2); and a same-context refresh prunes any pending draft whose edited
    // row the fresh page no longer allows (section 18), generically from row metadata, with no
    // per-feature special-casing. A page pushed through this method is always an "independent"
    // Runtime refresh (never a mutation's own result -- see OnSettled/SubmitImmediateToggleAsync),
    // so it also clears a stale local failure banner (section 19).
    internal void ApplyAuthoritativePage(QuickSettingsPageSnapshot page)
    {
        if (page.PageId != _expectedPageId)
        {
            OverlayLog.Warn("QuickSettings", "Ignoring a Quick Settings page whose PageId does not match this binding.",
                exception: null, ("Expected", _expectedPageId), ("Actual", page.PageId));
            return;
        }

        if (page.AppId != _authoritativePage.AppId)
            RetireAllPending();
        else
            PruneAgainstPage(page);

        LastLocalFailureMessage = null;
        _authoritativePage = page;
    }

    internal static bool CanMutate(QuickSettingsRow row) => row.Available && row.Writable;

    // Sections 15/16/33: one immediate mutation at a time; cancels unsubmitted same-section drafts
    // first so a parent toggle cannot leave a stale child timer alive. Returns true only when a
    // mutation was actually submitted; the caller re-renders either way.
    internal async Task<bool> SubmitImmediateToggleAsync(QuickSettingsRowId rowId, bool desired)
    {
        if (_disposed || _mutationBusy) return false;
        if (FindRow(rowId) is not { ControlKind: QuickSettingsControlKind.Toggle } row) return false;
        if (row.CommitPolicy.Mode != QuickSettingsCommitMode.Immediate) return false;
        if (!CanMutate(row)) return false;

        if (FindSectionForRow(rowId) is { } section)
            CancelUnsubmittedInSection(section.SectionId);

        var submittedPageId = _authoritativePage.PageId;
        var submittedAppId = _authoritativePage.AppId;
        var intent = new QuickSettingsMutationIntent(
            submittedPageId, submittedAppId, rowId,
            [new QuickSettingsRowValue(rowId, QuickSettingsValue.Boolean(desired))]);

        _mutationBusy = true;
        try
        {
            var result = await _mutate(intent);
            // Section 17.3: the active game may have changed while this immediate toggle was in
            // flight -- a fresh Profile(B) authoritative page can already be installed by the time
            // this A-targeted result returns. A mutation was still genuinely submitted (the caller's
            // re-render is a harmless no-op), but the stale A result must never overwrite B.
            if (IsStaleForCurrentContext(result.Page)) return true;
            _authoritativePage = result.Page;
            LastLocalFailureMessage = result.Succeeded ? null : result.FailureMessage;
            PruneAgainstPage(_authoritativePage);
            return true;
        }
        catch (Exception exception)
        {
            // PR #510 review: an operation/transport failure is a supported lifecycle condition too
            // (never a typed Page to compare, unlike the success path above), so it needs its own
            // staleness check -- otherwise a context change that raced this outstanding await would
            // leave A's exception message painted over B's already-installed authoritative page.
            if (_authoritativePage.PageId == submittedPageId && _authoritativePage.AppId == submittedAppId)
                LastLocalFailureMessage = exception.Message;
            return false;
        }
        finally { _mutationBusy = false; }
    }

    // Sections 20/21/23/24/25: independent (row-keyed) or grouped (CommitGroupId-keyed) pending
    // draft. The first edit of a group seeds the whole section in row order; later edits reuse the
    // same whole draft and restart the trailing delay from the newest edit. Malformed/unsupported
    // commit policy fails the row closed (section 22) rather than falling back to a literal delay.
    // Rejects while an immediate Toggle mutation is outstanding (reuses the same narrow
    // _mutationBusy fact SubmitImmediateToggleAsync sets): otherwise a grouped draft could seed
    // itself from the pre-toggle authoritative section (e.g. DeviceTdpEnabled still true) and later
    // re-enable a feature the user just turned off. The caller re-renders immediately after this
    // returns, so a rejected controller/pointer edit just snaps back to the current effective value.
    internal bool ScheduleSlider(QuickSettingsRowId rowId, QuickSettingsValue desired)
    {
        if (_disposed || _mutationBusy) return false;
        if (FindRow(rowId) is not { ControlKind: QuickSettingsControlKind.Slider } row) return false;
        if (!CanMutate(row)) return false;
        if (row.CommitPolicy is not { Mode: QuickSettingsCommitMode.TrailingDebounce, DelayMilliseconds: > 0 } policy) return false;

        var key = QuickSettingsPendingKey.For(row);
        if (!_pending.TryGetValue(key, out var entry))
        {
            Dictionary<QuickSettingsRowId, QuickSettingsValue> values;
            IReadOnlyList<QuickSettingsRowId> order;
            QuickSettingsSectionId sectionId;

            if (row.CommitGroupId is null)
            {
                values = new Dictionary<QuickSettingsRowId, QuickSettingsValue> { [rowId] = desired };
                order = [rowId];
                sectionId = FindSectionForRow(rowId)?.SectionId ?? default;
            }
            else
            {
                if (FindSectionForRow(rowId) is not { } section) return false;
                if (!TrySeedSectionDraft(section, out values, out order)) return false;
                sectionId = section.SectionId;
            }

            entry = new PendingEntry
            {
                Values = values,
                Order = order,
                SectionId = sectionId,
                Commit = new OverlayDelayedSliderCommit(_mutate,
                    (generation, settlement) => _uiThreadMarshal(() => OnSettled(key, generation, settlement)),
                    _delayAsync),
            };
            _pending[key] = entry;
        }

        entry.Values[rowId] = desired;
        ApplyLinkedConstraints(entry.Values, rowId);

        var values2 = entry.Order.Select(id => new QuickSettingsRowValue(id, entry.Values[id])).ToArray();
        var intent = new QuickSettingsMutationIntent(_authoritativePage.PageId, _authoritativePage.AppId, rowId, values2);
        entry.Commit.Schedule(intent, TimeSpan.FromMilliseconds(policy.DelayMilliseconds));
        return true;
    }

    // Section 24: never fabricate a value -- every row in the containing section must already
    // carry a structurally valid product value or the group draft is not seeded at all.
    private static bool TrySeedSectionDraft(
        QuickSettingsSection section,
        out Dictionary<QuickSettingsRowId, QuickSettingsValue> values,
        out IReadOnlyList<QuickSettingsRowId> order)
    {
        values = new Dictionary<QuickSettingsRowId, QuickSettingsValue>();
        var orderList = new List<QuickSettingsRowId>();
        foreach (var row in section.Rows)
        {
            if (row.Value is not { } value) { values = null!; order = null!; return false; }
            values[row.RowId] = value;
            orderList.Add(row.RowId);
        }
        order = orderList;
        return true;
    }

    // Section 26/27: metadata-driven lower/upper gap correction only -- no label parsing, no known
    // TDP limit tuples. Mirrors the QAM applyDeviceQuickSettingsLinkedConstraints reference exactly
    // so the pending draft's companion value (and therefore its rendered preview) corrects
    // immediately, before Runtime settlement.
    private void ApplyLinkedConstraints(Dictionary<QuickSettingsRowId, QuickSettingsValue> values, QuickSettingsRowId editedRowId)
    {
        foreach (var constraint in _authoritativePage.LinkedSliderConstraints)
        {
            if (constraint.LowerRowId != editedRowId && constraint.UpperRowId != editedRowId) continue;
            if (FindRow(constraint.LowerRowId)?.SliderSpec is not { } lowerSpec) continue;
            if (FindRow(constraint.UpperRowId)?.SliderSpec is not { } upperSpec) continue;
            if (!values.TryGetValue(constraint.LowerRowId, out var lowerValue) || lowerValue.IntegerValue is not { } lower) continue;
            if (!values.TryGetValue(constraint.UpperRowId, out var upperValue) || upperValue.IntegerValue is not { } upper) continue;
            var gap = constraint.MinimumGap;
            if (gap <= 0) continue;

            if (constraint.LowerRowId == editedRowId && upper < lower + gap)
            {
                if (lower + gap <= upperSpec.Maximum)
                    values[constraint.UpperRowId] = QuickSettingsValue.Integer(lower + gap);
                else
                    values[constraint.LowerRowId] = QuickSettingsValue.Integer(upperSpec.Maximum - gap);
            }
            else if (constraint.UpperRowId == editedRowId && lower > upper - gap)
            {
                if (upper - gap >= lowerSpec.Minimum)
                    values[constraint.LowerRowId] = QuickSettingsValue.Integer(upper - gap);
                else
                    values[constraint.UpperRowId] = QuickSettingsValue.Integer(lowerSpec.Minimum + gap);
            }
        }
    }

    // Section 16: an immediate parent toggle must retire same-section unsubmitted delayed work
    // (e.g. CPU Boost OFF must not let a pending AC/DC slider fire two seconds later). An
    // already-submitted mutation is left alone -- its later completion is still current-generation
    // governed by OnSettled.
    private void CancelUnsubmittedInSection(QuickSettingsSectionId sectionId)
    {
        foreach (var (key, entry) in _pending.ToArray())
        {
            if (entry.SectionId != sectionId) continue;
            entry.Commit.CancelUnsubmitted();
            if (entry.Commit.HasPendingDraft) continue; // already in flight -- leave it to settle
            _pending.Remove(key);
            entry.Commit.Dispose();
        }
    }

    // Section 39: Hide never waits for the debounce window. Cancel only UN-submitted drafts; an
    // already-submitted mutation may still settle later without holding OQ4 capture retirement open.
    internal void CancelUnsubmittedDrafts()
    {
        foreach (var (key, entry) in _pending.ToArray())
        {
            entry.Commit.CancelUnsubmitted();
            if (entry.Commit.HasPendingDraft) continue;
            _pending.Remove(key);
            entry.Commit.Dispose();
        }
    }

    // Sections 30-32/17.4: the CURRENT generation's settlement is authoritative. A valid Runtime
    // settlement (including a normal typed failure) always carries a fresh Page and replaces the
    // cached authoritative page UNLESS the active context has already moved on (a context change
    // already retired this entry's key in the common case -- section 17.2 -- but the explicit
    // IsStaleForCurrentContext check is the same defense-in-depth guarantee SubmitImmediateToggleAsync
    // uses); a thrown operation/transport failure never carries a Page at all.
    private void OnSettled(QuickSettingsPendingKey key, int generation, QuickSettingsCommitSettlement settlement)
    {
        if (_disposed) return;
        if (!_pending.TryGetValue(key, out var entry) || !entry.Commit.IsCurrentGeneration(generation)) return;

        _pending.Remove(key);
        entry.Commit.Dispose();

        if (settlement.Result is { } result)
        {
            if (!IsStaleForCurrentContext(result.Page))
            {
                _authoritativePage = result.Page;
                LastLocalFailureMessage = result.Succeeded ? null : result.FailureMessage;
                PruneAgainstPage(_authoritativePage);
            }
        }
        else
        {
            LastLocalFailureMessage = settlement.OperationFailureMessage ?? "Quick Settings update failed.";
        }

        SettledAsynchronously?.Invoke();
    }

    // Section 17.3/17.4: a mutation result is stale once the binder's live context has moved past
    // the (PageId, AppId) it was computed for -- e.g. the active game changed while the request was
    // in flight. PageId is fixed per binding instance so only AppId can actually drift in practice;
    // both are checked for clarity/defense-in-depth rather than trusting the Runtime to only ever
    // echo this binding's own PageId back.
    private bool IsStaleForCurrentContext(QuickSettingsPageSnapshot resultPage) =>
        resultPage.PageId != _expectedPageId || resultPage.AppId != _authoritativePage.AppId;

    // Section 17.2: an AppId context change retires ALL local pending work for the old context, not
    // just unsubmitted drafts -- an already-submitted Runtime request cannot be cancelled, but its
    // eventual settlement must be suppressed (Dispose() makes IsCurrentGeneration false, and removal
    // from _pending independently makes OnSettled's own lookup fail).
    private void RetireAllPending()
    {
        foreach (var entry in _pending.Values) entry.Commit.Dispose();
        _pending.Clear();
    }

    // Section 18/18.1: generic same-context prune -- inspect each pending entry's OWN edited row
    // (not every row it happens to carry) against the fresh page. Absent or no longer
    // Available+Writable retires that whole entry; nothing here knows about ProfileEnabled, TDP, CPU
    // Boost, or Power Mode by name. CancelUnsubmitted()/HasPendingDraft is the same idiom
    // CancelUnsubmittedInSection/CancelUnsubmittedDrafts already use to leave an already-submitted
    // (in-flight) entry alone -- it will settle on its own, subject to the central
    // QuickSettingsMutationAdapter's own writability backstop.
    private void PruneAgainstPage(QuickSettingsPageSnapshot page)
    {
        foreach (var (key, entry) in _pending.ToArray())
        {
            if (!entry.Commit.TryGetPendingIntent(out var intent)) continue;
            var row = page.Sections.SelectMany(s => s.Rows).FirstOrDefault(r => r.RowId == intent.EditedRowId);
            if (row is { Available: true, Writable: true }) continue;

            entry.Commit.CancelUnsubmitted();
            if (entry.Commit.HasPendingDraft) continue; // already in flight -- leave it to settle
            _pending.Remove(key);
            entry.Commit.Dispose();
        }
    }

    // Section 40: window/process teardown suppresses obsolete local settlement callbacks and
    // prevents new scheduling. Runtime/feature teardown owns already-submitted operations.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RetireAllPending();
    }
}
