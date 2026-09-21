using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// SF-V2-07/09 section 52/32: OverlayWindow/App's actual WinUI wiring can't be exercised directly in
// this test project -- constructing OverlayWindow needs a XAML host, the same limitation
// OverlayToggleRowTests/OverlayValueRowTests already accept for the row primitives (validated on
// hardware per section 54/34). These are focused reflection/composition regressions over the
// compiled shape instead, mirroring the SF-V2-06 precedent (the removed-v6-dispatch-authority
// regression). SF-V2-09 extends this file's coverage from the Device-only generic renderer to the
// Device+Profile generic renderer without duplicating a separate Profile test framework.
public sealed class OverlayDeviceRendererWiringTests
{
    private const BindingFlags AnyInstance = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags AnyStatic = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    [Fact]
    public void The_device_preview_fixture_no_longer_exists()
    {
        var overlayWindowType = typeof(OverlayWindow);
        Assert.DoesNotContain(overlayWindowType.GetFields(AnyStatic), f => f.Name == "NavigationPreviewRowCount");
        Assert.DoesNotContain(overlayWindowType.GetFields(AnyInstance), f => f.Name == "_sliderPreviewCommit");
    }

    [Fact]
    public void OverlayDelayedSliderCommit_no_longer_owns_a_production_delay_constant()
    {
        Assert.DoesNotContain(typeof(OverlayDelayedSliderCommit).GetFields(AnyStatic), f => f.Name == "ProductionDelay");
    }

    [Fact]
    public void OverlayWindow_exposes_the_quick_settings_configuration_and_page_apply_seam()
    {
        var overlayWindowType = typeof(OverlayWindow);
        Assert.NotNull(overlayWindowType.GetMethod("ConfigureQuickSettings", AnyInstance));
        Assert.NotNull(overlayWindowType.GetMethod("ApplyQuickSettingsPage", AnyInstance));
    }

    [Fact]
    public void OverlayWindow_and_its_device_binder_never_hold_the_transport_client()
    {
        // Section 10.2: App owns NamedPipeOverlayClient; the Window/binder receive only a narrow
        // mutation delegate. No field on either type may be the client type itself.
        Assert.DoesNotContain(typeof(OverlayWindow).GetFields(AnyInstance), f => f.FieldType == typeof(NamedPipeOverlayClient));
        Assert.DoesNotContain(typeof(OverlayQuickSettingsPageBinding).GetFields(AnyInstance), f => f.FieldType == typeof(NamedPipeOverlayClient));
    }

    [Fact]
    public void WindowInterop_preserves_topmost_and_no_activate_contract_across_warm_show_hide()
    {
        var source = ReadWindowInteropSource();

        Assert.Contains("presenter.IsAlwaysOnTop = true;", source);
        Assert.Contains("private const long WsExTopmost = 0x00000008L;", source);
        Assert.Contains("Overlay topmost state verified.", source);
        Assert.Contains("Overlay topmost style is missing after a successful Show.", source);
        Assert.DoesNotContain("Activate()", source);
        Assert.DoesNotContain("SetForegroundWindow", source);

        var show = source[source.IndexOf("internal static void ShowWithoutActivation", StringComparison.Ordinal)..
            source.IndexOf("internal static void Hide", StringComparison.Ordinal)];
        Assert.Contains("SwpNoActivate", show);
        Assert.Contains("SwpNoZOrder", show);
        Assert.Contains("SwpShowWindow", show);

        var visibilityUpdate = show.IndexOf("SwpNoActivate | SwpNoSendChanging | SwpNoZOrder | SwpNoSize | SwpNoMove | SwpShowWindow", StringComparison.Ordinal);
        var topmostPromotion = show.IndexOf("HwndTopmost", StringComparison.Ordinal);
        Assert.True(visibilityUpdate >= 0);
        Assert.True(topmostPromotion > visibilityUpdate);
        Assert.Contains("SwpNoActivate | SwpNoSendChanging | SwpNoSize | SwpNoMove", show[topmostPromotion..]);
        Assert.DoesNotContain("SwpShowWindow", show[topmostPromotion..]);

        var hide = source[source.IndexOf("internal static void Hide", StringComparison.Ordinal)..
            source.IndexOf("private static void LogTopmostStateAfterShow", StringComparison.Ordinal)];
        Assert.Contains("SwpNoZOrder", hide);
        Assert.Contains("SwpNoSize", hide);
        Assert.Contains("SwpNoMove", hide);
        Assert.Contains("SwpHideWindow", hide);
    }

