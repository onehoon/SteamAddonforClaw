using System.Diagnostics;
using System.IO;
using System.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// SF-V2-07: pure, WinUI-free coverage of OverlayQuickSettingsPageBinding -- the page-local Device
// Quick Settings interaction/draft logic (pending key derivation, independent/grouped intent
// construction, linked-constraint application, pending-over-authoritative effective value,
// immediate Toggle submission, and authoritative/typed/operation-failure settlement handling).
// OverlayWindow's WinUI row wiring around this binder is validated on hardware (work order s.51/54)
// since OverlayToggleRow/OverlaySliderRow need a XAML host unavailable in this test project.
public sealed class OverlayQuickSettingsPageBindingTests
{
    private static readonly IReadOnlyList<QuickSettingsDiscreteOption> NonContiguousOptions =
    [
        new(10, "Low"), new(20, "Balanced"), new(40, "High"),
    ];

    private static QuickSettingsRow Toggle(QuickSettingsRowId rowId, bool value, bool available = true, bool writable = true) =>
        new(rowId, rowId.ToString(), QuickSettingsControlKind.Toggle, available, writable,
            available ? QuickSettingsValue.Boolean(value) : null, null, QuickSettingsCommitPolicy.Immediate);

    private static QuickSettingsRow Numeric(
        QuickSettingsRowId rowId, int value, int min, int max,
        QuickSettingsCommitGroupId? group = null, bool available = true, bool writable = true, string? suffix = null) =>
        new(rowId, rowId.ToString(), QuickSettingsControlKind.Slider, available, writable,
            QuickSettingsValue.Integer(value),
            new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, min, max, Step: 1, Suffix: suffix),
            QuickSettingsCommitPolicy.TrailingDebounce2000, group);

    private static QuickSettingsRow Discrete(
        QuickSettingsRowId rowId, int value, IReadOnlyList<QuickSettingsDiscreteOption>? options = null,
        bool available = true, bool writable = true) =>
        new(rowId, rowId.ToString(), QuickSettingsControlKind.Slider, available, writable,
            QuickSettingsValue.Integer(value),
            new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: options ?? NonContiguousOptions),
            QuickSettingsCommitPolicy.TrailingDebounce2000);

    // TDP (grouped, linked PL1/PL2) + CPU Boost (independent discrete sliders) -- the two shapes
    // the real Device page exercises (work order sections 12/24/26).
    private static QuickSettingsPageSnapshot TdpPage(
        int pl1Ac = 20, int pl2Ac = 25, int pl1Dc = 15, int pl2Dc = 20, bool tdpEnabled = true,
        IReadOnlyList<QuickSettingsLinkedSliderConstraint>? linked = null) => new(
        QuickSettingsPageId.Device, null, true, null,
        [
            new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP",
            [
                Toggle(QuickSettingsRowId.DeviceTdpEnabled, tdpEnabled),
                Numeric(QuickSettingsRowId.DeviceTdpAcPl1, pl1Ac, 8, 30, QuickSettingsCommitGroupId.DeviceTdpConfiguration),
                Numeric(QuickSettingsRowId.DeviceTdpAcPl2, pl2Ac, 8, 37, QuickSettingsCommitGroupId.DeviceTdpConfiguration),
                Numeric(QuickSettingsRowId.DeviceTdpDcPl1, pl1Dc, 8, 30, QuickSettingsCommitGroupId.DeviceTdpConfiguration),
                Numeric(QuickSettingsRowId.DeviceTdpDcPl2, pl2Dc, 8, 37, QuickSettingsCommitGroupId.DeviceTdpConfiguration),
            ]),
            new QuickSettingsSection(QuickSettingsSectionId.DeviceCpuBoost, "CPU Boost",
            [
                Toggle(QuickSettingsRowId.DeviceCpuBoostEnabled, true),
                Discrete(QuickSettingsRowId.DeviceCpuBoostAc, 20),
                Discrete(QuickSettingsRowId.DeviceCpuBoostDc, 10),
            ]),
        ],
        linked ?? [new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2, 1)]);

    // SF-V2-09 section 31: a representative Profile page for a given AppId. `profileEnabled` mirrors
    // the real BuildProfile coupling (writable = PersistenceWritable && Enabled) so a master-OFF
    // result page makes every TDP/CPU Boost child row non-writable, exactly like production.
    private static QuickSettingsPageSnapshot ProfilePage(
        uint appId, bool profileEnabled = true,
        int pl1Ac = 20, int pl2Ac = 25, int pl1Dc = 15, int pl2Dc = 20, int cpuAc = 20, int cpuDc = 10,
        IReadOnlyList<QuickSettingsLinkedSliderConstraint>? linked = null) => new(
        QuickSettingsPageId.Profile, appId, true, null,
        [
            new QuickSettingsSection(QuickSettingsSectionId.ProfileGeneral, "Game " + appId,
            [
                Toggle(QuickSettingsRowId.ProfileEnabled, profileEnabled),
            ]),
            new QuickSettingsSection(QuickSettingsSectionId.ProfileTdp, "TDP Control",
            [
                Toggle(QuickSettingsRowId.ProfileTdpEnabled, profileEnabled, writable: profileEnabled),
                Numeric(QuickSettingsRowId.ProfileTdpAcPl1, pl1Ac, 8, 30, QuickSettingsCommitGroupId.ProfileTdpConfiguration, writable: profileEnabled),
                Numeric(QuickSettingsRowId.ProfileTdpAcPl2, pl2Ac, 8, 37, QuickSettingsCommitGroupId.ProfileTdpConfiguration, writable: profileEnabled),
                Numeric(QuickSettingsRowId.ProfileTdpDcPl1, pl1Dc, 8, 30, QuickSettingsCommitGroupId.ProfileTdpConfiguration, writable: profileEnabled),
                Numeric(QuickSettingsRowId.ProfileTdpDcPl2, pl2Dc, 8, 37, QuickSettingsCommitGroupId.ProfileTdpConfiguration, writable: profileEnabled),
            ]),
            new QuickSettingsSection(QuickSettingsSectionId.ProfileCpuBoost, "CPU Boost",
            [
                Toggle(QuickSettingsRowId.ProfileCpuBoostEnabled, profileEnabled, writable: profileEnabled),
                Discrete(QuickSettingsRowId.ProfileCpuBoostAc, cpuAc, writable: profileEnabled),
                Discrete(QuickSettingsRowId.ProfileCpuBoostDc, cpuDc, writable: profileEnabled),
            ]),
        ],
        linked ?? [new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.ProfileTdpAcPl1, QuickSettingsRowId.ProfileTdpAcPl2, 1)]);

    private static OverlayQuickSettingsPageBinding NewProfileBinding(
        QuickSettingsPageSnapshot page,
        Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> mutate,
        UiThreadStub uiThread,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        var binding = new OverlayQuickSettingsPageBinding(QuickSettingsPageId.Profile, mutate, uiThread.Marshal, delayAsync);
        binding.ApplyAuthoritativePage(page);
        return binding;
    }

    private static OverlayQuickSettingsPageBinding NewProfileBinding(
        QuickSettingsPageSnapshot page,
        Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> mutate,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        var binding = new OverlayQuickSettingsPageBinding(QuickSettingsPageId.Profile, mutate, action => action(), delayAsync);
        binding.ApplyAuthoritativePage(page);
        return binding;
    }

    // The binder is documented as single-UI-thread-affine: every settlement callback is expected to
    // be drained on the SAME thread that later reads binder state (App/OverlayWindow's DispatcherQueue
    // in production). A trivial `action => action()` marshal would instead run OnSettled on whatever
    // background thread the delayed commit's continuation lands on, racing the test's own polling
    // thread with no synchronization over the binder's plain fields. This stub reproduces the real
    // "post to one queue, drained by one thread" shape so tests observe state the same way production
    // does -- pump it from the polling thread via SpinUntilAsync's uiThread parameter.
    private sealed class UiThreadStub
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _queue = new();
        public Action<Action> Marshal => action => _queue.Enqueue(action);
        public void Pump() { while (_queue.TryDequeue(out var action)) action(); }
    }

    private static OverlayQuickSettingsPageBinding NewBinding(
        QuickSettingsPageSnapshot page,
        Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> mutate,
        UiThreadStub uiThread,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        var binding = new OverlayQuickSettingsPageBinding(QuickSettingsPageId.Device, mutate, uiThread.Marshal, delayAsync);
        binding.ApplyAuthoritativePage(page);
        return binding;
    }

    // Sync-only overload for tests that never wait on an asynchronous settlement (immediate Toggle,
    // pre-elapse pending-draft inspection) -- there is nothing to pump, so a direct invoke is safe.
    private static OverlayQuickSettingsPageBinding NewBinding(
        QuickSettingsPageSnapshot page,
        Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> mutate,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        var binding = new OverlayQuickSettingsPageBinding(QuickSettingsPageId.Device, mutate, action => action(), delayAsync);
        binding.ApplyAuthoritativePage(page);
        return binding;
    }

    private sealed class ManualDelay
    {
        private readonly object _lock = new();
        private readonly List<TaskCompletionSource> _live = new();
        public Func<TimeSpan, CancellationToken, Task> Func => (_, ct) =>
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock) _live.Add(tcs);
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        };
        public void Elapse()
        {
            TaskCompletionSource[] snapshot;
            lock (_lock) { snapshot = _live.ToArray(); _live.Clear(); }
            foreach (var tcs in snapshot) tcs.TrySetResult();
        }
    }

    private sealed class GatedMutate
    {
        private readonly object _lock = new();
        private readonly Queue<TaskCompletionSource<QuickSettingsMutationResult>> _pending = new();
        private readonly List<QuickSettingsMutationIntent> _calls = new();
        public IReadOnlyList<QuickSettingsMutationIntent> Calls { get { lock (_lock) return _calls.ToArray(); } }
        public Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> Func => intent =>
        {
            var tcs = new TaskCompletionSource<QuickSettingsMutationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock) { _calls.Add(intent); _pending.Enqueue(tcs); }
            return tcs.Task;
        };
        public void CompleteNext(QuickSettingsMutationResult result)
        {
            TaskCompletionSource<QuickSettingsMutationResult> tcs;
            lock (_lock) tcs = _pending.Dequeue();
            tcs.SetResult(result);
        }
        public void FailNext(Exception exception)
        {
            TaskCompletionSource<QuickSettingsMutationResult> tcs;
            lock (_lock) tcs = _pending.Dequeue();
            tcs.SetException(exception);
        }
    }

    private static QuickSettingsMutationResult Success(QuickSettingsPageSnapshot page) => new(true, null, page);
    private static QuickSettingsMutationResult Failure(string message, QuickSettingsPageSnapshot page) => new(false, message, page);

    private static async Task SpinUntilAsync(Func<bool> condition, string because, UiThreadStub? uiThread = null)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            uiThread?.Pump();
            if (condition()) return;
            if (sw.ElapsedMilliseconds > 5000) Assert.Fail($"Timed out waiting: {because}");
            await Task.Delay(10);
        }
    }

    // --- Pending key identity (section 47.A/B) ------------------------------------------------

    [Fact]
    public void Independent_sliders_get_distinct_row_keyed_pending_entries()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(), mutate.Func, delay.Func);

        Assert.True(binding.ScheduleSlider(QuickSettingsRowId.DeviceCpuBoostAc, QuickSettingsValue.Integer(40)));
        Assert.True(binding.ScheduleSlider(QuickSettingsRowId.DeviceCpuBoostDc, QuickSettingsValue.Integer(20)));

        Assert.Equal(2, binding.PendingKeys.Count);
    }

    [Fact]
    public void Grouped_sliders_share_one_group_keyed_pending_entry()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(), mutate.Func, delay.Func);

        Assert.True(binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26)));
        Assert.True(binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpDcPl2, QuickSettingsValue.Integer(22)));

        Assert.Single(binding.PendingKeys);
    }

    // --- Independent slider intent (section 47.C) ---------------------------------------------

    [Fact]
    public async Task Independent_slider_submits_exactly_one_row_value()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(), mutate.Func, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceCpuBoostAc, QuickSettingsValue.Integer(40));
        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "independent slider submitted");

        var intent = mutate.Calls[0];
        Assert.Equal(QuickSettingsPageId.Device, intent.PageId);
        Assert.Null(intent.AppId);
        Assert.Equal(QuickSettingsRowId.DeviceCpuBoostAc, intent.EditedRowId);
        var value = Assert.Single(intent.Values);
        Assert.Equal(QuickSettingsRowId.DeviceCpuBoostAc, value.RowId);
        Assert.Equal(40, value.Value.IntegerValue);
    }

    // --- Group seeding / reuse (section 47.D/E/F) -----------------------------------------------

    [Fact]
    public async Task Group_first_edit_seeds_the_whole_section_in_row_order()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(pl1Ac: 20, pl2Ac: 25, pl1Dc: 15, pl2Dc: 20), mutate.Func, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsValue.Integer(18));
        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "grouped mutation submitted");

        var intent = mutate.Calls[0];
        Assert.Equal(QuickSettingsRowId.DeviceTdpDcPl1, intent.EditedRowId);
        Assert.Equal(5, intent.Values.Count);
        Assert.Equal(
        [
            QuickSettingsRowId.DeviceTdpEnabled,
            QuickSettingsRowId.DeviceTdpAcPl1,
            QuickSettingsRowId.DeviceTdpAcPl2,
            QuickSettingsRowId.DeviceTdpDcPl1,
            QuickSettingsRowId.DeviceTdpDcPl2,
        ], intent.Values.Select(v => v.RowId));
        Assert.True(intent.Values.Single(v => v.RowId == QuickSettingsRowId.DeviceTdpEnabled).Value.BooleanValue);
        Assert.Equal(20, intent.Values.Single(v => v.RowId == QuickSettingsRowId.DeviceTdpAcPl1).Value.IntegerValue);
        Assert.Equal(18, intent.Values.Single(v => v.RowId == QuickSettingsRowId.DeviceTdpDcPl1).Value.IntegerValue);
    }

    [Fact]
    public async Task Group_later_edit_reuses_the_pending_whole_draft_and_only_the_newest_is_submitted()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(), mutate.Func, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26));
        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpDcPl2, QuickSettingsValue.Integer(30));
        Assert.Single(binding.PendingKeys); // still one pending group, not two independent timers

        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "single group commit submitted");

        var intent = mutate.Calls[0];
        Assert.Equal(QuickSettingsRowId.DeviceTdpDcPl2, intent.EditedRowId);
        // AC PL1's earlier edit (26, plus its own linked bump of AC PL2) survives inside the reused draft.
        Assert.Equal(26, intent.Values.Single(v => v.RowId == QuickSettingsRowId.DeviceTdpAcPl1).Value.IntegerValue);
        Assert.Equal(30, intent.Values.Single(v => v.RowId == QuickSettingsRowId.DeviceTdpDcPl2).Value.IntegerValue);
    }

    [Fact]
    public void Missing_section_value_fails_grouped_scheduling_closed()
    {
        var page = TdpPage() with
        {
            Sections =
            [
                new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP",
                [
                    Toggle(QuickSettingsRowId.DeviceTdpEnabled, true) with { Value = null },
                    Numeric(QuickSettingsRowId.DeviceTdpAcPl1, 20, 8, 30, QuickSettingsCommitGroupId.DeviceTdpConfiguration),
                    Numeric(QuickSettingsRowId.DeviceTdpAcPl2, 25, 8, 37, QuickSettingsCommitGroupId.DeviceTdpConfiguration),
                    Numeric(QuickSettingsRowId.DeviceTdpDcPl1, 15, 8, 30, QuickSettingsCommitGroupId.DeviceTdpConfiguration),
                    Numeric(QuickSettingsRowId.DeviceTdpDcPl2, 20, 8, 37, QuickSettingsCommitGroupId.DeviceTdpConfiguration),
                ]),
            ],
        };
        var mutate = new GatedMutate();
        using var binding = NewBinding(page, mutate.Func);

        var scheduled = binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26));

        Assert.False(scheduled);
        Assert.Empty(binding.PendingKeys);
        Assert.Empty(mutate.Calls);
    }

    // --- Linked constraints (section 47.G/H/I/J) ------------------------------------------------

    [Fact]
    public void Linked_lower_edit_raises_the_upper_companion_within_bounds()
    {
        var linked = new[] { new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2, 2) };
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(pl1Ac: 25, pl2Ac: 26, linked: linked), mutate.Func, new ManualDelay().Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26));

        Assert.True(binding.TryGetPendingValue(QuickSettingsRowId.DeviceTdpAcPl1, out var lower));
        Assert.Equal(26, lower.IntegerValue);
        Assert.True(binding.TryGetPendingValue(QuickSettingsRowId.DeviceTdpAcPl2, out var upper));
        Assert.Equal(28, upper.IntegerValue); // companion corrected immediately, before settlement
    }

    [Fact]
    public void Linked_lower_edit_clamps_itself_when_the_upper_maximum_is_the_binding_limit()
    {
        // Upper is already at its ceiling (37): raising lower past (upper - gap) must clamp lower
        // instead of pushing upper out of range.
        var linked = new[] { new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2, 2) };
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(pl1Ac: 20, pl2Ac: 37, linked: linked), mutate.Func, new ManualDelay().Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(36));

        Assert.True(binding.TryGetPendingValue(QuickSettingsRowId.DeviceTdpAcPl1, out var lower));
        Assert.Equal(35, lower.IntegerValue); // clamped to upperMax(37) - gap(2)
        Assert.True(binding.TryGetPendingValue(QuickSettingsRowId.DeviceTdpAcPl2, out var upper));
        Assert.Equal(37, upper.IntegerValue); // untouched
    }

    [Fact]
    public void Linked_upper_edit_lowers_the_lower_companion_within_bounds()
    {
        var linked = new[] { new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2, 2) };
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(pl1Ac: 20, pl2Ac: 22, linked: linked), mutate.Func, new ManualDelay().Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsValue.Integer(21));

        Assert.True(binding.TryGetPendingValue(QuickSettingsRowId.DeviceTdpAcPl2, out var upper));
        Assert.Equal(21, upper.IntegerValue);
        Assert.True(binding.TryGetPendingValue(QuickSettingsRowId.DeviceTdpAcPl1, out var lower));
        Assert.Equal(19, lower.IntegerValue); // companion corrected immediately
    }

    [Fact]
    public void Linked_upper_edit_clamps_itself_when_the_lower_minimum_is_the_binding_limit()
    {
        var linked = new[] { new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2, 2) };
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(pl1Ac: 8, pl2Ac: 20, linked: linked), mutate.Func, new ManualDelay().Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsValue.Integer(9));

        Assert.True(binding.TryGetPendingValue(QuickSettingsRowId.DeviceTdpAcPl2, out var upper));
        Assert.Equal(10, upper.IntegerValue); // clamped to lowerMin(8) + gap(2)
        Assert.True(binding.TryGetPendingValue(QuickSettingsRowId.DeviceTdpAcPl1, out var lower));
        Assert.Equal(8, lower.IntegerValue); // untouched
    }

    [Fact]
    public void Edit_outside_any_linked_constraint_only_changes_the_edited_row()
    {
        // Independent (non-grouped) sliders: no linked constraint declares them, and unlike a TDP
        // group edit (which seeds every row in the section) an independent draft carries only the
        // edited row -- its sibling must never appear in the pending draft at all.
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(), mutate.Func, new ManualDelay().Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceCpuBoostAc, QuickSettingsValue.Integer(40));

        Assert.True(binding.TryGetPendingValue(QuickSettingsRowId.DeviceCpuBoostAc, out var ac));
        Assert.Equal(40, ac.IntegerValue);
        Assert.False(binding.TryGetPendingValue(QuickSettingsRowId.DeviceCpuBoostDc, out _));
    }

    // --- Discrete/numeric row validity (section 48) ---------------------------------------------

    [Fact]
    public void FindDiscreteIndex_uses_ordered_non_contiguous_option_values()
    {
        Assert.Equal(1, QuickSettingsRowRendering.FindDiscreteIndex(NonContiguousOptions, 20));
        Assert.Equal(2, QuickSettingsRowRendering.FindDiscreteIndex(NonContiguousOptions, 40));
        Assert.Equal(-1, QuickSettingsRowRendering.FindDiscreteIndex(NonContiguousOptions, 99));
    }

    [Fact]
    public void IsWellFormed_rejects_a_toggle_with_a_non_boolean_value()
    {
        var row = Numeric(QuickSettingsRowId.DeviceTdpEnabled, 5, 0, 10) with { ControlKind = QuickSettingsControlKind.Toggle };
        Assert.False(QuickSettingsRowRendering.IsWellFormed(row));
    }

    [Fact]
    public void IsWellFormed_rejects_a_slider_without_a_slider_spec()
    {
        var row = Numeric(QuickSettingsRowId.DeviceTdpAcPl1, 20, 8, 30) with { SliderSpec = null };
        Assert.False(QuickSettingsRowRendering.IsWellFormed(row));
    }

    [Fact]
    public void IsWellFormed_rejects_a_numeric_slider_with_an_invalid_range()
    {
        var row = Numeric(QuickSettingsRowId.DeviceTdpAcPl1, 20, 30, 8); // min > max
        Assert.False(QuickSettingsRowRendering.IsWellFormed(row));
    }

    [Fact]
    public void IsWellFormed_rejects_a_discrete_slider_with_no_options()
    {
        var row = Discrete(QuickSettingsRowId.DeviceCpuBoostAc, 20, options: []);
        Assert.False(QuickSettingsRowRendering.IsWellFormed(row));
    }

    [Fact]
    public void IsWellFormed_accepts_well_formed_rows()
    {
        Assert.True(QuickSettingsRowRendering.IsWellFormed(Toggle(QuickSettingsRowId.DeviceTdpEnabled, true)));
        Assert.True(QuickSettingsRowRendering.IsWellFormed(Numeric(QuickSettingsRowId.DeviceTdpAcPl1, 20, 8, 30)));
        Assert.True(QuickSettingsRowRendering.IsWellFormed(Discrete(QuickSettingsRowId.DeviceCpuBoostAc, 20)));
    }

    // A row with Available=true but no known/desired side (e.g. CPU Boost/Power Mode enabled with
    // neither a desired nor a known current value) is a normal supported Runtime read-failure shape,
    // not a theoretical case -- it must fail closed rather than render an arbitrary option.

    [Fact]
    public void Numeric_slider_with_a_null_value_fails_closed()
    {
        var row = Numeric(QuickSettingsRowId.DeviceTdpAcPl1, 20, 8, 30) with { Value = null };
        Assert.False(QuickSettingsRowRendering.IsWellFormed(row));
    }

    [Fact]
    public void Discrete_slider_with_a_null_value_fails_closed()
    {
        var row = Discrete(QuickSettingsRowId.DeviceCpuBoostAc, 20) with { Value = null };
        Assert.False(QuickSettingsRowRendering.IsWellFormed(row));
    }

    [Fact]
    public void Discrete_slider_with_an_unknown_product_value_fails_closed()
    {
        // 99 is not one of NonContiguousOptions' 10/20/40 -- must not silently select index 0.
        var row = Discrete(QuickSettingsRowId.DeviceCpuBoostAc, 99);
        Assert.False(QuickSettingsRowRendering.IsWellFormed(row));
    }

    [Fact]
    public void Toggle_with_a_null_value_is_well_formed_but_a_non_boolean_value_is_not()
    {
        Assert.True(QuickSettingsRowRendering.IsWellFormed(Toggle(QuickSettingsRowId.DeviceTdpEnabled, true) with { Value = null }));
        var malformed = Numeric(QuickSettingsRowId.DeviceTdpEnabled, 5, 0, 10) with { ControlKind = QuickSettingsControlKind.Toggle };
        Assert.False(QuickSettingsRowRendering.IsWellFormed(malformed));
    }

    // --- Effective value / authoritative refresh (section 49) -----------------------------------

    [Fact]
    public void Pending_draft_survives_an_ordinary_authoritative_page_refresh()
    {
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(pl1Ac: 20, pl2Ac: 25), mutate.Func, new ManualDelay().Func);
        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(29));

        // Runtime republishes the same 20/25 authority mid-debounce (e.g. an unrelated invalidation).
        binding.ApplyAuthoritativePage(TdpPage(pl1Ac: 20, pl2Ac: 25));
        var effective = binding.BuildEffectivePage();

        var pl1 = effective.Sections[0].Rows.Single(r => r.RowId == QuickSettingsRowId.DeviceTdpAcPl1);
        Assert.Equal(29, pl1.Value!.IntegerValue); // pending draft still wins
    }

    [Fact]
    public void Unrelated_rows_adopt_the_newest_authoritative_page_while_another_row_is_pending()
    {
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(), mutate.Func, new ManualDelay().Func);
        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(29));

        binding.ApplyAuthoritativePage(TdpPage() with
        {
            Sections =
            [
                TdpPage().Sections[0],
                new QuickSettingsSection(QuickSettingsSectionId.DeviceCpuBoost, "CPU Boost",
                [
                    Toggle(QuickSettingsRowId.DeviceCpuBoostEnabled, true),
                    Discrete(QuickSettingsRowId.DeviceCpuBoostAc, 40), // changed from 20 to 40
                    Discrete(QuickSettingsRowId.DeviceCpuBoostDc, 10),
                ]),
            ],
        });
        var effective = binding.BuildEffectivePage();

        var cpuAc = effective.Sections[1].Rows.Single(r => r.RowId == QuickSettingsRowId.DeviceCpuBoostAc);
        Assert.Equal(40, cpuAc.Value!.IntegerValue); // unrelated row adopts new authority immediately
    }

    [Fact]
    public async Task Current_success_settlement_clears_pending_and_applies_the_result_page()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        var uiThread = new UiThreadStub();
        using var binding = NewBinding(TdpPage(), mutate.Func, uiThread, delay.Func);
        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26));

        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "commit submitted", uiThread);
        var settledPage = TdpPage(pl1Ac: 26, pl2Ac: 27);
        mutate.CompleteNext(Success(settledPage));

        await SpinUntilAsync(() => binding.PendingKeys.Count == 0, "settlement applied", uiThread);
        Assert.Same(settledPage, binding.AuthoritativePage);
        Assert.Null(binding.LastLocalFailureMessage);
    }

    [Fact]
    public async Task Current_typed_failure_clears_pending_applies_the_result_page_and_keeps_the_message()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        var uiThread = new UiThreadStub();
        using var binding = NewBinding(TdpPage(), mutate.Func, uiThread, delay.Func);
        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26));

        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "commit submitted", uiThread);
        var settledPage = TdpPage(); // apply may have partially persisted -- still a real authoritative page
        mutate.CompleteNext(Failure("TDP apply failed", settledPage));

        await SpinUntilAsync(() => binding.PendingKeys.Count == 0, "failure settlement applied", uiThread);
        Assert.Same(settledPage, binding.AuthoritativePage);
        Assert.Equal("TDP apply failed", binding.LastLocalFailureMessage);
    }

    [Fact]
    public async Task Operation_failure_retires_pending_without_synthesizing_a_fake_page()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        var uiThread = new UiThreadStub();
        var originalPage = TdpPage();
        using var binding = NewBinding(originalPage, mutate.Func, uiThread, delay.Func);
        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26));

        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "commit submitted", uiThread);
        mutate.FailNext(new IOException("pipe broke"));

        await SpinUntilAsync(() => binding.PendingKeys.Count == 0, "operation failure retired the draft", uiThread);
        Assert.Same(originalPage, binding.AuthoritativePage); // cached authority, never a fake page
        Assert.Equal("pipe broke", binding.LastLocalFailureMessage);
    }

    [Fact]
    public async Task Stale_settlement_key_reuse_does_not_apply_over_a_superseding_draft()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        var uiThread = new UiThreadStub();
        var originalPage = TdpPage();
        using var binding = NewBinding(originalPage, mutate.Func, uiThread, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26));
        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "A submitted", uiThread);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpDcPl2, QuickSettingsValue.Integer(30)); // B supersedes A (same group key)

        mutate.CompleteNext(Success(TdpPage(pl1Ac: 26))); // A settles late
        await Task.Delay(60); // let the background continuation enqueue A's (stale) settlement action
        uiThread.Pump();
        Assert.Same(originalPage, binding.AuthoritativePage); // A's settlement ignored as stale
        Assert.Single(binding.PendingKeys); // B still pending

        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 2, "B submitted", uiThread);
        var settledPage = TdpPage(pl2Dc: 30);
        mutate.CompleteNext(Success(settledPage));
        await SpinUntilAsync(() => binding.PendingKeys.Count == 0, "B settled", uiThread);
        Assert.Same(settledPage, binding.AuthoritativePage);
    }

    // --- Immediate Toggle (section 50) -----------------------------------------------------------

    [Fact]
    public async Task Immediate_toggle_submits_exactly_one_row_value_through_the_generic_seam()
    {
        var mutate = new GatedMutate();
        var page = TdpPage();
        using var binding = NewBinding(page, mutate.Func);
        var resultPage = TdpPage(tdpEnabled: false);
        var task = binding.SubmitImmediateToggleAsync(QuickSettingsRowId.DeviceTdpEnabled, false);
        mutate.CompleteNext(Success(resultPage));
        Assert.True(await task);

        var intent = Assert.Single(mutate.Calls);
        Assert.Equal(QuickSettingsPageId.Device, intent.PageId);
        Assert.Equal(QuickSettingsRowId.DeviceTdpEnabled, intent.EditedRowId);
        var value = Assert.Single(intent.Values);
        Assert.Equal(QuickSettingsRowId.DeviceTdpEnabled, value.RowId);
        Assert.False(value.Value.BooleanValue);
        Assert.Same(resultPage, binding.AuthoritativePage);
    }

    [Fact]
    public async Task Immediate_toggle_cancels_unsubmitted_same_section_pending_work()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(), mutate.Func, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26)); // same section (TDP)
        Assert.Single(binding.PendingKeys);

        var task = binding.SubmitImmediateToggleAsync(QuickSettingsRowId.DeviceTdpEnabled, false);
        mutate.CompleteNext(Success(TdpPage(tdpEnabled: false)));
        await task;

        Assert.Empty(binding.PendingKeys); // the pending TDP group draft never fires later
        delay.Elapse();
        await Task.Delay(40);
        Assert.Single(mutate.Calls); // only the immediate toggle -- the cancelled slider never submitted
    }

    [Fact]
    public async Task Immediate_toggle_leaves_an_other_section_pending_draft_intact()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(), mutate.Func, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceCpuBoostAc, QuickSettingsValue.Integer(40)); // CPU Boost section
        var task = binding.SubmitImmediateToggleAsync(QuickSettingsRowId.DeviceTdpEnabled, false); // TDP section
        mutate.CompleteNext(Success(TdpPage(tdpEnabled: false)));
        await task;

        Assert.Single(binding.PendingKeys); // the unrelated CPU Boost draft is untouched
    }

    [Fact]
    public async Task Slider_edit_is_rejected_while_immediate_parent_toggle_is_in_flight()
    {
        // Otherwise a grouped draft could seed itself from the pre-toggle authoritative section
        // (DeviceTdpEnabled still true) and, two seconds later, re-enable TDP right after the user
        // turned it off -- a normal UI/RPC latency path, not a theoretical race.
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(), mutate.Func, delay.Func);

        var toggleTask = binding.SubmitImmediateToggleAsync(QuickSettingsRowId.DeviceTdpEnabled, false);
        Assert.Single(mutate.Calls); // toggle submitted synchronously (Immediate policy, no debounce)

        Assert.False(binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26)));
        Assert.Empty(binding.PendingKeys);

        mutate.CompleteNext(Success(TdpPage(tdpEnabled: false)));
        Assert.True(await toggleTask);

        delay.Elapse();
        await Task.Delay(40);
        Assert.Single(mutate.Calls); // no stale grouped TDP mutation can follow the OFF result
    }

    [Fact]
    public async Task Immediate_toggle_typed_failure_still_applies_the_result_page()
    {
        var mutate = new GatedMutate();
        using var binding = NewBinding(TdpPage(), mutate.Func);
        var resultPage = TdpPage();
        var task = binding.SubmitImmediateToggleAsync(QuickSettingsRowId.DeviceTdpEnabled, false);
        mutate.CompleteNext(Failure("TDP toggle failed", resultPage));
        await task;

        Assert.Same(resultPage, binding.AuthoritativePage);
        Assert.Equal("TDP toggle failed", binding.LastLocalFailureMessage);
    }

    [Fact]
    public async Task Unavailable_or_non_writable_toggle_emits_no_mutation()
    {
        var mutate = new GatedMutate();
        var page = TdpPage() with
        {
            Sections =
            [
                new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP",
                [
                    Toggle(QuickSettingsRowId.DeviceTdpEnabled, true, writable: false),
                ]),
            ],
        };
        using var binding = NewBinding(page, mutate.Func);

        var submitted = await binding.SubmitImmediateToggleAsync(QuickSettingsRowId.DeviceTdpEnabled, false);

        Assert.False(submitted);
        Assert.Empty(mutate.Calls);
    }

    // --- Hide / teardown (section 39/40) -----------------------------------------------------------

    [Fact]
    public async Task CancelUnsubmittedDrafts_drops_unsubmitted_work_but_leaves_in_flight_work_to_settle()
    {
        // One shared ManualDelay elapses every currently-live wait, so the TDP group must already be
        // past its delay (in flight) BEFORE the CPU Boost draft is scheduled -- otherwise a single
        // Elapse() would race both timers at once instead of isolating "already submitted" from
        // "still unsubmitted".
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        var uiThread = new UiThreadStub();
        using var binding = NewBinding(TdpPage(), mutate.Func, uiThread, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26));
        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "TDP group already in flight", uiThread);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceCpuBoostAc, QuickSettingsValue.Integer(40)); // still unsubmitted
        Assert.Equal(2, binding.PendingKeys.Count);

        binding.CancelUnsubmittedDrafts();

        Assert.Single(binding.PendingKeys); // the in-flight TDP group is left to settle
        mutate.CompleteNext(Success(TdpPage(pl1Ac: 26)));
        await SpinUntilAsync(() => binding.PendingKeys.Count == 0, "in-flight settlement still delivered", uiThread);
    }

    [Fact]
    public async Task Dispose_suppresses_a_settlement_that_arrives_after_teardown()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        var uiThread = new UiThreadStub();
        var originalPage = TdpPage();
        var binding = NewBinding(originalPage, mutate.Func, uiThread, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(26));
        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "commit in flight", uiThread);

        binding.Dispose();
        mutate.CompleteNext(Success(TdpPage(pl1Ac: 26)));
        await Task.Delay(60); // let the background continuation enqueue the post-teardown settlement
        uiThread.Pump();

        Assert.Same(originalPage, binding.AuthoritativePage); // stale settlement never applied post-teardown
    }

    // --- SF-V2-09 section 17: Profile (PageId, AppId) context safety --------------------------------

    [Fact]
    public async Task Profile_intent_carries_the_target_app_id()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer(40));
        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "profile slider submitted");

        var intent = mutate.Calls[0];
        Assert.Equal(QuickSettingsPageId.Profile, intent.PageId);
        Assert.Equal(480u, intent.AppId);
        Assert.Equal(QuickSettingsRowId.ProfileCpuBoostAc, intent.EditedRowId);
    }

    [Fact]
    public async Task Profile_tdp_group_emits_one_grouped_mutation_with_the_target_app_id()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.ProfileTdpAcPl1, QuickSettingsValue.Integer(26));
        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "profile TDP group submitted");

        var intent = mutate.Calls[0];
        Assert.Equal(QuickSettingsPageId.Profile, intent.PageId);
        Assert.Equal(480u, intent.AppId);
        Assert.Equal(5, intent.Values.Count);
        Assert.Equal(
        [
            QuickSettingsRowId.ProfileTdpEnabled,
            QuickSettingsRowId.ProfileTdpAcPl1,
            QuickSettingsRowId.ProfileTdpAcPl2,
            QuickSettingsRowId.ProfileTdpDcPl1,
            QuickSettingsRowId.ProfileTdpDcPl2,
        ], intent.Values.Select(v => v.RowId));
    }

    [Fact]
    public async Task Context_change_retires_an_old_profile_pending_draft()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer(40));
        Assert.Single(binding.PendingKeys);

        binding.ApplyAuthoritativePage(ProfilePage(490)); // active game switched from 480 to 490

        Assert.Empty(binding.PendingKeys);
        Assert.Equal(490u, binding.AuthoritativePage.AppId);

        delay.Elapse();
        await Task.Delay(40);
        Assert.Empty(mutate.Calls); // the retired draft's timer never fires
    }

    [Fact]
    public async Task Late_delayed_result_for_a_retired_profile_context_cannot_replace_the_new_context()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        var uiThread = new UiThreadStub();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func, uiThread, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer(40));
        delay.Elapse();
        await SpinUntilAsync(() => mutate.Calls.Count == 1, "A (480) submitted", uiThread);

        var bPage = ProfilePage(490); // active game switched to B (490) while A's mutation is in flight
        binding.ApplyAuthoritativePage(bPage);

        mutate.CompleteNext(Success(ProfilePage(480, cpuAc: 40))); // A's late result arrives
        await Task.Delay(60);
        uiThread.Pump();

        Assert.Same(bPage, binding.AuthoritativePage); // B remains current; A's result is discarded
        Assert.Null(binding.LastLocalFailureMessage);
    }

    [Fact]
    public async Task Late_immediate_toggle_result_for_a_retired_profile_context_cannot_replace_the_new_context()
    {
        var mutate = new GatedMutate();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func);

        var task = binding.SubmitImmediateToggleAsync(QuickSettingsRowId.ProfileEnabled, false); // targets A (480)
        Assert.Single(mutate.Calls);

        var bPage = ProfilePage(490); // active game switched to B before A's result arrives
        binding.ApplyAuthoritativePage(bPage);

        mutate.CompleteNext(Success(ProfilePage(480, profileEnabled: false))); // A's late result
        Assert.True(await task); // a mutation was genuinely submitted -- the caller's re-render is a no-op

        Assert.Same(bPage, binding.AuthoritativePage); // B remains current; A's result is discarded
    }

    // PR #510 review: an operation/transport exception has no Page to compare, so
    // IsStaleForCurrentContext alone cannot guard it -- SubmitImmediateToggleAsync must independently
    // remember which context it submitted for and only surface the exception if that context is
    // still current when the catch runs.
    [Fact]
    public async Task Late_operation_failure_for_a_retired_profile_context_does_not_leak_into_the_new_context()
    {
        var mutate = new GatedMutate();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func);

        var task = binding.SubmitImmediateToggleAsync(QuickSettingsRowId.ProfileEnabled, false); // targets A (480)
        Assert.Single(mutate.Calls);

        var bPage = ProfilePage(490); // active game switched to B before A's request fails
        binding.ApplyAuthoritativePage(bPage);

        mutate.FailNext(new InvalidOperationException("pipe broke for A"));
        Assert.False(await task);

        Assert.Same(bPage, binding.AuthoritativePage); // B remains current
        Assert.Null(binding.LastLocalFailureMessage); // A's exception must not leak onto B
    }

    // The same-context counterpart: an operation failure that arrives while the context has NOT
    // changed must still surface its message, exactly as before this fix.
    [Fact]
    public async Task Same_context_operation_failure_still_surfaces_its_message()
    {
        var mutate = new GatedMutate();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func);

        var task = binding.SubmitImmediateToggleAsync(QuickSettingsRowId.ProfileEnabled, false);
        mutate.FailNext(new InvalidOperationException("pipe broke"));
        Assert.False(await task);

        Assert.Equal("pipe broke", binding.LastLocalFailureMessage);
    }

    [Fact]
    public void Same_context_valid_profile_pending_draft_survives_an_ordinary_refresh()
    {
        var mutate = new GatedMutate();
        using var binding = NewProfileBinding(ProfilePage(480, cpuAc: 20), mutate.Func, new ManualDelay().Func);
        binding.ScheduleSlider(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer(40));

        binding.ApplyAuthoritativePage(ProfilePage(480, cpuAc: 20)); // same (Profile, 480) context, unrelated refresh

        var effective = binding.BuildEffectivePage();
        var cpuAc = effective.Sections.SelectMany(s => s.Rows).Single(r => r.RowId == QuickSettingsRowId.ProfileCpuBoostAc);
        Assert.Equal(40, cpuAc.Value!.IntegerValue); // pending draft still wins
    }

    [Fact]
    public async Task Fresh_page_prunes_a_pending_row_that_became_absent()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func, delay.Func);
        binding.ScheduleSlider(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer(40));
        Assert.Single(binding.PendingKeys);

        var withoutCpuBoostSection = ProfilePage(480) with
        {
            Sections = ProfilePage(480).Sections.Where(s => s.SectionId != QuickSettingsSectionId.ProfileCpuBoost).ToArray(),
        };
        binding.ApplyAuthoritativePage(withoutCpuBoostSection);

        Assert.Empty(binding.PendingKeys);
        delay.Elapse();
        await Task.Delay(40);
        Assert.Empty(mutate.Calls); // the pruned draft never fires
    }

    [Fact]
    public async Task Fresh_page_prunes_a_pending_row_that_became_non_writable()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func, delay.Func);
        binding.ScheduleSlider(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer(40));
        Assert.Single(binding.PendingKeys);

        binding.ApplyAuthoritativePage(ProfilePage(480, profileEnabled: false)); // same context; now non-writable

        Assert.Empty(binding.PendingKeys);
        delay.Elapse();
        await Task.Delay(40);
        Assert.Empty(mutate.Calls);
    }

    // The exact scenario the PR #509 QAM review flagged, now proven for the Overlay C# binder:
    // ProfileEnabled lives in ProfileGeneral, a section with no sliders of its own, so same-section
    // cancellation (CancelUnsubmittedInSection) cannot reach a pending TDP/CPU Boost/Power Mode child
    // draft in a DIFFERENT section. The generic same-context prune (not a ProfileEnabled special
    // case) is what retires them once the OFF result page makes them non-writable.
    [Fact]
    public async Task Profile_master_off_result_retires_every_pending_child_draft_without_feature_specific_logic()
    {
        var delay = new ManualDelay();
        var mutate = new GatedMutate();
        var uiThread = new UiThreadStub();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func, uiThread, delay.Func);

        binding.ScheduleSlider(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer(40)); // CPU Boost section
        binding.ScheduleSlider(QuickSettingsRowId.ProfileTdpAcPl1, QuickSettingsValue.Integer(26)); // TDP section
        Assert.Equal(2, binding.PendingKeys.Count);

        var task = binding.SubmitImmediateToggleAsync(QuickSettingsRowId.ProfileEnabled, false); // ProfileGeneral section
        mutate.CompleteNext(Success(ProfilePage(480, profileEnabled: false)));
        await task;

        Assert.Empty(binding.PendingKeys); // generic prune retired both cross-section drafts

        delay.Elapse();
        await Task.Delay(40);
        uiThread.Pump();
        Assert.Single(mutate.Calls); // only the master toggle -- neither child draft ever committed
    }

    [Fact]
    public async Task Context_change_clears_a_stale_local_failure_message()
    {
        var mutate = new GatedMutate();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func);
        var task = binding.SubmitImmediateToggleAsync(QuickSettingsRowId.ProfileEnabled, false);
        mutate.CompleteNext(Failure("Profile toggle failed", ProfilePage(480)));
        await task;
        Assert.Equal("Profile toggle failed", binding.LastLocalFailureMessage);

        binding.ApplyAuthoritativePage(ProfilePage(490));

        Assert.Null(binding.LastLocalFailureMessage);
    }

    [Fact]
    public async Task An_ordinary_same_context_refresh_clears_a_stale_local_failure_message()
    {
        var mutate = new GatedMutate();
        using var binding = NewProfileBinding(ProfilePage(480), mutate.Func);
        var task = binding.SubmitImmediateToggleAsync(QuickSettingsRowId.ProfileEnabled, false);
        mutate.CompleteNext(Failure("Profile toggle failed", ProfilePage(480)));
        await task;
        Assert.Equal("Profile toggle failed", binding.LastLocalFailureMessage);

        binding.ApplyAuthoritativePage(ProfilePage(480)); // same context, ordinary Runtime refresh

        Assert.Null(binding.LastLocalFailureMessage);
    }

    [Fact]
    public void Applying_a_page_with_the_wrong_page_id_is_ignored()
    {
        var mutate = new GatedMutate();
        var originalPage = ProfilePage(480);
        using var binding = NewProfileBinding(originalPage, mutate.Func);

        binding.ApplyAuthoritativePage(TdpPage()); // a Device page reaching the Profile binding

        Assert.Same(originalPage, binding.AuthoritativePage); // ignored -- fail closed, no corruption
    }
}
