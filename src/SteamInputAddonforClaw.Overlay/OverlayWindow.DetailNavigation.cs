using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private sealed record ActiveDetailPage(
        AddonQuickSettingsTabId Tab,
        object Identity,
        FrameworkElement RootView,
        FrameworkElement DetailView,
        IReadOnlyList<OverlayRow> RootRows,
        Border OriginContainer,
        double RootScrollOffset);

    private ActiveDetailPage? _activeDetailPage;

    private bool EnterDetailPage(
        AddonQuickSettingsTabId tab,
        object identity,
        FrameworkElement rootView,
        FrameworkElement detailView,
        IReadOnlyList<OverlayRow> detailRows,
        Border originContainer,
        string title)
    {
        if (_tabState.SelectedTab != tab || _activeDetailPage is not null ||
            !_pageRows.TryGetValue(tab, out var rootRows) ||
            !rootRows.Any(row => ReferenceEquals(row.Container, originContainer)))
            return false;

        _numericSliderRepeatTarget.Clear();
        _activeDetailPage = new ActiveDetailPage(
            tab,
            identity,
            rootView,
            detailView,
            rootRows,
            originContainer,
            BodyScroll.VerticalOffset);

        rootView.Visibility = Visibility.Collapsed;
        detailView.Visibility = Visibility.Visible;
        DetailBackButton.Visibility = Visibility.Visible;
        PageTitle.Text = title;
        PageTitle.Visibility = Visibility.Visible;
        _pageRows[tab] = detailRows;
        _rowSelection.SetRows(CapabilitiesFor(tab));
        ApplyRowSelectionVisual();
        ScrollActivePageToTop();
        return true;
    }

    private bool TryReturnFromDetailPage()
    {
        if (_activeDetailPage is not { } detail || _tabState.SelectedTab != detail.Tab)
            return false;

        var selectedDetailContainer = SelectedRowContainer(detail.Tab);
        if (selectedDetailContainer is not null &&
            !detail.RootRows.Any(row => ReferenceEquals(row.Container, selectedDetailContainer)) &&
            !ReferenceEquals(selectedDetailContainer.Background, RowUnselectedFillBrush))
            selectedDetailContainer.Background = RowUnselectedFillBrush;

        _activeDetailPage = null;
        detail.DetailView.Visibility = Visibility.Collapsed;
        detail.RootView.Visibility = Visibility.Visible;
        DetailBackButton.Visibility = Visibility.Collapsed;
        PageTitle.Text = LabelFor(detail.Tab);
        PageTitle.Visibility = Visibility.Visible;
        _pageRows[detail.Tab] = detail.RootRows;

        var originIndex = FindRowIndex(detail.RootRows, detail.OriginContainer);
        _rowSelection.SetRows(CapabilitiesFor(detail.Tab), originIndex);
        ApplyRowSelectionVisual();
        RestoreBodyScroll(detail.RootScrollOffset);
        return true;
    }

    private void ResetDetailForTabChangeOrShow()
    {
        if (_activeDetailPage is not { } detail)
        {
            DetailBackButton.Visibility = Visibility.Collapsed;
            return;
        }

        var selectedDetailContainer = SelectedRowContainer(detail.Tab);
        if (selectedDetailContainer is not null &&
            !detail.RootRows.Any(row => ReferenceEquals(row.Container, selectedDetailContainer)) &&
            !ReferenceEquals(selectedDetailContainer.Background, RowUnselectedFillBrush))
            selectedDetailContainer.Background = RowUnselectedFillBrush;

        _activeDetailPage = null;
        detail.DetailView.Visibility = Visibility.Collapsed;
        detail.RootView.Visibility = Visibility.Visible;
        DetailBackButton.Visibility = Visibility.Collapsed;
        PageTitle.Text = LabelFor(_tabState.SelectedTab);
        PageTitle.Visibility = Visibility.Visible;
        _pageRows[detail.Tab] = detail.RootRows;
    }

    private void ScrollActivePageToTop()
    {
        try
        {
            BodyScroll.ChangeView(null, 0, null, disableAnimation: true);
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Shell", "Could not reset the body scroll position on detail navigation.", exception);
        }
    }

    private void RestoreBodyScroll(double verticalOffset)
    {
        try
        {
            BodyScroll.UpdateLayout();
            BodyScroll.ChangeView(null, verticalOffset, null, disableAnimation: true);
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Shell", "Could not restore the body scroll position after detail navigation.", exception);
        }
    }

    private static int? FindRowIndex(IReadOnlyList<OverlayRow> rows, Border container)
    {
        for (var index = 0; index < rows.Count; index++)
            if (ReferenceEquals(rows[index].Container, container))
                return index;
        return null;
    }

    private void OnDetailBackButtonClick(object sender, RoutedEventArgs args) => TryReturnFromDetailPage();
}