    [Fact]
    public void WindowInterop_forces_the_arrow_cursor_for_client_area_messages()
    {
        var source = ReadWindowInteropSource();

        Assert.Contains("private const uint WmSetCursor = 0x0020;", source);
        Assert.Contains("private const int HtClient = 1;", source);
        Assert.Contains("private static readonly nint IdcArrow = 32512;", source);
        Assert.Contains("case WmSetCursor:", source);
        Assert.Contains("var hitTest = unchecked((short)((long)lParam & 0xffff));", source);
        Assert.Contains("var arrowCursor = LoadCursor(IntPtr.Zero, IdcArrow);", source);
        Assert.Contains("SetCursor(arrowCursor);", source);
        Assert.Contains("return (nint)1;", source);
    }

    [Fact]
    public void WindowInterop_requests_windows_11_round_corners_without_making_them_show_fatal()
    {
        var source = ReadWindowInteropSource();

        Assert.Contains("private const uint DwmwaWindowCornerPreference = 33;", source);
        Assert.Contains("private const int DwmcpRound = 2;", source);
        Assert.Contains("ApplyRoundedCorners(hwnd);", source);
        Assert.Contains("DwmSetWindowAttribute", source);
        Assert.Contains("continuing with the usable square surface.", source);
        Assert.Contains("Interlocked.Exchange(ref _roundedCornerWarningLogged, 1)", source);
    }

    [Fact]
    public void Large_overlay_uses_the_neutral_surface_and_centered_scale_fade_contract()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml");
        var source = ReadOverlayWindowSource();

        Assert.Contains("RequestedTheme=\"Light\"", xaml);
        Assert.Contains("x:Key=\"OverlaySurfaceBrush\" Color=\"#FFE7E7E7\"", xaml);
        Assert.Contains("Background=\"{StaticResource OverlaySurfaceBrush}\"", xaml);
        Assert.DoesNotContain("#FFF3F3F3", xaml);

