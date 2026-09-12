using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Overlay;

// SF-V2-07 section 18/48: pure, WinUI-free helpers over the shared page payload. The renderer
// treats RowId/SectionId/CommitGroupId as opaque stable identities; it never reconstructs Device
// labels/order/options and never inspects known TDP limit tuples. Kept free of WinUI so it is
// unit-testable without a XAML host, mirroring the OverlayToggleModel/OverlaySliderModel split.
internal static class QuickSettingsRowRendering
{
    internal static bool IsWellFormed(QuickSettingsRow row) => row.ControlKind switch
    {
        QuickSettingsControlKind.Toggle => row.Value is null || row.Value.Kind == QuickSettingsValueKind.Boolean,
        QuickSettingsControlKind.Slider => IsWellFormedSlider(row),
        _ => false,
    };

    private static bool IsWellFormedSlider(QuickSettingsRow row)
    {
        if (row.SliderSpec is not { } spec) return false;
        if (row.Value is not null && row.Value.Kind != QuickSettingsValueKind.Integer) return false;
        return spec.Kind switch
        {
            QuickSettingsSliderKind.Numeric => spec.Minimum <= spec.Maximum && spec.Step > 0,
            QuickSettingsSliderKind.Discrete => spec.Options is { Count: > 0 },
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

// SF-V2-07 section 9/23: the smallest page-local Device Quick Settings binder. It owns only
// surface-local facts -- the latest authoritative page, rendered rows' pending drafts, and one
// narrow immediate-mutation-busy fact -- never hardware state, persistence, Runtime feature state,
// or controller/capture authority. Zero WinUI dependency: OverlayWindow supplies a UI-thread
// marshal for the one asynchronous callback (a delayed commit's settlement) and otherwise calls
// this synchronously from the UI thread.
internal sealed class OverlayQuickSettingsPageBinding : IDisposable
{
    private sealed class PendingEntry
    {
        internal required Dictionary<QuickSettingsRowId, QuickSettingsValue> Values { get; init; }
        internal required IReadOnlyList<QuickSettingsRowId> Order { get; init; }
        internal required QuickSettingsSectionId SectionId { get; init; }
        internal required OverlayDelayedSliderCommit Commit { get; init; }
    }

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

    // Section 29: a fresh authoritative page never clears pending drafts by itself -- the caller
    // re-renders via BuildEffectivePage(), which keeps overlaying them over the new authority.
    internal void ApplyAuthoritativePage(QuickSettingsPageSnapshot page) => _authoritativePage = page;

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

        var intent = new QuickSettingsMutationIntent(
            _authoritativePage.PageId, _authoritativePage.AppId, rowId,
            [new QuickSettingsRowValue(rowId, QuickSettingsValue.Boolean(desired))]);

        _mutationBusy = true;
        try
        {
            var result = await _mutate(intent);
            _authoritativePage = result.Page;
            LastLocalFailureMessage = result.Succeeded ? null : result.FailureMessage;
            return true;
        }
        catch (Exception exception)
        {
            // Operation/transport failure (section 32): never synthesize a fake authoritative page.
            LastLocalFailureMessage = exception.Message;
            return false;
        }
        finally { _mutationBusy = false; }
    }

    // Sections 20/21/23/24/25: independent (row-keyed) or grouped (CommitGroupId-keyed) pending
    // draft. The first edit of a group seeds the whole section in row order; later edits reuse the
    // same whole draft and restart the trailing delay from the newest edit. Malformed/unsupported
    // commit policy fails the row closed (section 22) rather than falling back to a literal delay.
    internal bool ScheduleSlider(QuickSettingsRowId rowId, QuickSettingsValue desired)
    {
        if (_disposed) return false;
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

    // Sections 30-32: the CURRENT generation's settlement is authoritative. A valid Runtime
    // settlement (including a normal typed failure) always carries a fresh Page and replaces the
    // cached authoritative page; a thrown operation/transport failure never does.
    private void OnSettled(QuickSettingsPendingKey key, int generation, QuickSettingsCommitSettlement settlement)
    {
        if (_disposed) return;
        if (!_pending.TryGetValue(key, out var entry) || !entry.Commit.IsCurrentGeneration(generation)) return;

        _pending.Remove(key);
        entry.Commit.Dispose();

        if (settlement.Result is { } result)
        {
            _authoritativePage = result.Page;
            LastLocalFailureMessage = result.Succeeded ? null : result.FailureMessage;
        }
        else
        {
            LastLocalFailureMessage = settlement.OperationFailureMessage ?? "Quick Settings update failed.";
        }

        SettledAsynchronously?.Invoke();
    }

    // Section 40: window/process teardown suppresses obsolete local settlement callbacks and
    // prevents new scheduling. Runtime/feature teardown owns already-submitted operations.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _pending.Values) entry.Commit.Dispose();
        _pending.Clear();
    }
}
