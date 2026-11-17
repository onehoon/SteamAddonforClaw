using System.IO;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// The editor is WinUI, so verify its wiring through source contracts and exercise the shared
// selection model with the stable row identities used by the renderer.
public sealed class AddonQuickSettingsTabOrderEditorTests
{
    private static OverlayRowCapabilities Selectable() => new(() => true);

    [Fact]
    public void Tab_order_rows_expose_selection_capabilities_and_adjustment()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayTabOrderRow.cs");

        Assert.Contains("Container = OverlayRowChrome.Create(grid);", source);
        Assert.Contains("internal OverlayRowCapabilities Capabilities { get; }", source);
        Assert.Contains("Adjust: delta =>", source);
    }

    [Fact]
    public void Authoritative_reorder_preserves_the_selected_tab_row_identity()
    {
        var state = new OverlayTabState();
        var rowByTab = state.Order.ToDictionary(tab => tab, _ => Selectable());
        var selection = new OverlayRowSelection();
        selection.SetRows(state.Order.Select(tab => rowByTab[tab]).ToArray());
        Assert.True(selection.TrySelect(IndexOf(state.Order, AddonQuickSettingsTabId.Controller)));
        var selectedRow = rowByTab[AddonQuickSettingsTabId.Controller];

        IReadOnlyList<AddonQuickSettingsTabId> proposed =
        [
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Setting,
        ];

        Assert.True(state.TryApplyOrder(proposed));
        var reorderedRows = state.Order.Select(tab => rowByTab[tab]).ToArray();
        var selectedIndex = Array.FindIndex(reorderedRows, row => ReferenceEquals(row, selectedRow));
        selection.SetRows(reorderedRows, selectedIndex);

        Assert.Equal(1, selectedIndex);
        Assert.Equal(selectedIndex, selection.SelectedIndex);
        Assert.Same(selectedRow, selection.SelectedCapabilities);
    }

    [Fact]
    public void Runtime_reorder_reconciles_only_the_visible_tab_order_detail_without_scrolling()
    {
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var apply = ExtractMethod(shell, "internal void ApplyTabOrderState(");
        var activeDetailBlockStart = apply.IndexOf("if (tabOrderDetailActive && _tabOrderRows.Count == applied.Count)", StringComparison.Ordinal);
        var activeDetailBlockEnd = apply.IndexOf("ApplySelectedHeaderVisual();", activeDetailBlockStart, StringComparison.Ordinal);
        Assert.True(activeDetailBlockStart >= 0 && activeDetailBlockEnd > activeDetailBlockStart);
        var activeDetailBlock = apply[activeDetailBlockStart..activeDetailBlockEnd];

        Assert.Contains("Identity: SettingDetailId.TabOrder", apply);
        Assert.Contains("SelectedRowContainer(AddonQuickSettingsTabId.Setting)", apply);
        Assert.True(apply.IndexOf("selectedSettingContainer", StringComparison.Ordinal)
            < apply.IndexOf("_tabState.TryApplyOrder(normalized)", StringComparison.Ordinal));
        Assert.Contains("Grid.SetRow(editorRow.Container, position);", apply);
        Assert.Contains("BuildTabOrderDetailRows(applied)", activeDetailBlock);
        Assert.Contains("ReconcileRowSelection(", activeDetailBlock);
        Assert.Contains("bringIntoViewOnChange: false", activeDetailBlock);
        Assert.DoesNotContain("_rowSelection.SetRows", apply);
        Assert.DoesNotContain("ChangeView(", apply);
        Assert.DoesNotContain("StartBringIntoView", apply);
        Assert.DoesNotContain("ResolvePreferredSettingRowIndex", shell);
    }

    [Fact]
    public void Reorder_while_root_or_another_detail_is_active_does_not_replace_its_selection_set()
    {
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var apply = ExtractMethod(shell, "internal void ApplyTabOrderState(");
        var conditionalStart = apply.IndexOf("if (tabOrderDetailActive && _tabOrderRows.Count == applied.Count)", StringComparison.Ordinal);
        var conditionalEnd = apply.IndexOf("ApplySelectedHeaderVisual();", conditionalStart, StringComparison.Ordinal);
        Assert.True(conditionalStart >= 0 && conditionalEnd > conditionalStart);
        var afterConditional = apply[conditionalEnd..];

        Assert.Equal(1, CountOccurrences(apply, "_pageRows[AddonQuickSettingsTabId.Setting] = BuildTabOrderDetailRows(applied);"));
        Assert.Contains("_pageRows[AddonQuickSettingsTabId.Setting] = BuildTabOrderDetailRows(applied);", apply[conditionalStart..conditionalEnd]);
        Assert.DoesNotContain("ReconcileRowSelection(", afterConditional);
        Assert.DoesNotContain("_rowSelection.SetRows", afterConditional);
        Assert.DoesNotContain("ChangeView(", afterConditional);
    }

    [Fact]
    public void Tab_order_moves_remain_one_position_runtime_authoritative_intents()
    {
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var request = ExtractMethod(shell, "private void RequestTabOrderMove(");

        Assert.Contains("if (delta is not (-1 or 1)) return;", request);
        Assert.Contains("TabOrderMoveRequested?.Invoke(new(tab, delta));", request);
        Assert.DoesNotContain("TryApplyOrder", request);
        Assert.DoesNotContain("_tabState.Order", request);
    }

    [Fact]
    public void Next_show_selects_the_first_tab_from_the_latest_authoritative_order()
    {
        var state = new OverlayTabState();
        state.Select(AddonQuickSettingsTabId.Setting);
        IReadOnlyList<AddonQuickSettingsTabId> proposed =
        [
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Setting,
        ];
        Assert.True(state.TryApplyOrder(proposed));

        Assert.Equal(AddonQuickSettingsTabId.Setting, state.SelectedTab);
        state.ResetForShow();
        Assert.Equal(AddonQuickSettingsTabId.Profile, state.SelectedTab);
    }

    private static int IndexOf(IReadOnlyList<AddonQuickSettingsTabId> order, AddonQuickSettingsTabId tab)
    {
        for (var i = 0; i < order.Count; i++)
            if (order[i] == tab) return i;
        return -1;
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method: {signature}");
        var nextMethod = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        var nextInternal = source.IndexOf("\n    internal ", start + signature.Length, StringComparison.Ordinal);
        var end = new[] { nextMethod, nextInternal }.Where(index => index >= 0).DefaultIfEmpty(source.Length).Min();
        return source[start..end];
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts])).ReplaceLineEndings("\n");
    }
}