        Assert.Contains("private const float HiddenScale = 0.98f;", source);
        Assert.Contains("private const float HiddenTranslateYDip = 8.0f;", source);
        Assert.Contains("private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(140);", source);
        Assert.Contains("ElementCompositionPreview.GetElementVisual(AnimatedContent)", source);
        Assert.Contains("AnimatedContent.ActualWidth", source);
        Assert.Contains("AnimatedContent.ActualHeight", source);
        Assert.Contains("visual.CenterPoint", source);
        Assert.Contains("visual.StartAnimation(nameof(visual.Scale), scale);", source);
        Assert.Contains("AnimationTranslateYPhysical", source);
        Assert.DoesNotContain("ContentSlideDistanceDip", source);
    }

    [Fact]
    public void OverlayQuickSettingsPageBinding_takes_a_narrow_mutation_delegate_not_the_client()
    {
        var constructor = typeof(OverlayQuickSettingsPageBinding).GetConstructors(AnyInstance).Single();
        var mutateParameter = constructor.GetParameters().Single(p => p.Name == "mutate");
        Assert.NotEqual(typeof(NamedPipeOverlayClient), mutateParameter.ParameterType);
        Assert.True(mutateParameter.ParameterType.IsGenericType);
        Assert.Equal(typeof(Func<,>), mutateParameter.ParameterType.GetGenericTypeDefinition());
    }

    // OverlayWindow itself cannot be constructed/exercised here (WinUI needs a XAML host), so this
    // is a source/composition regression -- mirroring QamFrontendContractTests' qam.js text
    // assertions -- proving RenderQuickSettingsPage() actually consumes and clears the binder's local
    // failure fact in BOTH the fast (value-only) path and the structural-rebuild path, per the PR
    // #508 review that flagged a mutation failure being retained in the binder but never surfaced.
    [Fact]
    public void RenderQuickSettingsPage_surfaces_and_clears_the_binder_local_failure_message_on_both_paths()
    {
        var source = ReadOverlayWindowSource();

        Assert.Contains("private static void ApplyQuickSettingsLocalFailure(QuickSettingsSurface surface)", source);
        Assert.Contains("surface.Binding.LastLocalFailureMessage", source);
        // Cleared, not just shown: no message collapses the banner again.
        Assert.Contains("Visibility.Collapsed", source);

        var renderQuickSettingsPage = source[source.IndexOf("private void RenderQuickSettingsPage(QuickSettingsSurface surface)", StringComparison.Ordinal)..
            source.IndexOf("private static QuickSettingsRowShape QuickSettingsRowShapeOf", StringComparison.Ordinal)];
        var fastPathCallCount = CountOccurrences(renderQuickSettingsPage, "ApplyQuickSettingsLocalFailure(surface);");
        Assert.Equal(2, fastPathCallCount); // once after the fast-path update, once after a rebuild
    }

    // PR #510 review: RowShape alone (RowId/ControlKind/SliderKind/WellFormed) cannot distinguish two
    // different Profile games with the same enabled features -- the game identity renders from
    // section Label/Message text (BuildProfile's enriched display name), not row identity. Without
    // also gating the fast path on AppId and section text, a Profile(A)->Profile(B) switch with an
    // identical RowShape would take the value-only fast path and leave A's heading on screen.
    [Fact]
    public void Quick_settings_fast_path_also_requires_the_same_app_id_and_section_text()
    {
        var source = ReadOverlayWindowSource();

        var renderQuickSettingsPage = source[source.IndexOf("private void RenderQuickSettingsPage(QuickSettingsSurface surface)", StringComparison.Ordinal)..
            source.IndexOf("private static void ApplyQuickSettingsLocalFailure", StringComparison.Ordinal)];

        Assert.Contains("surface.RenderedAppId == page.AppId", renderQuickSettingsPage);
        Assert.Contains("surface.RenderedSections is not null && surface.RenderedSections.SequenceEqual(sectionShape)", renderQuickSettingsPage);
        Assert.Contains("QuickSettingsRowShapesEqual(surface.RowShape, rowShape)", renderQuickSettingsPage);
        Assert.DoesNotContain("surface.RowShape.SequenceEqual(rowShape)", renderQuickSettingsPage);

        var rebuildContent = source[source.IndexOf("private void RebuildQuickSettingsContent(QuickSettingsSurface surface", StringComparison.Ordinal)..
            source.IndexOf("private static TextBlock CreateQuickSettingsMessageText", StringComparison.Ordinal)];
        Assert.Contains("surface.RenderedAppId = page.AppId;", rebuildContent);
        Assert.Contains("surface.RenderedSections = QuickSettingsSectionShapeOf(page);", rebuildContent);
    }

    [Fact]
    public void Quick_settings_fast_path_reconciles_selection_after_authoritative_row_state_updates()
    {
        var source = ReadOverlayWindowSource();
        var renderQuickSettingsPage = source[source.IndexOf("private void RenderQuickSettingsPage(QuickSettingsSurface surface)", StringComparison.Ordinal)..
            source.IndexOf("private static QuickSettingsRowShape QuickSettingsRowShapeOf", StringComparison.Ordinal)];

        Assert.Contains("var previousSelection = _rowSelection.SelectedIndex;", renderQuickSettingsPage);
        Assert.Contains("UpdateQuickSettingsRowValues(surface, page);", renderQuickSettingsPage);
        Assert.Contains("if (_tabState.SelectedTab == surface.TabId)", renderQuickSettingsPage);
        Assert.Contains("_rowSelection.SetRows(CapabilitiesFor(surface.TabId), previousSelection);", renderQuickSettingsPage);
        Assert.Contains("ApplyRowSelectionVisual();", renderQuickSettingsPage);
        Assert.Contains("if (_rowSelection.SelectedIndex != previousSelection)", renderQuickSettingsPage);
        Assert.Contains("BringSelectedRowIntoView();", renderQuickSettingsPage);
    }

    [Fact]
    public void Quick_settings_fast_path_identity_includes_metadata_captured_by_row_renderers()
    {
        var source = ReadOverlayWindowSource();
        var shape = source[source.IndexOf("private readonly record struct QuickSettingsRowShape", StringComparison.Ordinal)..
            source.IndexOf("private sealed class QuickSettingsSurface", StringComparison.Ordinal)];
        var shapeFactory = source[source.IndexOf("private static QuickSettingsRowShape QuickSettingsRowShapeOf", StringComparison.Ordinal)..
            source.IndexOf("private static (QuickSettingsSectionId", StringComparison.Ordinal)];

        Assert.Contains("string Label", shape);
        Assert.Contains("string? NumericSuffix", shape);
        Assert.Contains("QuickSettingsDiscreteOption[]? DiscreteOptions", shape);
        Assert.Contains("row.Label", shapeFactory);
        Assert.Contains("spec is { Kind: QuickSettingsSliderKind.Numeric } ? spec.Suffix : null", shapeFactory);
        Assert.Contains("spec is { Kind: QuickSettingsSliderKind.Discrete } ? spec.Options?.ToArray() : null", shapeFactory);
        Assert.Contains("QuickSettingsRowShapesEqual", source);
        Assert.Contains("return left.SequenceEqual(right);", source);
    }

    // SF-V2-09 section 32: Device and Profile share one generic renderer/binder path -- BuildPage's
    // dispatch calls the SAME BuildQuickSettingsPage helper for both tab identities, and the historic
    // Profile placeholder is gone.
    [Fact]
    public void Device_and_profile_tabs_both_route_through_the_same_generic_quick_settings_builder()
    {
        var source = ReadOverlayWindowSource();

        Assert.Contains("AddonQuickSettingsTabId.Device => BuildQuickSettingsPage(id, QuickSettingsPageId.Device)", source);
        Assert.Contains("AddonQuickSettingsTabId.Profile => BuildQuickSettingsPage(id, QuickSettingsPageId.Profile)", source);
        Assert.DoesNotContain("CreatePlaceholderPage(AddonQuickSettingsTabId.Profile)", source);
        Assert.DoesNotContain("CreatePlaceholderPage(id: AddonQuickSettingsTabId.Profile)", source);
    }

    // SF-V2-09 section 32/13.1: exactly one page-local surface type/dictionary backs both pages --
    // no separate Device/Profile field pairs, no per-page renderer class.
    [Fact]
    public void Quick_settings_surface_state_is_one_generic_page_local_type()
    {
        var source = ReadOverlayWindowSource();

        Assert.Contains("private sealed class QuickSettingsSurface", source);
        Assert.Contains("Dictionary<QuickSettingsPageId, QuickSettingsSurface> _quickSettingsSurfaces", source);
        Assert.DoesNotContain("_deviceBinding", source);
        Assert.DoesNotContain("_profileBinding", source);
        Assert.DoesNotContain("_deviceToggleRows", source);
        Assert.DoesNotContain("_deviceSliderRows", source);
    }

    [Fact]
    public void Overlay_value_renderer_uses_one_value_row_for_numeric_and_discrete_shared_slider_contracts()
    {
        var source = ReadOverlayWindowSource();
        var contracts = ReadSource("src", "SteamInputAddonforClaw.Contracts", "Frontend", "QuickSettingsContracts.cs");

        Assert.Contains("Dictionary<QuickSettingsRowId, OverlayValueRow> ValueRows", source);
        Assert.Contains("CreateQuickSettingsValueRow", source);
        Assert.Contains("ApplyQuickSettingsValueState", source);
        Assert.Contains("QuickSettingsSliderKind.Numeric", source);
        Assert.Contains("var options = spec.Options!", source);
        Assert.Contains("FormatDiscreteLabel(options, index)", source);
        Assert.Contains("QuickSettingsValue.Integer(options[i].Value)", source);
        Assert.Contains("new OverlayValueRow", source);
        Assert.Contains("ScheduleQuickSettingsSlider", source);
        Assert.DoesNotContain("OverlaySliderRow", source);
        Assert.DoesNotContain("SliderRows", source);

        Assert.Contains("public enum QuickSettingsControlKind { Toggle, Slider }", contracts);
        Assert.Contains("QuickSettingsSliderKind.Numeric", contracts);
        Assert.Contains("QuickSettingsSliderKind.Discrete", contracts);
    }

    [Fact]
    public void Overlay_value_row_uses_touchable_arrow_buttons_and_the_same_adjustment_seam()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayValueRow.cs");

        Assert.Contains("internal sealed class OverlayValueModel", source);
        Assert.Contains("internal sealed class OverlayValueRow", source);
        Assert.Contains("MinWidth = 40", source);
        Assert.Contains("MinHeight = 40", source);
        Assert.Contains("button.Click += click", source);
        Assert.Contains("_model.RequestAdjust(-1)", source);
        Assert.Contains("_model.RequestAdjust(+1)", source);
        Assert.Contains("Adjust: OnControllerAdjust", source);
        Assert.Contains("Activate: null", source);
        Assert.DoesNotContain("Slider", source);
        Assert.DoesNotContain("ComboBox", source);
        Assert.DoesNotContain("RequestSet", source);
    }

    // SF-V2-09 section 32/6/20/37: no Profile-specific product table/policy may be duplicated into
    // the Overlay renderer/binder -- product definition stays solely in
    // QuickSettingsPresentation.BuildProfile, and commit delay stays solely in the shared
    // QuickSettingsCommitPolicy the row carries.
    [Fact]
    public void Overlay_window_source_carries_no_duplicate_profile_product_or_delay_policy()
    {
        var source = ReadOverlayWindowSource();

        Assert.DoesNotContain("Intel FPS Limit", source);
        Assert.DoesNotContain("Plugged in · PL1", source);
        Assert.DoesNotContain("Efficient Aggressive", source);
        Assert.DoesNotContain("Best performance", source);
        Assert.DoesNotContain("2000", source);
        Assert.DoesNotContain("PROFILE_SLIDER_COMMIT_DELAY_MS", source);
        Assert.DoesNotContain("OverlayProfileDelay", source);
    }

    // SF-V2-09 section 32: row selection for both pages still flows through the one existing
    // OverlayRowSelection model -- no second Profile-specific selection authority.
    [Fact]
    public void Quick_settings_rows_still_use_the_one_shared_row_selection_model()
    {
        var source = ReadOverlayWindowSource();
        Assert.Contains("private readonly OverlayRowSelection _rowSelection = new();", source);
        Assert.DoesNotContain("_profileRowSelection", source);
    }

    [Fact]
    public void Quick_settings_renderer_uses_visibility_in_shape_admission_and_rebuild()
    {
        var source = ReadOverlayWindowSource();

        Assert.Contains("bool Visible,", source);
        Assert.Contains("bool WellFormed,", source);
        Assert.Contains("row.Visible", source);
        Assert.Contains("var visibleRows = section.Rows.Where(row => row.Visible).ToArray();", source);
        Assert.Contains("if (visibleRows.Length == 0) continue;", source);
    }

    [Fact]
    public void Quick_settings_use_single_column_sections_and_pointer_selection_without_mutation_on_empty_row_tap()
    {
        var source = ReadOverlayWindowSource();
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");

        Assert.Contains("var sectionPanel = new StackPanel", quickSettings);
        Assert.Contains("var content = new StackPanel { Spacing = 16 }", quickSettings);
        Assert.Contains("var rowStack = new StackPanel", quickSettings);
        Assert.Contains("sectionPanel.Children.Add(rowStack);", quickSettings);
        Assert.Contains("RegisterRowPointerSelection(overlayRow.Container);", quickSettings);
        Assert.Contains("RegisterRowPointerSelection(row.Container);", source);
        Assert.Contains("_rowSelection.TrySelect(index)", navigation);
        Assert.Contains("container.AddHandler(", navigation);
        Assert.Contains("UIElement.PointerPressedEvent", navigation);
        Assert.Contains("new PointerEventHandler", navigation);
        Assert.Contains("handledEventsToo: true", navigation);
        Assert.DoesNotContain("overlayRow.Container.Tapped +=", quickSettings);
        Assert.DoesNotContain("sectionPanel.ColumnDefinitions", quickSettings);
        Assert.DoesNotContain("rowStack.ColumnDefinitions", quickSettings);
    }

    [Fact]
    public void Ordinary_rows_share_the_left_accent_chrome_and_selected_tabs_use_an_indicator()
    {
        var chrome = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayRowChrome.cs");
        var source = ReadOverlayWindowSource();

        Assert.Contains("SelectionAccentWidth = 3", chrome);
        Assert.Contains("MinHeight = 52", chrome);
        Assert.Contains("new Thickness(SelectionAccentWidth, 0, 0, 0)", chrome);
        Assert.Contains("OverlayRowChrome.Create(grid)", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayToggleRow.cs"));
        Assert.Contains("OverlayRowChrome.Create(grid)", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayValueRow.cs"));
        Assert.Contains("OverlayRowChrome.Create(grid)", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayTabOrderRow.cs"));
        Assert.Contains("_tabIndicators", source);
        Assert.Contains("_tabHosts", source);
        Assert.Contains("Grid.SetColumn(tabHost, position)", source);
        Assert.DoesNotContain("Grid.SetColumn(button, position)", source);
        Assert.Contains("indicator.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed", source);
        Assert.DoesNotContain("button.Background = _tabSelectedBackgroundBrush", source);
        Assert.DoesNotContain("button.Foreground = _tabSelectedForegroundBrush", source);
    }

    [Fact]
    public void OverlayWindow_code_behind_is_only_the_composition_root()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs");

        Assert.DoesNotContain("RenderQuickSettingsPage", source);
        Assert.DoesNotContain("RebuildQuickSettingsContent", source);
        Assert.DoesNotContain("BuildTabOrderEditorPage", source);
        Assert.DoesNotContain("NavigateUp", source);
        Assert.DoesNotContain("AnimateAsync", source);
    }

    [Fact]
    public void OverlayWindow_partial_files_keep_one_owner_and_explicit_responsibilities()
    {
        var presentation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs");
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");

        Assert.Contains("partial class OverlayWindow", presentation);
        Assert.Contains("ShowForPocAsync", presentation);
        Assert.Contains("HideForPocAsync", presentation);
        Assert.Contains("AnimateAsync", presentation);

        Assert.Contains("partial class OverlayWindow", shell);
        Assert.Contains("BuildShell", shell);
        Assert.Contains("ApplyTabOrderState", shell);
        Assert.Contains("ApplySelectedTabVisualState", shell);

        Assert.Contains("partial class OverlayWindow", navigation);
        Assert.Contains("NavigateUp", navigation);
        Assert.Contains("AdjustSelectedRow", navigation);
        Assert.Contains("BringSelectedRowIntoView", navigation);

        Assert.Contains("partial class OverlayWindow", quickSettings);
        Assert.Contains("ConfigureQuickSettings", quickSettings);
        Assert.Contains("RenderQuickSettingsPage", quickSettings);
        Assert.Contains("RebuildQuickSettingsContent", quickSettings);

        var splitSources = string.Join(Environment.NewLine, presentation, shell, navigation, quickSettings);
        Assert.DoesNotContain("class OverlayNavigationManager", splitSources);
        Assert.DoesNotContain("class OverlayPresentationManager", splitSources);
        Assert.DoesNotContain("class OverlayShellManager", splitSources);
        Assert.DoesNotContain("class OverlayQuickSettingsManager", splitSources);
    }

    private static string ReadOverlayWindowSource() => string.Join(
        Environment.NewLine,
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs"));

    private static string ReadWindowInteropSource() => ReadSource("src", "SteamInputAddonforClaw.Overlay", "WindowInterop.cs");

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
