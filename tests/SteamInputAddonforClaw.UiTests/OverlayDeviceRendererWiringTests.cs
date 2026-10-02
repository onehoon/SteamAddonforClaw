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
    public void WindowInterop_uses_AppWindow_visibility_and_fails_closed_on_a_missing_topmost_postcondition()
    {
        var source = ReadWindowInteropSource();

        Assert.Contains("presenter.IsAlwaysOnTop = true;", source);
        Assert.Contains("private const long WsExTopmost = 0x00000008L;", source);
        Assert.Contains("Overlay topmost state verified.", source);
        Assert.Contains("Overlay topmost postcondition failed.", source);
        Assert.DoesNotContain("Overlay topmost style is missing after a successful Show.", source);
        Assert.DoesNotContain("Activate()", source);
        Assert.DoesNotContain("SetForegroundWindow", source);
        Assert.DoesNotContain("SwpShowWindow", source);
        Assert.DoesNotContain("SwpHideWindow", source);
        Assert.Contains("private const uint WsExNoActivate = 0x08000000;", source);
        Assert.Contains("case WmMouseActivate:", source);
        Assert.Contains("return MaNoActivate;", source);

        var configure = source[source.IndexOf("internal static void Configure", StringComparison.Ordinal)..
            source.IndexOf("internal static void ShowWithoutActivation", StringComparison.Ordinal)];
        Assert.Contains("SwpNoZOrder", configure);
        Assert.DoesNotContain("HwndTopmost", configure);

        var show = source[source.IndexOf("internal static void ShowWithoutActivation", StringComparison.Ordinal)..
            source.IndexOf("internal static void Hide", StringComparison.Ordinal)];
        Assert.Contains("appWindow.Show(false);", show);
        Assert.Contains("SwpNoActivate", show);
        Assert.Equal(1, CountOccurrences(show, "SetWindowPos("));
        var visibility = show.IndexOf("appWindow.Show(false);", StringComparison.Ordinal);
        var topmostPromotion = show.IndexOf("SetWindowPos(hwnd, HwndTopmost", StringComparison.Ordinal);
        var verification = show.IndexOf("VerifyTopmostStateAfterShow(hwnd, presenter);", StringComparison.Ordinal);
        Assert.True(visibility >= 0 && visibility < topmostPromotion);
        Assert.True(topmostPromotion < verification);
        Assert.Contains("SwpNoActivate | SwpNoSendChanging | SwpNoSize | SwpNoMove", show[topmostPromotion..]);
        Assert.DoesNotContain("SwpNoZOrder", show);

        var hide = source[source.IndexOf("internal static void Hide", StringComparison.Ordinal)..
            source.IndexOf("private static void VerifyTopmostStateAfterShow", StringComparison.Ordinal)];
        Assert.Contains(".Hide();", hide);
        Assert.DoesNotContain("SetWindowPos", hide);

        var verifier = source[source.IndexOf("private static void VerifyTopmostStateAfterShow", StringComparison.Ordinal)..
            source.IndexOf("private static bool HasTopmostStyle", StringComparison.Ordinal)];
        Assert.Contains("if (!topmostStyle)", verifier);
        Assert.Contains("OverlayLog.Error(\"Window\", \"Overlay topmost postcondition failed.\"", verifier);
        Assert.Contains("throw exception;", verifier);
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
        var surfaceHostStart = xaml.IndexOf("x:Name=\"SurfaceHost\"", StringComparison.Ordinal);
        var opaquePanelStart = xaml.IndexOf("x:Name=\"OpaquePanel\"", StringComparison.Ordinal);
        var animatedContentStart = xaml.IndexOf("<Grid x:Name=\"AnimatedContent\"", opaquePanelStart, StringComparison.Ordinal);
        Assert.True(surfaceHostStart >= 0 && opaquePanelStart > surfaceHostStart && animatedContentStart > opaquePanelStart);
        var surfaceHostDeclaration = xaml[surfaceHostStart..opaquePanelStart];
        var opaquePanelDeclaration = xaml[opaquePanelStart..animatedContentStart];

        Assert.Contains("RequestedTheme=\"Dark\"", xaml);
        Assert.Contains("Background=\"{StaticResource QamSurfaceBrush}\"", surfaceHostDeclaration);
        Assert.Contains("Background=\"{StaticResource QamRailBrush}\"", xaml);
        Assert.DoesNotContain("Background=", opaquePanelDeclaration);
        Assert.DoesNotContain("CornerRadius=", opaquePanelDeclaration);
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
    // is a source/composition regression -- mirroring the retired frontend contract checks
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
            source.IndexOf("private static void ApplyQuickSettingsLocalFailure", StringComparison.Ordinal)];
        Assert.Contains("ReconcileQuickSettingsSections(surface, page);", renderQuickSettingsPage);
        Assert.Contains("RebuildQuickSettingsContent(surface, page);", renderQuickSettingsPage);
        Assert.Contains("ApplyQuickSettingsLocalFailure(surface);", renderQuickSettingsPage);
    }

    // SectionId is the local view identity. A structural update replaces only the changed section;
    // matching sections update in place and page availability transitions retain the full rebuild path.
    [Fact]
    public void Quick_settings_structural_updates_reconcile_only_the_changed_section()
    {
        var source = ReadOverlayWindowSource();

        var surface = source[source.IndexOf("private sealed class QuickSettingsSurface", StringComparison.Ordinal)..
            source.IndexOf("private readonly Dictionary<QuickSettingsPageId", StringComparison.Ordinal)];
        var reconcile = source[source.IndexOf("private void ReconcileQuickSettingsSections", StringComparison.Ordinal)..
            source.IndexOf("private RenderedQuickSettingsSection BuildQuickSettingsSection", StringComparison.Ordinal)];
        var stableStart = reconcile.IndexOf("if (surface.RenderedSections.TryGetValue", StringComparison.Ordinal);
        var stableEnd = reconcile.IndexOf("continue;", stableStart, StringComparison.Ordinal);
        var stableSectionPath = reconcile[stableStart..stableEnd];

        Assert.Contains("Dictionary<QuickSettingsSectionId, RenderedQuickSettingsSection> RenderedSections", surface);
        Assert.Contains("OverlayQuickSettingsSectionRendering.HasSameShape(rendered.Section, section)", stableSectionPath);
        Assert.Contains("rendered.Section = section;", stableSectionPath);
        Assert.Contains("UpdateQuickSettingsRowValues(surface, section);", stableSectionPath);
        Assert.DoesNotContain("RemoveQuickSettingsSection", stableSectionPath);
        Assert.DoesNotContain("BuildQuickSettingsSection", stableSectionPath);
        Assert.Contains("RemoveQuickSettingsSection(surface, rendered);", reconcile);
        Assert.Contains("BuildQuickSettingsSection(surface, section)", reconcile);
        Assert.Contains("ReorderQuickSettingsSectionCards(surface, page);", reconcile);
        Assert.DoesNotContain("surface.Content.Children.Clear()", reconcile);
        Assert.DoesNotContain("surface.ToggleRows.Clear()", reconcile);
        Assert.DoesNotContain("surface.ValueRows.Clear()", reconcile);
    }

    [Fact]
    public void Quick_settings_section_updates_preserve_selected_row_identity_and_scroll_only_when_needed()
    {
        var source = ReadOverlayWindowSource();
        var selection = source[source.IndexOf("private void UpdateQuickSettingsPageRows", StringComparison.Ordinal)..
            source.IndexOf("private static TextBlock CreateQuickSettingsMessageText", StringComparison.Ordinal)];

        Assert.Contains("oldRows[selectedIndex].QuickSettingsRowId", selection);
        Assert.Contains("_pageRows[surface.TabId] = rows;", selection);
        Assert.Contains("_rowSelection.SetRows(CapabilitiesFor(surface.TabId), preferredIndex);", selection);
        Assert.Contains("ApplyRowSelectionVisual();", selection);
        Assert.Contains("selectedIndex != previousSelection || selectedRowId != preferredRowId", selection);
        Assert.Contains("BringSelectedRowIntoView();", selection);
    }

    [Fact]
    public void Quick_settings_section_shape_includes_all_row_renderer_metadata()
    {
        var source = ReadOverlayWindowSource();
        var shape = source[source.IndexOf("private readonly record struct RowShape", StringComparison.Ordinal)..
            source.IndexOf("internal static bool TryGetFeatureHeaderToggle", StringComparison.Ordinal)];
        var comparer = source[source.IndexOf("internal static bool HasSameShape", StringComparison.Ordinal)..
            source.IndexOf("private static bool DiscreteOptionsEqual", StringComparison.Ordinal)];

        Assert.Contains("string Label", shape);
        Assert.Contains("string? NumericSuffix", shape);
        Assert.Contains("QuickSettingsDiscreteOption[]? DiscreteOptions", shape);
        Assert.Contains("previous.Label", comparer);
        Assert.Contains("previous.Message", comparer);
        Assert.Contains("oldShape.Visible != newShape.Visible", comparer);
        Assert.Contains("oldShape.Label", comparer);
        Assert.Contains("oldShape.NumericSuffix", comparer);
        Assert.Contains("DiscreteOptionsEqual", comparer);
    }

    // SF-V2-09 section 32: Device and Profile share one generic renderer/binder path -- BuildPage's
    // dispatch calls the SAME BuildQuickSettingsPage helper for both tab identities, and the historic
    // Profile placeholder is gone.
    [Fact]
    public void Device_and_profile_tabs_both_route_through_the_same_generic_quick_settings_builder()
    {
        var source = ReadOverlayWindowSource();

        Assert.Contains("AddonQuickSettingsTabId.Device => BuildQuickSettingsPage(id, QuickSettingsPageId.Device)", source);
        Assert.Contains("AddonQuickSettingsTabId.Profile => BuildProfilePage()", source);
        Assert.DoesNotContain("CreatePlaceholderPage(AddonQuickSettingsTabId.Profile)", source);
        Assert.DoesNotContain("CreatePlaceholderPage(id: AddonQuickSettingsTabId.Profile)", source);
    }

    [Fact]
    public void Profile_catalog_navigation_keeps_the_selected_card_in_view()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");

        Assert.Contains("private void RefreshProfileCatalogSelectionAfterMove()", source);
        Assert.Contains("_profileCatalogCards[index].StartBringIntoView(", source);
        Assert.Contains("Could not bring the selected Profile card into view.", source);
        Assert.Equal(4, CountOccurrences(source, "RefreshProfileCatalogSelectionAfterMove();"));
    }

    [Fact]
    public void Profile_catalog_titles_are_bounded_to_two_wrapped_lines()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");

        Assert.Contains("var title = new TextBlock", source);
        Assert.Contains("Text = entry.Name", source);
        Assert.Contains("TextWrapping = TextWrapping.Wrap", source);
        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", source);
        Assert.Contains("MaxLines = 2", source);
        Assert.DoesNotContain("Content = entry.Name", source);
    }

    [Fact]
    public void Unavailable_active_publication_refreshes_selected_offline_detail_in_place()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");
        var applyActivePage = source[source.IndexOf("internal void ApplyActiveProfilePage", StringComparison.Ordinal)..];

        Assert.Contains("if (_profileMode == ProfilePresentationMode.SelectedDetail && _selectedCatalogAppId is { } selectedAppId)", applyActivePage);
        Assert.Contains("ProfilePageRequestRequested?.Invoke(selectedAppId);", applyActivePage);
        Assert.Contains("_profileMode = ProfilePresentationMode.ActiveDetail;", applyActivePage);
        Assert.Contains("_profileMode = ProfilePresentationMode.Catalog;", applyActivePage);
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
        Assert.Contains("OverlayValueButtonKind.NumericStepper", source);
        Assert.Contains("var options = spec.Options!", source);
        Assert.Contains("FormatDiscreteLabel(options, index)", source);
        Assert.Contains("QuickSettingsValue.Integer(options[i].Value)", source);
        Assert.Contains("OverlayValueButtonKind.DiscreteChoice", source);
        Assert.Contains("new OverlayValueRow", source);
        Assert.Contains("ScheduleQuickSettingsSlider", source);
        Assert.DoesNotContain("OverlaySliderRow", source);
        Assert.DoesNotContain("SliderRows", source);

        Assert.Contains("public enum QuickSettingsControlKind { Toggle, Slider }", contracts);
        Assert.Contains("QuickSettingsSliderKind.Numeric", contracts);
        Assert.Contains("QuickSettingsSliderKind.Discrete", contracts);
    }

    [Fact]
    public void Overlay_value_row_uses_touchable_icon_buttons_and_the_same_adjustment_seam()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayValueRow.cs");

        Assert.Contains("internal sealed class OverlayValueModel", source);
        Assert.Contains("internal sealed class OverlayValueRow", source);
        Assert.Contains("internal enum OverlayValueButtonKind", source);
        Assert.Contains("NumericStepper", source);
        Assert.Contains("DiscreteChoice", source);
        Assert.Contains("ButtonBase", source);
        Assert.Contains("new RepeatButton", source);
        Assert.Contains(": new Button();", source);
        Assert.Contains("Delay = 300", source);
        Assert.Contains("Interval = 75", source);
        Assert.Contains("FontIcon", source);
        Assert.Contains("CreateIconButton", source);
        Assert.Contains("MinWidth = 40", source);
        Assert.Contains("MinHeight = 40", source);
        Assert.Contains("CornerRadius = new CornerRadius(6)", source);
        Assert.Contains("button.Click += click", source);
        Assert.Contains("_model.RequestAdjust(-1)", source);
        Assert.Contains("_model.RequestAdjust(+1)", source);
        Assert.Contains("Adjust: OnControllerAdjust", source);
        Assert.Contains("Activate: null", source);
        Assert.DoesNotContain("CreateArrowButton", source);
        Assert.DoesNotContain("‹", source);
        Assert.DoesNotContain("›", source);
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
        Assert.Contains("if (visibleRows.Length == 0)", source);
        Assert.Contains("return new RenderedQuickSettingsSection { Section = section, Rows = [] };", source);
    }

    [Fact]
    public void Quick_settings_use_single_column_sections_and_pointer_selection_without_mutation_on_empty_row_tap()
    {
        var source = ReadOverlayWindowSource();
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");

        Assert.Contains("var sectionPanel = new StackPanel", quickSettings);
        Assert.Contains("QamSectionSpacing", quickSettings);
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
    public void Quick_settings_feature_sections_render_their_first_toggle_as_the_header_row()
    {
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");

        Assert.Contains("TryGetFeatureHeaderToggle", quickSettings);
        Assert.Contains("displayLabelOverride ?? row.Label", quickSettings);
        Assert.Contains("visibleRows.Skip(1)", quickSettings);
        Assert.Contains("QamDetailIndent", quickSettings);
        Assert.Contains("strongLabel: true", quickSettings);
        Assert.DoesNotContain("section.Label.Contains", quickSettings);
        Assert.DoesNotContain("section.Label == \"TDP\"", quickSettings);
        Assert.DoesNotContain("section.Label == \"CPU Boost\"", quickSettings);
    }

    [Fact]
    public void Setting_cards_expand_inline_and_render_the_claw_hud_rows()
    {
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var valueRow = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayValueRow.cs");
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var profile = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");

        Assert.Contains("CreateOverlaySectionCard(sectionPanel)", quickSettings);
        Assert.Contains("QamSectionPadding", quickSettings);
        Assert.Contains("QamSectionCornerRadius", quickSettings);
        Assert.Contains("QamSectionBrush", quickSettings);
        Assert.Contains("CreateSettingCard(SettingCardId.ClawHud", clawHud);
        Assert.Contains("CreateSettingCard(SettingCardId.TabOrder", clawHud);
        Assert.Contains("section.Children.Add(row.Container);", clawHud);
        Assert.Contains("_expandedSettingCard == card", clawHud);
        Assert.Contains("ToggleSettingCard", clawHud);
        Assert.Contains("ResetSettingCardsForShow();", shell);
        Assert.Contains("TryHandleSettingBack()", profile);
        Assert.Contains("RegisterRowPointerSelection(overlayRow.Container);", quickSettings);
        Assert.Contains("OverlayRowChrome.Create(grid)", valueRow);
        Assert.DoesNotContain("CreateOverlaySectionCard(BuildShortcutPage())", shell);
        Assert.DoesNotContain("CreateOverlaySectionCard(BuildProfilePage())", shell);
    }

    [Fact]
    public void Feature_header_toggles_can_use_shared_strong_body_typography()
    {
        var toggle = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayToggleRow.cs");
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");

        Assert.Contains("bool strongLabel = false", toggle);
        Assert.Contains("QamBodyStrongTextStyle", toggle);
        Assert.Contains("displayLabelOverride ?? row.Label", quickSettings);
        Assert.Contains("strongLabel)", quickSettings);
    }

    [Fact]
    public void Overlay_shell_uses_a_left_vertical_icon_rail_without_bumper_images()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml");
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");
        var presentation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");
        var project = ReadSource("src", "SteamInputAddonforClaw.Overlay", "SteamInputAddonforClaw.Overlay.csproj");
        var tabRailStart = xaml.IndexOf("x:Name=\"TabRail\"", StringComparison.Ordinal);
        var tabRailEnd = xaml.IndexOf("</Border>", tabRailStart, StringComparison.Ordinal);
        var tabRail = xaml[tabRailStart..tabRailEnd];
        var bodyStart = xaml.IndexOf("<Grid Grid.Column=\"1\"", StringComparison.Ordinal);
        var bodyEnd = xaml.IndexOf("</Grid>", bodyStart, StringComparison.Ordinal);
        var bodyColumn = xaml[bodyStart..bodyEnd];

        Assert.Contains("x:Name=\"SurfaceHost\"", xaml);
        Assert.Contains("x:Name=\"OpaquePanel\"", xaml);
        Assert.Contains("MaxWidth=\"416\"", xaml);
        Assert.Contains("x:Name=\"QamShell\"", xaml);
        Assert.Contains("<ColumnDefinition Width=\"52\" />", xaml);
        Assert.Contains("<ColumnDefinition Width=\"*\" />", xaml);
        Assert.Contains("x:Name=\"TabRail\"", tabRail);
        Assert.Contains("Grid.Column=\"0\"", tabRail);
        Assert.Contains("x:Name=\"TabStrip\"", tabRail);
        Assert.Contains("VerticalAlignment=\"Center\"", tabRail);
        Assert.Contains("RowSpacing=\"{StaticResource QamRailSpacing}\"", tabRail);
        Assert.DoesNotContain("ColumnDefinitions", tabRail);
        Assert.DoesNotContain("Grid.ColumnDefinitions", bodyColumn);
        Assert.Contains("Grid.Column=\"1\" Margin=\"{StaticResource QamContentPadding}\"", bodyColumn);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xaml, "<ScrollViewer\\b"));
        Assert.Contains("x:Name=\"BodyScroll\"", xaml);
        Assert.Contains("x:Name=\"TabBody\"", xaml);
        Assert.DoesNotContain("Steam_LB.png", xaml);
        Assert.DoesNotContain("Steam_RB.png", xaml);
        Assert.DoesNotContain("Assets\\Controller\\Steam_LB.png", project);
        Assert.DoesNotContain("Assets\\Controller\\Steam_RB.png", project);
        Assert.DoesNotContain("PreviousTabHint", shell);
        Assert.DoesNotContain("NextTabHint", shell);
        Assert.Contains("Grid.SetRow(tabHost, position)", shell);
        Assert.DoesNotContain("Grid.SetColumn(tabHost, position)", shell);
        Assert.Contains("TabStrip.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });", shell);
        Assert.Contains("AutomationProperties.SetName(button, label)", shell);
        Assert.Contains("ToolTipService.SetToolTip(button, label)", shell);
        Assert.Contains("Symbol.CellPhone", shell);
        Assert.Contains("Symbol.Contact", shell);
        Assert.Contains("Symbol.XboxOneConsole", shell);
        Assert.Contains("Symbol.ViewAll", shell);
        Assert.Contains("Symbol.Setting", shell);
        Assert.Contains("QamRailButtonStyle", shell);
        Assert.Contains("QamRailIconSize", shell);
        Assert.DoesNotContain("_tabIndicators", shell);
        Assert.Contains("OpaquePanel.Width = Math.Max(0.0, args.NewSize.Width);", presentation);
        Assert.Contains("MinWidth = 0", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayToggleRow.cs"));
        Assert.Contains("OverlayTabState", shell);
        Assert.Contains("QamRailSelectedFillBrush", shell);
        Assert.Contains("SelectPreviousTab", shell);
        Assert.Contains("SelectNextTab", shell);
        Assert.Contains("case OverlayNavigationAction.PreviousTab:", app);
        Assert.Contains("_window?.SelectPreviousTab();", app);
        Assert.Contains("case OverlayNavigationAction.NextTab:", app);
        Assert.Contains("_window?.SelectNextTab();", app);
        Assert.Contains("_tabState.ResetForShow();", shell);
        Assert.Contains("NavigateUp", navigation);
        Assert.Contains("NavigateDown", navigation);
        Assert.Contains("AdjustSelectedRow", navigation);
    }

    [Fact]
    public void Rows_and_tabs_use_the_measured_qam_fill_and_resource_owned_metrics()
    {
        var chrome = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayRowChrome.cs");
        var resources = ReadSource("src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml");
        var source = ReadOverlayWindowSource();

        Assert.Contains("QamRowPadding", chrome);
        Assert.Contains("QamRowMinHeight", chrome);
        Assert.Contains("QamRowCornerRadius", chrome);
        Assert.Contains("QamSelectionBorderThickness", chrome);
        Assert.Contains("QamSelectedFillBrush", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs"));
        Assert.Contains("OverlayRowChrome.Create(grid)", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayToggleRow.cs"));
        Assert.Contains("OverlayRowChrome.Create(grid)", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayValueRow.cs"));
        Assert.Contains("OverlayRowChrome.Create(grid)", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayTabOrderRow.cs"));
        Assert.Contains("_tabHosts", source);
        Assert.Contains("Grid.SetRow(tabHost, position)", source);
        Assert.DoesNotContain("Grid.SetColumn(tabHost, position)", source);
        Assert.DoesNotContain("Grid.SetColumn(button, position)", source);
        Assert.Contains("_tabState.TryApplyOrder(normalized)", source);
        Assert.Contains("QamRailSelectedFillBrush", source);
        Assert.Contains("QamRailIconSelectedBrush", source);
        Assert.Contains("QamRailSelectedFillBrush", resources);
        Assert.DoesNotContain("Width = 3", source);
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
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs"));

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
