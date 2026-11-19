using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
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
    public void WindowInterop_uses_one_native_visibility_and_topmost_authority()
    {
        var source = ReadWindowInteropSource();

        Assert.DoesNotContain("presenter.IsAlwaysOnTop = true;", source);
        Assert.DoesNotContain("appWindow.Show(false);", source);
        Assert.DoesNotContain(".Hide();", source);
        Assert.DoesNotContain("MoveInZOrderAtTop", source);
        Assert.Contains("private const long WsExTopmost = 0x00000008L;", source);
        Assert.Contains("Overlay topmost state verified.", source);
        Assert.Contains("Overlay topmost postcondition failed.", source);
        Assert.DoesNotContain("Overlay topmost style is missing after a successful Show.", source);
        Assert.DoesNotContain("Activate()", source);
        Assert.DoesNotContain("SetForegroundWindow", source);
        Assert.Contains("private const uint SwpShowWindow = 0x0040;", source);
        Assert.Contains("private const uint SwpHideWindow = 0x0080;", source);
        Assert.Contains("private const int SwShowNoActivate = 4;", source);
        Assert.Contains("private const int SwHide = 0;", source);
        Assert.Contains("private const uint WsExNoActivate = 0x08000000;", source);
        Assert.Contains("case WmMouseActivate:", source);
        Assert.Contains("return MaNoActivate;", source);

        var configure = source[source.IndexOf("internal static void Configure", StringComparison.Ordinal)..
            source.IndexOf("internal static bool TryApplyVisibleClawHudTopInset", StringComparison.Ordinal)];
        Assert.Contains("presenter.SetBorderAndTitleBar(false, false);", configure);
        Assert.Contains("presenter.IsResizable = false;", configure);
        Assert.Contains("HwndTopmost", configure);
        var finalPlacement = configure[configure.LastIndexOf("if (!SetWindowPos(", StringComparison.Ordinal)..];
        Assert.Contains("SwpNoActivate", finalPlacement);
        Assert.Contains("SwpNoOwnerZOrder", finalPlacement);
        Assert.Contains("SwpFrameChanged", finalPlacement);
        Assert.DoesNotContain("SwpNoZOrder", finalPlacement);
        Assert.DoesNotContain("SwpNoSendChanging", finalPlacement);

        var show = source[source.IndexOf("internal static void ShowWithoutActivation", StringComparison.Ordinal)..
            source.IndexOf("internal static void Hide", StringComparison.Ordinal)];
        Assert.DoesNotContain("AppWindow", show);
        Assert.Contains("HwndTopmost", show);
        Assert.Contains("SwpShowWindow", show);
        Assert.Contains("SwpNoActivate", show);
        Assert.Contains("SwpNoOwnerZOrder", show);
        Assert.Equal(1, CountOccurrences(show, "SetWindowPos("));
        var topmostPromotion = show.IndexOf("SetWindowPos(", StringComparison.Ordinal);
        var nativeShow = show.IndexOf("ShowWindow(hwnd, SwShowNoActivate);", StringComparison.Ordinal);
        var verification = show.IndexOf("VerifyTopmostStateAfterShow(hwnd);", StringComparison.Ordinal);
        Assert.True(topmostPromotion >= 0 && topmostPromotion < nativeShow);
        Assert.True(nativeShow < verification);
        Assert.Contains("SwpNoMove", show);
        Assert.Contains("SwpNoSize", show);

        var hide = source[source.IndexOf("internal static void Hide", StringComparison.Ordinal)..
            source.IndexOf("private static void VerifyTopmostStateAfterShow", StringComparison.Ordinal)];
        Assert.Contains("ShowWindow(hwnd, SwHide);", hide);
        Assert.Contains("if (IsWindowVisible(hwnd))", hide);
        Assert.Contains("Overlay remained visible after the native Hide operation.", hide);
        Assert.DoesNotContain("SetWindowPos", hide);
        Assert.DoesNotContain("AppWindow", hide);

        var verifier = source[source.IndexOf("private static void VerifyTopmostStateAfterShow", StringComparison.Ordinal)..
            source.IndexOf("private static bool HasTopmostStyle", StringComparison.Ordinal)];
        Assert.Contains("if (!windowVisible || !topmostStyle)", verifier);
        Assert.Contains("IsWindowVisible(hwnd)", verifier);
        Assert.Contains("(\"WindowVisible\", windowVisible)", verifier);
        Assert.Contains("(\"TopmostStyle\", topmostStyle)", verifier);
        Assert.Contains("(\"IsOverlayForeground\", foreground == hwnd)", verifier);
        Assert.DoesNotContain("presenter", verifier.ToLowerInvariant());
        Assert.DoesNotContain("if (foreground == hwnd)", verifier);
        Assert.Contains("OverlayLog.Error(\"Window\", \"Overlay topmost postcondition failed.\"", verifier);
        Assert.Contains("throw exception;", verifier);
    }

    [Fact]
    public void Overlay_geometry_uses_full_monitor_bounds_without_work_area_reservations()
    {
        var windowInterop = ReadWindowInteropSource();
        var configure = windowInterop[windowInterop.IndexOf("internal static void Configure", StringComparison.Ordinal)..
            windowInterop.IndexOf("internal static bool TryApplyVisibleClawHudTopInset", StringComparison.Ordinal)];
        var geometry = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindowGeometry.cs");

        Assert.Contains("MonitorFromWindow(foreground, MonitorDefaultToNearest)", configure);
        Assert.Contains("MonitorFromPoint(new POINT(), MonitorDefaultToPrimary)", configure);
        Assert.Contains("internal RECT rcWork;", windowInterop);
        Assert.DoesNotContain("info.rcWork", configure);
        Assert.DoesNotContain("WorkLeft", configure);
        Assert.DoesNotContain("WorkTop", configure);
        Assert.DoesNotContain("WorkRight", configure);
        Assert.DoesNotContain("WorkBottom", configure);
        Assert.DoesNotContain("WorkWidth", configure);
        Assert.DoesNotContain("WorkHeight", configure);
        Assert.DoesNotContain("ReservedLeftPx", configure);
        Assert.DoesNotContain("ReservedTopPx", configure);
        Assert.DoesNotContain("ReservedRightPx", configure);
        Assert.DoesNotContain("ReservedBottomPx", configure);

        Assert.DoesNotContain("OverlayGeometryMetrics", geometry);
        Assert.DoesNotContain("workLeft", geometry);
        Assert.DoesNotContain("workTop", geometry);
        Assert.DoesNotContain("workRight", geometry);
        Assert.DoesNotContain("workBottom", geometry);
        Assert.DoesNotContain("WorkWidth", geometry);
        Assert.DoesNotContain("WorkHeight", geometry);
        Assert.DoesNotContain("ReservedLeftPx", geometry);
        Assert.DoesNotContain("ReservedTopPx", geometry);
        Assert.DoesNotContain("ReservedRightPx", geometry);
        Assert.DoesNotContain("ReservedBottomPx", geometry);

        var provisionalPlacement = configure.IndexOf("SwpNoActivate | SwpNoSendChanging | SwpNoZOrder", StringComparison.Ordinal);
        var dpiRead = configure.IndexOf("dpi = GetDpiForWindow(hwnd);", StringComparison.Ordinal);
        var geometryCalculation = configure.IndexOf("OverlayWindowGeometry.Calculate(", StringComparison.Ordinal);
        var finalPlacement = configure.LastIndexOf("SetWindowPos(", StringComparison.Ordinal);
        Assert.True(provisionalPlacement >= 0 && provisionalPlacement < dpiRead);
        Assert.True(dpiRead < geometryCalculation && geometryCalculation < finalPlacement);
        Assert.Contains("info.rcMonitor.Left", configure);
        Assert.Contains("info.rcMonitor.Top", configure);
        Assert.Contains("info.rcMonitor.Right", configure);
        Assert.Contains("info.rcMonitor.Bottom", configure);
    }

    [Fact]
    public void WindowInterop_logs_only_native_z_order_visibility_and_extended_style_transitions()
    {
        var source = ReadWindowInteropSource();

        Assert.Contains("private const uint WmWindowPosChanged = 0x0047;", source);
        Assert.Contains("private const uint WmStyleChanged = 0x007D;", source);
        Assert.Contains("case WmWindowPosChanged:", source);
        Assert.Contains("case WmStyleChanged:", source);
        Assert.Contains("Marshal.PtrToStructure<WINDOWPOS>(lParam)", source);
        Assert.Contains("windowPos.HwndInsertAfter", source);
        Assert.Contains("windowPos.Flags", source);
        Assert.Contains("SwpNoZOrder", source);
        Assert.Contains("SwpShowWindow | SwpHideWindow", source);
        Assert.Contains("Overlay window position changed.", source);
        Assert.Contains("Overlay extended style changed.", source);
        Assert.Contains("wParam == GwlExStyle", source);
        Assert.Contains("styleOld", source);
        Assert.Contains("styleNew", source);
        Assert.Contains("oldTopmost", source);
        Assert.Contains("newTopmost", source);
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
    public void OverlayQuickSettingsPageBinding_takes_a_narrow_mutation_delegate_not_the_client()
    {
        var constructor = typeof(OverlayQuickSettingsPageBinding).GetConstructors(AnyInstance).Single();
        var mutateParameter = constructor.GetParameters().Single(p => p.Name == "mutate");
        Assert.NotEqual(typeof(NamedPipeOverlayClient), mutateParameter.ParameterType);
        Assert.True(mutateParameter.ParameterType.IsGenericType);
        Assert.Equal(typeof(Func<,>), mutateParameter.ParameterType.GetGenericTypeDefinition());
    }

    // OverlayWindow itself cannot be constructed/exercised here (WinUI needs a XAML host), so this
    // source/composition regression verifies local failure facts remain in the binding while the
    // compact Overlay intentionally does not render them as extra text.
    [Fact]
    public void RenderQuickSettingsPage_keeps_failure_state_in_the_binding_without_rendering_a_banner()
    {
        var source = ReadOverlayWindowSource();
        var binding = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayQuickSettingsPageBinding.cs");

        Assert.Contains("LastLocalFailureMessage", binding);
        Assert.DoesNotContain("LastLocalFailureMessage", source);
        Assert.DoesNotContain("FailureText", source);
        Assert.DoesNotContain("UnavailableText", source);

        var renderQuickSettingsPage = source[source.IndexOf("private void RenderQuickSettingsPage(", StringComparison.Ordinal)..
            source.IndexOf("private static void UpdateQuickSettingsRowValues", StringComparison.Ordinal)];
        Assert.Contains("ReconcileQuickSettingsSections(surface, page);", renderQuickSettingsPage);
        Assert.Contains("RebuildQuickSettingsContent(surface, page);", renderQuickSettingsPage);
    }

    [Fact]
    public void Device_unavailable_page_uses_prebuilt_loading_status_without_rebuilding_message_elements()
    {
        var source = ReadOverlayWindowSource();
        var build = source[source.IndexOf("private FrameworkElement BuildQuickSettingsPage(", StringComparison.Ordinal)..
            source.IndexOf("private void RenderQuickSettingsPage(", StringComparison.Ordinal)];
        var rebuild = source[source.IndexOf("private void RebuildQuickSettingsContent(", StringComparison.Ordinal)..
            source.IndexOf("private void ReconcileQuickSettingsSections(", StringComparison.Ordinal)];
        var unavailable = rebuild[rebuild.IndexOf("if (!page.Available)", StringComparison.Ordinal)..
            rebuild.IndexOf("else", rebuild.IndexOf("if (!page.Available)", StringComparison.Ordinal), StringComparison.Ordinal)];

        Assert.Contains("Content = content", build);
        Assert.Contains("HomeHost = homeHost", build);
        Assert.Contains("if (pageId == QuickSettingsPageId.Device)", build);
        Assert.Contains("LoadingStatus = loadingStatus", build);
        Assert.Contains("homeHost.Children.Add(loadingStatus)", build);
        Assert.Contains("return homeHost", build);
        Assert.Contains("surface.Content.Children.Clear()", rebuild);
        Assert.Contains("surface.RenderedAvailable = false", unavailable);
        Assert.DoesNotContain("TextBlock", unavailable);
        Assert.DoesNotContain("Children.Add", unavailable);
        Assert.DoesNotContain("page.Message", unavailable);
        Assert.Contains("status.Text = available ? string.Empty : message", source);
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
        Assert.DoesNotContain("ReconcileQuickSettingsSection", stableSectionPath);
        Assert.Contains("ReconcileQuickSettingsSection(surface, rendered, section)", reconcile);
        Assert.DoesNotContain("RemoveQuickSettingsSection(surface, rendered);", reconcile);
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
            source.IndexOf("private static Border CreateOverlaySectionCard", StringComparison.Ordinal)];

        Assert.Contains("oldRows[selectedIndex].QuickSettingsRowId", selection);
        Assert.Contains("_pageRows[surface.TabId] = rows;", selection);
        Assert.Contains("FindPreferredRowIndex(rowIds, preferredRowId)", selection);
        Assert.Contains("var tab = SelectionTabFor(surface);", selection);
        Assert.Contains("_rowSelection.SetRows(CapabilitiesFor(tab), preferredIndex);", selection);
        Assert.Contains("ApplyRowSelectionVisual();", selection);
        Assert.Contains("selectedRowId != preferredRowId", selection);
        Assert.DoesNotContain("selectedIndex != previousSelection", selection);
        Assert.Contains("BringSelectedRowIntoView();", selection);
    }

    [Fact]
    public void Quick_settings_shape_changes_reconcile_rows_inside_the_existing_section_card()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var reconcile = source[source.IndexOf("private RenderedQuickSettingsSection ReconcileQuickSettingsSection(", StringComparison.Ordinal)..
            source.IndexOf("private static void RemoveQuickSettingsSection", StringComparison.Ordinal)];
        var emptySectionPath = reconcile[..reconcile.IndexOf("if (rendered.Card is null", StringComparison.Ordinal)];
        var incrementalPath = reconcile[reconcile.IndexOf("foreach (var rowId in plan.ChangedRowIds)", StringComparison.Ordinal)..];

        Assert.Contains("OverlayQuickSettingsSectionRendering.PlanRows", reconcile);
        Assert.Contains("plan.ChangedRowIds", reconcile);
        Assert.Contains("rendered.RenderedRows.TryGetValue", reconcile);
        Assert.Contains("if (plan.DesiredRows.Count == 0 && !profileHeadingOnly)", emptySectionPath);
        Assert.Contains("if (plan.DesiredRows.Count == 0)", reconcile);
        Assert.Contains("CreateOverlaySectionHeadingRow(section.Label!)", reconcile);
        Assert.Contains("RemoveQuickSettingsSection(surface, rendered);", emptySectionPath);
        Assert.Contains("RemoveRenderedQuickSettingsRow", reconcile);
        Assert.Contains("CreateRenderedQuickSettingsRow", reconcile);
        Assert.Contains("rendered.Card", reconcile);
        Assert.Contains("rendered.RowStack", reconcile);
        Assert.Contains("rendered.DetailStack", reconcile);
        Assert.Contains("PlaceQuickSettingsChild", reconcile);
        Assert.DoesNotContain("CreateOverlaySectionCard", reconcile);
        Assert.DoesNotContain("surface.Content.Children.Clear()", reconcile);
        Assert.DoesNotContain("RemoveQuickSettingsSection", incrementalPath);
    }

    [Fact]
    public void Detail_row_selection_surface_aligns_to_the_section_edge_without_moving_content()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var createRow = ExtractMethod(source, "private RenderedQuickSettingsRow CreateRenderedQuickSettingsRow(");
        var resources = XDocument.Parse(ReadSource(
            "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));

        Assert.Contains("if (!descriptor.IsFeatureHeader)", createRow, StringComparison.Ordinal);
        Assert.Contains("OverlayRowChrome.ApplyDetailPadding(row.Container);", createRow, StringComparison.Ordinal);
        Assert.Contains("row.Container.Margin = new Thickness(margin.Left - indent, margin.Top, margin.Right, margin.Bottom);", createRow, StringComparison.Ordinal);
        Assert.Contains("row.Container.Padding = new Thickness(padding.Left + indent, padding.Top, padding.Right, padding.Bottom);", createRow, StringComparison.Ordinal);

        static double[] ThicknessValues(XDocument document, string key) =>
            document.Descendants().Single(element => (string?)element.Attribute(XName.Get(
                    "Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == key)
                .Value.Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();

        var detailIndent = ThicknessValues(resources, "QamDetailIndent")[0];
        var rowMargin = ThicknessValues(resources, "QamRowMargin");
        var detailPadding = ThicknessValues(resources, "QamDetailRowPadding");
        Assert.Equal(-32, rowMargin[0] - detailIndent);
        Assert.Equal(-16, rowMargin[2]);
        Assert.Equal(32, detailPadding[0] + detailIndent);
        Assert.Equal(5, detailPadding[1]);
        Assert.Equal(5, detailPadding[3]);
        Assert.Equal(0, (rowMargin[0] - detailIndent) + (detailPadding[0] + detailIndent));
    }

    [Fact]
    public void Section_spacing_uses_content_shape_and_is_reused_by_all_overlay_tabs()
    {
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var resources = XDocument.Parse(ReadSource(
            "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var buildSection = ExtractMethod(quickSettings, "private RenderedQuickSettingsSection BuildQuickSettingsSection(");
        var createCard = ExtractMethod(quickSettings, "private static Border CreateOverlaySectionCard(");
        var resolveCardPadding = ExtractMethod(quickSettings,
            "private static Thickness ResolveOverlaySectionCardPadding(");
        var buildController = ExtractMethod(controller, "private FrameworkElement BuildControllerPage(");
        var ledRows = ExtractMethod(controller, "private void BuildControllerLedRows(");
        var settingDetail = ExtractMethod(clawHud, "private FrameworkElement CreateSettingDetailPage(");
        var menuChrome = ExtractMethod(clawHud, "private static (Border Container, Button Button) CreateDetailMenuChrome(");
        var paddingAdjustment = ExtractMethod(quickSettings,
            "internal static (double AdditionalTopPadding, double AdditionalBottomPadding) GetSectionCardPaddingAdjustment(");
        var reconcileSection = ExtractMethod(quickSettings, "private RenderedQuickSettingsSection ReconcileQuickSettingsSection(");

        Assert.Contains("var hasHeaderContent = desiredRows.Any(row => row.IsFeatureHeader) || !string.IsNullOrEmpty(section.Label);", paddingAdjustment);
        Assert.Contains("var hasDetailContent = desiredRows.Any(row => !row.IsFeatureHeader);", paddingAdjustment);
        Assert.Contains("hasHeaderContent ? 2.0 : 7.0", paddingAdjustment);
        Assert.Contains("hasDetailContent || !hasHeaderContent ? 7.0 : 2.0", paddingAdjustment);
        Assert.Contains("GetSectionCardPaddingAdjustment(section, desiredRows)", buildSection);
        Assert.Contains("GetSectionCardPaddingAdjustment(section, plan.DesiredRows)", reconcileSection);
        Assert.Contains("if (rendered.Card.Padding != expectedCardPadding)", reconcileSection);
        Assert.Contains("rendered.Card.Padding = expectedCardPadding;", reconcileSection);
        Assert.DoesNotContain("CreateOverlaySectionCard(", reconcileSection);
        Assert.DoesNotContain("rendered.Card =", reconcileSection);
        Assert.Contains("CreateOverlaySectionCard(sectionPanel, additionalTopPadding, additionalBottomPadding)", buildSection);
        Assert.Contains("ResolveOverlaySectionCardPadding(additionalTopPadding, additionalBottomPadding)", createCard);
        Assert.Contains("sectionPadding.Top + OverlayQamResources.Scale(additionalTopPadding)", resolveCardPadding);
        Assert.Contains("sectionPadding.Bottom + OverlayQamResources.Scale(additionalBottomPadding)", resolveCardPadding);

        static double[] ThicknessValues(XDocument document, string key) =>
            document.Descendants().Single(element => (string?)element.Attribute(XName.Get(
                    "Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == key)
                .Value.Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();

        var rowPadding = ThicknessValues(resources, "QamRowPadding");
        var detailPadding = ThicknessValues(resources, "QamDetailRowPadding");
        Assert.Equal(10, rowPadding[1]);
        Assert.Equal(10, rowPadding[3]);
        Assert.Equal(5, detailPadding[1]);
        Assert.Equal(5, detailPadding[3]);
        Assert.Equal(12, rowPadding[1] + 2.0);
        Assert.Equal(12, rowPadding[3] + 2.0);
        Assert.Equal(12, detailPadding[1] + 7.0);
        Assert.Equal(12, detailPadding[3] + 7.0);

        Assert.Contains("CreateOverlaySectionCard(steamSection, additionalTopPadding: 2.0, additionalBottomPadding: 7.0)", buildController);
        Assert.Contains("CreateOverlaySectionCard(mappingSection, additionalTopPadding: 2.0, additionalBottomPadding: 7.0)", buildController);
        Assert.Contains("additionalTopPadding: 2.0", buildController);
        Assert.Contains("additionalBottomPadding: 7.0", buildController);
        Assert.Contains("CreateOverlaySectionCard(details, additionalTopPadding: 7.0, additionalBottomPadding: 7.0)", ledRows);
        Assert.Contains("CreateOverlaySectionCard(content, additionalTopPadding: 7.0, additionalBottomPadding: 7.0)", settingDetail);
        Assert.Contains("CreateOverlaySectionCard(header, additionalTopPadding: 2.0, additionalBottomPadding: 2.0)", menuChrome);
        Assert.Contains("_gyroscopeRow.Container.Visibility = _controllerMotionSupported ? Visibility.Visible : Visibility.Collapsed;", controller);

        var shortcutRefresh = ExtractMethod(quickSettings, "private void RefreshShortcutQuickSettingsView(QuickSettingsRowId? preferredShortcutRowId)");
        Assert.Contains("desiredSource.HomeHost.Children.Remove(desiredSource.Content);", shortcutRefresh);
        Assert.Contains("_shortcutQuickSettingsHost.Children.Add(desiredSource.Content);", shortcutRefresh);
        Assert.DoesNotContain("BuildQuickSettingsSection", shortcutRefresh);
    }

    [Fact]
    public void Value_only_quick_settings_updates_skip_page_reconciliation_selection_and_scroll_work()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var render = source[source.IndexOf("private void RenderQuickSettingsPage(", StringComparison.Ordinal)..
            source.IndexOf("private static void UpdateQuickSettingsRowValues", StringComparison.Ordinal)];
        var fastPathStart = render.IndexOf("if (surface.RenderedPage is", StringComparison.Ordinal);
        var fastPathEnd = render.IndexOf("if (!page.Available)", fastPathStart, StringComparison.Ordinal);
        Assert.True(fastPathStart >= 0 && fastPathEnd > fastPathStart);
        var fastPath = render[fastPathStart..fastPathEnd];

        Assert.Contains("QuickSettingsRowRendering.IsValueOnlyPageUpdate", fastPath);
        Assert.Contains("FindValueRowsToApply(previousPage, page, forceRowId)", fastPath);
        Assert.Contains("forceRowId", fastPath);
        Assert.Contains("ApplyQuickSettingsRowValues", fastPath);
        Assert.DoesNotContain("ReconcileQuickSettingsSections", fastPath);
        Assert.DoesNotContain("RebuildQuickSettingsContent", fastPath);
        Assert.DoesNotContain("RestoreQuickSettingsSelection", fastPath);
        Assert.DoesNotContain("ApplyRowSelectionVisual", fastPath);
        Assert.DoesNotContain("BringSelectedRowIntoView", fastPath);
        Assert.DoesNotContain("_numericSliderRepeatTarget.Clear()", fastPath);
        Assert.Contains("ReconcileQuickSettingsSections(surface, page);", render);
        Assert.Contains("RebuildQuickSettingsContent(surface, page);", render);
    }

    [Fact]
    public void Quick_settings_value_diff_is_scoped_to_changed_row_ids_and_can_restore_a_rejected_preview()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var update = source[source.IndexOf("private static void ApplyQuickSettingsRowValues", StringComparison.Ordinal)..
            source.IndexOf("private static void ApplyQuickSettingsToggleState", StringComparison.Ordinal)];
        var scheduleStart = source.IndexOf("private void ScheduleQuickSettingsSlider", StringComparison.Ordinal);
        var schedule = source[scheduleStart..
            source.IndexOf("private static string FormatDiscreteLabel", scheduleStart, StringComparison.Ordinal)];

        Assert.Contains("rowIds.Contains(row.RowId)", update);
        Assert.Contains("ScheduleSlider(rowId, desired, out var changedRowIds)", schedule);
        Assert.Contains("changedRowIds.Append(rowId).ToHashSet()", schedule);
        Assert.Contains("ApplyQuickSettingsRowValues(surface, page, changedRowIds)", schedule);
        var targetedUpdate = schedule[schedule.IndexOf("changedRowIds = changedRowIds.Append", StringComparison.Ordinal)..];
        Assert.DoesNotContain("RenderQuickSettingsPage", targetedUpdate);
        Assert.DoesNotContain("ReconcileQuickSettingsSections", schedule);
    }

    [Fact]
    public void Immediate_native_toggle_forces_its_row_through_value_only_readback()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var renderStart = source.IndexOf("private void RenderQuickSettingsPage(", StringComparison.Ordinal);
        var render = source[renderStart..
            source.IndexOf("private static void UpdateQuickSettingsRowValues", renderStart, StringComparison.Ordinal)];
        var submitStart = source.IndexOf("private async Task SubmitQuickSettingsToggleAsync", StringComparison.Ordinal);
        var submit = source[submitStart..
            source.IndexOf("private void ScheduleQuickSettingsSlider", submitStart, StringComparison.Ordinal)];

        Assert.Contains("QuickSettingsRowId? forceRowId = null", render);
        Assert.Contains("FindValueRowsToApply(previousPage, page, forceRowId)", render);
        Assert.Contains("RenderQuickSettingsPage(surface, forceRowId: rowId)", submit);
        Assert.Contains("ReconcileQuickSettingsSections(surface, page);", render);
        Assert.Contains("RebuildQuickSettingsContent(surface, page);", render);
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
        Assert.Contains("QuickSettingsCommitPolicy CommitPolicy", shape);
        Assert.Contains("previous.Label", comparer);
        Assert.DoesNotContain("previous.Message", comparer);
        Assert.Contains("oldShape.Visible != newShape.Visible", comparer);
        Assert.Contains("oldShape.CommitPolicy != newShape.CommitPolicy", comparer);
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
    public void Overlay_profile_has_no_catalog_offline_selection_or_profile_request_wire()
    {
        var profile = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");
        var wire = ReadSource("src", "SteamInputAddonforClaw.FrontendTransport", "OverlayWire.cs");
        var client = ReadSource("src", "SteamInputAddonforClaw.FrontendTransport", "NamedPipeOverlayClient.cs");
        var server = ReadSource("src", "SteamInputAddonforClaw.FrontendTransport", "NamedPipeOverlayServer.cs");
        var retired = "ProfileCatalogRequest ProfileCatalogState ProfilePageRequest ProfilePageResult " +
                      "OverlayProfileCatalogState OverlayProfilePageRequest OverlayProfilePageResponse " +
                      "_selectedCatalogAppId ProfilePresentationMode.Catalog ProfilePresentationMode.SelectedDetail " +
                      "ProfileCatalogRequestRequested ProfilePageRequestRequested";
        var productSources = string.Join("\n", profile, app, wire, client, server);

        foreach (var symbol in retired.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            Assert.DoesNotContain(symbol, productSources, StringComparison.Ordinal);
        Assert.DoesNotContain("_profileCatalog", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedDetail", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_show_uses_the_configured_first_tab_and_preserves_profile_empty_state()
    {
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var presentation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs");
        var profile = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");
        var reset = ExtractMethod(shell, "private void ResetUiForShow()");
        var show = ExtractMethod(presentation, "internal async Task ShowForPocAsync()");
        var dispatch = ExtractMethod(app, "private Task HandleCommandAsync(OverlayCommand command)");

        Assert.Contains("PrepareActiveProfileFirstShow();", reset);
        Assert.Contains("_tabState.ResetForShow();", reset);
        Assert.DoesNotContain("_tabState.Select(AddonQuickSettingsTabId.Profile)", reset);
        Assert.Contains("case OverlayCommand.Show:", dispatch);
        Assert.Contains("case OverlayCommand.ShowActiveProfile:", dispatch);
        Assert.Contains("await _window.ShowForPocAsync();", dispatch);
        Assert.Equal(1, CountOccurrences(dispatch, "await _window.ShowForPocAsync();"));
        Assert.DoesNotContain("preferActiveProfile", reset + show + dispatch);
        Assert.True(show.IndexOf("ResetUiForShow();", StringComparison.Ordinal)
            < show.IndexOf("WindowInterop.ShowWithoutActivation(this);", StringComparison.Ordinal));
        Assert.Contains("var activeProfileReady = HasRenderableActiveProfile(page);", profile);
        Assert.Contains("_profileStatusMessage.Text = ResolveProfileStatusMessage(page);", profile);
        Assert.Contains("_profileStatusMessage.Visibility = Visibility.Collapsed", profile);
        Assert.Contains("_profileDetailRoot.Visibility = Visibility.Collapsed", profile);

        const string noGameStatus = "runtime no-game status";
        Assert.Equal(noGameStatus, OverlayWindow.ResolveProfileStatusMessage(
            QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, null, noGameStatus)));
    }

    [Fact]
    public void Profile_initial_load_preserves_authority_and_hides_old_controls_until_a_fresh_page_arrives()
    {
        var profile = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");
        var prepare = ExtractMethod(profile, "private void PrepareActiveProfileFirstShow()");
        var presentation = ExtractMethod(profile, "private void ApplyProfilePresentation(QuickSettingsPageSnapshot? page)");
        var apply = ExtractMethod(profile, "internal string ApplyActiveProfilePage(QuickSettingsPageSnapshot page)");

        Assert.Contains("private bool _activeProfileInitialLoadPending;", profile);
        Assert.Contains("_activeProfileInitialLoadPending = true;", prepare);
        Assert.DoesNotContain("surface.Binding?.ApplyAuthoritativePage(page);", prepare);
        Assert.DoesNotContain("RenderQuickSettingsPage(surface);", prepare);
        Assert.Contains("surface.Content.Visibility = Visibility.Collapsed;", prepare);
        Assert.Contains("HideProfileArtworkPreservingCache();", prepare);
        Assert.Contains("ApplyProfilePresentation(page);", prepare);

        var pendingIndex = presentation.IndexOf("if (_activeProfileInitialLoadPending)", StringComparison.Ordinal);
        var readyIndex = presentation.IndexOf("var activeProfileReady = HasRenderableActiveProfile(page);", StringComparison.Ordinal);
        Assert.True(pendingIndex >= 0 && pendingIndex < readyIndex);
        Assert.Contains("_profileDetailRoot.Visibility = Visibility.Collapsed;", presentation);
        Assert.Contains("_profileStatusMessage.Text = \"Loading the active game profile.\";", presentation);
        Assert.Contains("_profileStatusMessage.Visibility = Visibility.Visible;", presentation[..readyIndex]);
        Assert.Contains("return;", presentation[..readyIndex]);

        var clearPendingIndex = apply.IndexOf("_activeProfileInitialLoadPending = false;", StringComparison.Ordinal);
        var applyPageIndex = apply.IndexOf("surface.Binding?.ApplyAuthoritativePage(page);", StringComparison.Ordinal);
        var renderIndex = apply.IndexOf("RenderQuickSettingsPage(surface);", StringComparison.Ordinal);
        Assert.True(clearPendingIndex >= 0 && clearPendingIndex < applyPageIndex && applyPageIndex < renderIndex);
        Assert.Contains("if (page.PageId != QuickSettingsPageId.Profile) return \"Ignored\";", apply);
    }

    [Fact]
    public void Profile_status_distinguishes_no_game_from_an_unavailable_active_xbox_profile()
    {
        const string noGameMessage = "runtime no-game status";
        const string xboxFailureMessage = "runtime active-profile failure";
        var noGamePage = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, null, noGameMessage);
        var unrelatedNoTargetFailure = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, null, xboxFailureMessage);
        var unavailableXboxPage = QuickSettingsPageSnapshot.Unavailable(
            QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForXbox("xbox:active"), xboxFailureMessage);

        Assert.False(OverlayWindow.HasRenderableActiveProfile(noGamePage));
        Assert.Equal(noGameMessage, OverlayWindow.ResolveProfileStatusMessage(noGamePage));
        Assert.False(OverlayWindow.HasRenderableActiveProfile(unavailableXboxPage));
        Assert.Equal(xboxFailureMessage, OverlayWindow.ResolveProfileStatusMessage(unavailableXboxPage));
        Assert.Equal(xboxFailureMessage, OverlayWindow.ResolveProfileStatusMessage(unrelatedNoTargetFailure));
        Assert.NotEqual(noGameMessage, OverlayWindow.ResolveProfileStatusMessage(unavailableXboxPage));
        Assert.False(string.IsNullOrWhiteSpace(OverlayWindow.ResolveProfileStatusMessage(null)));

        var presentation = ExtractMethod(ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs"),
            "private void ApplyProfilePresentation(QuickSettingsPageSnapshot? page)");
        var readyIndex = presentation.IndexOf("if (activeProfileReady)", StringComparison.Ordinal);
        var readyReturnIndex = presentation.IndexOf("return;", readyIndex, StringComparison.Ordinal);
        var unavailableIndex = presentation.IndexOf("_profileDetailRoot.Visibility = Visibility.Collapsed;", readyReturnIndex, StringComparison.Ordinal);
        Assert.True(readyIndex >= 0 && readyIndex < readyReturnIndex && readyReturnIndex < unavailableIndex);
        Assert.Contains("_profileStatusMessage.Visibility = Visibility.Collapsed;", presentation[readyIndex..readyReturnIndex]);
        Assert.Contains("_profileDetailRoot.Visibility = Visibility.Visible;", presentation[readyIndex..readyReturnIndex]);
        Assert.Contains("_profileStatusMessage.Text = ResolveProfileStatusMessage(page);", presentation[unavailableIndex..]);
        Assert.Contains("_profileStatusMessage.Visibility = Visibility.Visible;", presentation[unavailableIndex..]);
    }

    [Fact]
    public void Conditional_profile_recheck_only_accepts_the_exact_no_game_page_once_per_selected_entry()
    {
        var noGameMessage = ReadNoGameProfileMessage();
        var noGame = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, null, noGameMessage);
        var loading = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, null, "Loading the active game profile.");
        var activeSteam = new QuickSettingsPageSnapshot(
            QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(42), true, null, [], []);
        var activeXbox = new QuickSettingsPageSnapshot(
            QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForXbox("xbox:active"), true, null, [], []);

        Assert.True(OverlayWindow.IsExactNoRunningGamePage(noGame));
        Assert.True(OverlayWindow.ShouldRequestNoRunningGameRecheck(noGame, true, false, false));
        Assert.False(OverlayWindow.ShouldRequestNoRunningGameRecheck(activeSteam, true, false, false));
        Assert.False(OverlayWindow.ShouldRequestNoRunningGameRecheck(activeXbox, true, false, false));
        Assert.False(OverlayWindow.ShouldRequestNoRunningGameRecheck(noGame, false, false, false));
        Assert.False(OverlayWindow.ShouldRequestNoRunningGameRecheck(noGame, true, true, false));
        Assert.False(OverlayWindow.ShouldRequestNoRunningGameRecheck(noGame, true, false, true));
        Assert.False(OverlayWindow.IsExactNoRunningGamePage(loading));
        Assert.False(OverlayWindow.ShouldRequestNoRunningGameRecheck(loading, true, false, false));
        Assert.False(OverlayWindow.IsExactNoRunningGamePage(
            QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, null, "Profile capture failed.")));
        Assert.False(OverlayWindow.IsExactNoRunningGamePage(
            QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForXbox("xbox:active"), noGameMessage)));
        Assert.False(OverlayWindow.IsExactNoRunningGamePage(
            QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Device, null, noGameMessage)));

        var profile = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");
        var client = ReadSource("src", "SteamInputAddonforClaw.FrontendTransport", "NamedPipeOverlayClient.cs");
        var entry = ExtractMethod(profile, "private void OnProfileTabSelectionChanged(bool selected)");
        var prepare = ExtractMethod(profile, "private void PrepareActiveProfileFirstShow()");
        var apply = ExtractMethod(profile, "internal string ApplyActiveProfilePage(QuickSettingsPageSnapshot page)");
        var showReset = ExtractMethod(shell, "private void ResetUiForShow()");

        Assert.Contains("_noGameRecheckRequestedForThisEntry = false;", entry);
        Assert.Contains("TryRequestNoRunningGameRecheck(surface.Binding?.AuthoritativePage);", entry);
        Assert.Contains("_noGameRecheckRequestedForThisEntry = false;", prepare);
        Assert.Contains("TryRequestNoRunningGameRecheck(page);", apply);
        Assert.Contains("PrepareActiveProfileFirstShow();", showReset);
        Assert.DoesNotContain("if (preferActiveProfile) PrepareActiveProfileFirstShow();", showReset);
        Assert.Contains("_window.NoRunningGameProfileRecheckRequested += OnNoRunningGameProfileRecheckRequested;", app);
        Assert.Contains("_client.SendNoRunningGameProfileRecheckRequestAsync()", app);
        Assert.Contains("OverlayWireMessageKind.NoRunningGameProfileRecheckRequest", client);
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
    public void Overlay_value_renderer_routes_debounced_numeric_to_native_slider_and_discrete_to_value_row()
    {
        var source = ReadOverlayWindowSource();
        var contracts = ReadSource("src", "SteamInputAddonforClaw.Contracts", "Frontend", "QuickSettingsContracts.cs");

        Assert.Contains("Dictionary<QuickSettingsRowId, OverlayNumericSliderRow> NumericSliderRows", source);
        Assert.Contains("Dictionary<QuickSettingsRowId, OverlayValueRow> ValueRows", source);
        Assert.Contains("CreateQuickSettingsValueRow", source);
        Assert.Contains("ApplyQuickSettingsValueState", source);
        Assert.Contains("QuickSettingsSliderKind.Numeric", source);
        Assert.Contains("OverlayNumericSliderRow", source);
        Assert.Contains("QuickSettingsCommitMode.TrailingDebounce", source);
        Assert.Contains("ApplyQuickSettingsNumericSliderState(sliderRow, row)", source);
        Assert.Contains("var options = spec.Options!", source);
        Assert.Contains("FormatDiscreteLabel(options, index)", source);
        Assert.Contains("QuickSettingsValue.Integer(options[i].Value)", source);
        Assert.Contains("OverlayValueButtonKind.DiscreteChoice", source);
        Assert.Contains("new OverlayValueRow", source);
        Assert.Contains("ScheduleQuickSettingsSlider", source);
        Assert.DoesNotContain("OverlaySliderRow", source);
        Assert.Contains("ApplyQuickSettingsNumericSliderState", source);

        Assert.Contains("public enum QuickSettingsControlKind { Toggle, Slider }", contracts);
        Assert.Contains("QuickSettingsSliderKind.Numeric", contracts);
        Assert.Contains("QuickSettingsSliderKind.Discrete", contracts);
    }

    [Fact]
    public void Stable_numeric_quick_settings_updates_reuse_the_existing_slider_row()
    {
        var source = ReadOverlayWindowSource();
        var update = source[source.IndexOf("private static void UpdateQuickSettingsRowValues", StringComparison.Ordinal)..
            source.IndexOf("private static void ApplyQuickSettingsToggleState", StringComparison.Ordinal)];

        Assert.Contains("surface.NumericSliderRows.TryGetValue(row.RowId, out var numericSlider)", update);
        Assert.Contains("ApplyQuickSettingsNumericSliderState(numericSlider, row)", update);
        Assert.DoesNotContain("new OverlayNumericSliderRow", update);
    }

    [Fact]
    public void Numeric_slider_native_properties_are_applied_only_when_the_value_changes()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayNumericSliderRow.cs");
        var render = source[source.IndexOf("private void Render()", StringComparison.Ordinal)..
            source.IndexOf("private static void ApplyQamSliderResources", StringComparison.Ordinal)];

        Assert.Contains("if (_valueText.Text != valueText)", render);
        Assert.Contains("if (_slider.IsEnabled != _model.IsAvailable)", render);
        Assert.Contains("if (_slider.Minimum != expandedMinimum)", render);
        Assert.Contains("if (_slider.Maximum != expandedMaximum)", render);
        Assert.Contains("if (_slider.Minimum != _model.Minimum)", render);
        Assert.Contains("if (_slider.Maximum != _model.Maximum)", render);
        Assert.Contains("if (_slider.StepFrequency != _model.Step)", render);
        Assert.Contains("if (_slider.Value != _model.PreviewValue)", render);
        Assert.True(render.IndexOf("_slider.Minimum = expandedMinimum", StringComparison.Ordinal)
            < render.IndexOf("_slider.Maximum = expandedMaximum", StringComparison.Ordinal));
        Assert.True(render.IndexOf("_slider.Maximum = expandedMaximum", StringComparison.Ordinal)
            < render.IndexOf("_slider.Minimum = _model.Minimum", StringComparison.Ordinal));
    }

    [Fact]
    public void Value_row_native_text_and_button_states_are_applied_only_when_changed()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayValueRow.cs");
        var render = source[source.IndexOf("private void Render()", StringComparison.Ordinal)..
            source.IndexOf("internal static string FormatInteger", StringComparison.Ordinal)];

        Assert.Contains("if (_valueText.Text != valueText)", render);
        Assert.Contains("if (_previousButton.IsEnabled != canDecrease)", render);
        Assert.Contains("if (_nextButton.IsEnabled != canIncrease)", render);
    }

    [Fact]
    public void Overlay_value_row_preserves_one_step_adjustment_and_numeric_repeat_behavior()
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
        Assert.Contains("button.Click += click", source);
        Assert.Contains("_model.RequestAdjust(-1)", source);
        Assert.Contains("_model.RequestAdjust(+1)", source);
        Assert.Contains("Adjust: OnControllerAdjust", source);
        Assert.Contains("Activate: null", source);
        var rowStart = source.IndexOf("internal sealed class OverlayValueRow", StringComparison.Ordinal);
        var valueRowSource = source[rowStart..];
        Assert.DoesNotContain("Slider", valueRowSource);
        Assert.DoesNotContain("ComboBox", valueRowSource);
        Assert.DoesNotContain("RequestSet", valueRowSource);
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
        Assert.DoesNotContain("AC · PL1", source);
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
    public void Quick_settings_renderer_uses_visibility_in_row_reconciliation()
    {
        var source = ReadOverlayWindowSource();
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");

        Assert.Contains("bool Visible,", source);
        Assert.Contains("bool WellFormed,", source);
        Assert.Contains("row.Visible", source);
        Assert.Contains(".Where(row => row.Visible && QuickSettingsRowRendering.IsWellFormed(row))", quickSettings);
        Assert.Contains("if (desiredRows.Count == 0 && !profileHeadingOnly)", quickSettings);
        Assert.Contains("section.SectionId == QuickSettingsSectionId.ProfileGeneral", quickSettings);
        Assert.Contains("return new RenderedQuickSettingsSection { Section = section, Rows = [] };", source);
    }

    [Fact]
    public void Quick_settings_pointer_tap_selects_rows_without_mutating_on_empty_space()
    {
        var source = ReadOverlayWindowSource();
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");

        Assert.Contains("RegisterRowPointerSelection(row.Container);", quickSettings);
        Assert.Contains("RegisterRowPointerSelection(row.Container);", source);
        Assert.Contains("_rowSelection.TrySelect(index)", navigation);
        Assert.Contains("container.AddHandler(", navigation);
        Assert.Contains("UIElement.PointerPressedEvent", navigation);
        Assert.Contains("new PointerEventHandler", navigation);
        Assert.Contains("handledEventsToo: true", navigation);
        Assert.DoesNotContain("overlayRow.Container.Tapped +=", quickSettings);
    }

    [Fact]
    public void Quick_settings_feature_sections_render_their_first_toggle_as_the_header_row()
    {
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");

        Assert.Contains("TryGetFeatureHeaderToggle", quickSettings);
        Assert.Contains("displayLabelOverride ?? row.Label", quickSettings);
        Assert.Contains("descriptor.IsFeatureHeader", quickSettings);
        Assert.Contains("descriptor.DisplayLabel", quickSettings);
        Assert.Contains("if (!descriptor.IsFeatureHeader)", quickSettings);
        Assert.DoesNotContain("section.Label.Contains", quickSettings);
        Assert.DoesNotContain("section.Label == \"TDP\"", quickSettings);
        Assert.DoesNotContain("section.Label == \"CPU Boost\"", quickSettings);
    }

    [Fact]
    public void Profile_title_stays_data_driven_without_a_visible_master_toggle()
    {
        var presentation = ReadSource("src", "SteamInputAddonforClaw", "Frontend", "QuickSettingsPresentation.cs");
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var buildProfile = ExtractMethod(presentation,
            "private static QuickSettingsPageSnapshot BuildProfile(ProfileQuickSettingsProjection snapshot)");
        var buildGeneral = ExtractMethod(presentation,
            "private static QuickSettingsSection BuildProfileGeneralSection(ProfileQuickSettingsProjection snapshot)");

        Assert.Contains("QuickSettingsRowId.ProfileEnabled", buildGeneral);
        Assert.Contains("Writable: false", buildGeneral);
        Assert.Contains("Visible = false", buildGeneral);
        Assert.Contains("QuickSettingsSectionId.ProfileGeneral, snapshot.DisplayName, [row]", buildGeneral);
        Assert.Contains("CreateOverlaySectionHeadingRow(section.Label)", quickSettings);
        Assert.DoesNotContain("new OverlayToggleRow", presentation);
    }



    [Fact]
    public void Setting_root_and_detail_pages_reuse_the_existing_settings_controls()
    {
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var valueRow = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayValueRow.cs");
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var profile = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");

        Assert.Contains("CreateOverlaySectionCard(sectionPanel, additionalTopPadding, additionalBottomPadding)", quickSettings);
        var settingPageStart = clawHud.IndexOf("private FrameworkElement BuildSettingPage", StringComparison.Ordinal);
        var settingPageEnd = clawHud.IndexOf("private FrameworkElement CreateSettingDetailPage", settingPageStart, StringComparison.Ordinal);
        var settingPage = clawHud[settingPageStart..settingPageEnd];
        Assert.Equal(1, CountOccurrences(settingPage, "BuildClawHudPage()"));
        Assert.Equal(1, CountOccurrences(settingPage, "BuildQuickSettingsPage()"));
        Assert.Equal(1, CountOccurrences(settingPage, "BuildTabOrderEditorPage()"));
        Assert.Equal(3, CountOccurrences(settingPage, "_settingRootRows.Add(CreateSettingRootRow("));
        Assert.Contains("rows.AddRange(_settingRootRows);", settingPage);
        Assert.DoesNotContain("rows.AddRange(_clawHudRows)", settingPage);
        Assert.DoesNotContain("rows.AddRange(_quickSettingsRows)", settingPage);
        Assert.Contains("host.Children.Add(clawHudDetailView);", settingPage);
        Assert.Contains("host.Children.Add(quickSettingsDetailView);", settingPage);
        Assert.Contains("host.Children.Add(tabOrderDetailView);", settingPage);
        Assert.DoesNotContain("CreateOverlayDetailStack", clawHud);
        Assert.DoesNotContain("SettingCardId", clawHud);
        Assert.Contains("ApplyQuickSettingsCurrentPowerSourceOnly(settings.QuickSettingsCurrentPowerSourceOnly, settingsAvailable)",
            ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs"));
        Assert.Contains("section.Children.Add(row.Container);", clawHud);
        Assert.Contains("IsSettingDetailActive(detail)", clawHud);
        Assert.Contains("ResetDetailForTabChangeOrShow();", shell);
        Assert.Contains("TryReturnFromDetailPage()", profile);
        Assert.Contains("RegisterRowPointerSelection(row.Container);", quickSettings);
        Assert.Contains("OverlayRowChrome.Create(grid)", valueRow);
        Assert.DoesNotContain("CreateOverlaySectionCard(BuildShortcutPage())", shell);
        Assert.DoesNotContain("CreateOverlaySectionCard(BuildProfilePage())", shell);
    }

    [Fact]
    public void Setting_detail_navigation_binds_only_visible_rows_and_restores_root_selection_and_scroll()
    {
        var detail = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.DetailNavigation.cs");
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");
        var presentation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs");

        var enter = ExtractMethod(detail, "private bool EnterDetailPage(");
        Assert.Contains("BodyScroll.VerticalOffset", enter);
        Assert.Contains("_activeDetailPage = new ActiveDetailPage", enter);
        Assert.Contains("rootView.Visibility = Visibility.Collapsed", enter);
        Assert.Contains("detailView.Visibility = Visibility.Visible", enter);
        Assert.Contains("_pageRows[tab] = detailRows;", enter);
        Assert.Contains("_rowSelection.SetRows(CapabilitiesFor(tab));", enter);
        Assert.Contains("ScrollActivePageToTop();", enter);
        Assert.DoesNotContain("BuildClawHudPage()", enter);
        Assert.DoesNotContain("Focus()", detail);

        var back = ExtractMethod(detail, "private bool TryReturnFromDetailPage(");
        Assert.Contains("FindRowIndex(detail.RootRows, detail.OriginContainer)", back);
        Assert.Contains("_pageRows[detail.Tab] = detail.RootRows;", back);
        Assert.Contains("PageTitle.Visibility = Visibility.Visible;", back);
        Assert.Contains("_rowSelection.SetRows(CapabilitiesFor(detail.Tab), originIndex);", back);
        Assert.Contains("ApplyRowSelectionVisual();", back);
        Assert.Contains("RestoreBodyScroll(detail.RootScrollOffset);", back);
        Assert.DoesNotContain("SendDismiss", back);
        Assert.Contains("BodyScroll.UpdateLayout();", detail);

        var reset = ExtractMethod(shell, "private void ResetUiForShow()");
        Assert.True(reset.IndexOf("ResetDetailForTabChangeOrShow();", StringComparison.Ordinal)
            < reset.IndexOf("_tabState.ResetForShow();", StringComparison.Ordinal));
        Assert.Contains("ResetUiForShow();", presentation);
        Assert.True(presentation.IndexOf("ResetUiForShow();", StringComparison.Ordinal)
            < presentation.IndexOf("WindowInterop.ShowWithoutActivation(this)", StringComparison.Ordinal));

        Assert.Contains("if (!_pageRows.TryGetValue(_tabState.SelectedTab, out var rows)) return;", navigation);
        Assert.Contains("ReferenceEquals(rows[index].Container, container)", navigation);
        Assert.Contains("private void SelectRenderedRow(Border container)", navigation);
    }

    [Fact]
    public void Tab_change_resets_detail_only_when_leaving_its_owner_tab()
    {
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var selectedState = ExtractMethod(shell, "private void ApplySelectedTabVisualState()");

        Assert.Contains("activeDetail.Tab != _tabState.SelectedTab", selectedState);
        Assert.Contains("ResetDetailForTabChangeOrShow();", selectedState);
        Assert.Contains("if (_activeDetailPage is null)", selectedState);
        Assert.Contains("DetailBackButton.Visibility = Visibility.Collapsed;", selectedState);
        Assert.Contains("PageTitle.Text = LabelFor(selected);", selectedState);
    }


    [Fact]
    public void Setting_root_rows_open_prebuilt_details_through_controller_and_pointer_activation()
    {
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var rootRowStart = clawHud.IndexOf("private OverlayRow CreateSettingRootRow", StringComparison.Ordinal);
        var rootRowEnd = clawHud.IndexOf("private void OpenSettingDetail", rootRowStart, StringComparison.Ordinal);
        var rootRow = clawHud[rootRowStart..rootRowEnd];
        var chromeStart = clawHud.IndexOf("private static (Border Container, Button Button) CreateDetailMenuChrome", StringComparison.Ordinal);
        var chromeEnd = clawHud.IndexOf("private void OpenSettingDetail", chromeStart, StringComparison.Ordinal);
        var chrome = clawHud[chromeStart..chromeEnd];

        Assert.Contains("var (container, headerButton) = CreateDetailMenuChrome(title, $\"Open {title} settings\");", rootRow);
        Assert.Contains("MinHeight = OverlayQamResources.Get(\"QamRowMinHeight\"", chrome);
        var rowMinHeight = XDocument.Parse(ReadSource(
            "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"))
            .Descendants()
            .Single(element => (string?)element.Attribute(XName.Get(
                "Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "QamRowMinHeight")
            .Value;
        Assert.True(double.TryParse(rowMinHeight, NumberStyles.Float, CultureInfo.InvariantCulture, out var minHeight)
            && minHeight >= 40);
        Assert.Contains("IsTabStop = false", chrome);
        Assert.Contains("AllowFocusOnInteraction = false", chrome);
        Assert.Contains("headerButton.Click += (_, _) => OpenSettingDetail(id, detailView, detailRows, container, title);", rootRow);
        Assert.Contains("Activate: () => OpenSettingDetail(id, detailView, detailRows, container, title)", rootRow);
        Assert.Contains("RegisterRowPointerSelection(container);", rootRow);
        Assert.Contains("IsSelectable: () => true", rootRow);
        Assert.DoesNotContain("Focus()", rootRow);
        Assert.DoesNotContain("CreateOverlayDetailStack", rootRow);
    }

    [Fact]
    public void Quick_settings_detail_uses_existing_runtime_preferences_and_tab_order_reconciles_only_when_visible()
    {
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var overlayApp = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");
        var host = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");

        Assert.Contains("SettingDetailId.ClawHud,", clawHud);
        Assert.Contains("SettingDetailId.QuickSettings,", clawHud);
        Assert.Contains("SettingDetailId.TabOrder,", clawHud);
        Assert.Contains("SetQuickSettingsCurrentPowerSourceOnlyAsync(request.CurrentPowerSourceOnly!.Value", host);
        Assert.Contains("SetQuickOverlayYButtonTargetAsync(target", host);
        Assert.Contains("SendCurrentPowerSourceMutationAsync", overlayApp);
        Assert.Contains("SendYButtonQuickJumpMutationAsync(target)", overlayApp);
        Assert.Contains("QuickSettingsCurrentPowerSourceOnlyRequested", clawHud);
        Assert.Contains("YButtonQuickJumpTargetRequested", clawHud);
        Assert.Contains("OverlayValueButtonKind.DiscreteChoice", clawHud);
        Assert.Contains("canInteract: _quickSettingsYButtonQuickJumpAvailable && !_yButtonQuickJumpMutationInFlight", clawHud);
        Assert.Contains("var quickSettingsMutation = !led && controllerForSteamMutation is null && !yButtonQuickJumpMutation", controller);
        Assert.Contains("if (quickSettingsMutation)", controller);
        var failureStart = controller.IndexOf("internal void ApplyFrontendSettingsMutationFailure(", StringComparison.Ordinal);
        var failureEnd = controller.IndexOf("private void RequestControllerLedEnabled", failureStart, StringComparison.Ordinal);
        var failure = controller[failureStart..failureEnd];
        var yFailureStart = failure.IndexOf("else if (yButtonQuickJumpMutation)", StringComparison.Ordinal);
        var yFailureEnd = failure.IndexOf("else SetQuickSettingsCurrentPowerSourceMutationInFlight(false)", yFailureStart, StringComparison.Ordinal);
        var yFailure = failure[yFailureStart..yFailureEnd];
        Assert.Contains("SetYButtonQuickJumpMutationInFlight(false);", yFailure);
        Assert.Contains("return;", yFailure);
        Assert.DoesNotContain("SetQuickSettingsCurrentPowerSourceMutationInFlight(false)", yFailure);
        Assert.Contains("request.CurrentPowerSourceOnly!.Value", host);
        Assert.Contains("Identity: SettingDetailId.TabOrder", shell);
        Assert.Contains("BuildTabOrderDetailRows(applied)", shell);
        Assert.Contains("ReconcileRowSelection(", shell);
        Assert.DoesNotContain("ResolvePreferredSettingRowIndex", shell);
        Assert.DoesNotContain("_clawHudRows.Count +", shell);
        Assert.DoesNotContain("SettingCard", clawHud);
    }

    [Fact]
    public void ClawHud_independent_state_and_correlated_mutation_result_have_separate_completion_paths()
    {
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var state = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayClawHudMutationState.cs");

        Assert.Contains("_window?.ApplyClawHudState(snapshot)", app);
        Assert.Contains("_window?.CompleteClawHudMutation(result)", app);
        Assert.Contains("_clawHudMutation.ApplyState(snapshot)", clawHud);
        Assert.Contains("_clawHudMutation.Complete(result)", clawHud);
        Assert.Contains("_clawHudMutation.Fail()", clawHud);
        Assert.Contains("PendingIntent = intent", state);
        Assert.Contains("PendingEnabled = desired", state);
        Assert.Contains("Snapshot = result.Snapshot", state);
    }

    [Fact]
    public void ClawHud_top_inset_uses_effective_settings_and_one_final_only_live_placement()
    {
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var presentation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs");
        var interop = ReadWindowInteropSource();
        var geometry = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindowGeometry.cs");

        Assert.Contains("_startupClawHudSettings.ResolveTopInsetPx(", clawHud);
        Assert.Contains("_clawHudMutation.PendingEnabled ?? _clawHudMutation.Snapshot?.DesiredEnabled", clawHud);
        Assert.Contains("_clawHudMutation.DisplaySettings", clawHud);
        Assert.Contains("pendingFont", clawHud);
        Assert.Contains("pendingSize", clawHud);
        Assert.Contains("Math.Clamp(hudSizeOffset, -2, 2)", geometry);
        var stateReconcile = SliceMethod(clawHud,
            "private void ReconcileClawHudTopInsetIfChanged(", "private void RenderClawHudControls()");
        Assert.Contains("if (currentInsetPx != previousInsetPx)", stateReconcile);
        Assert.DoesNotContain("SetWindowPos", stateReconcile);

        var configureWindow = SliceMethod(presentation, "private void ConfigureWindow(bool alreadyVisible = false)", "private void ReconcileVisibleClawHudTopInset(");
        Assert.Contains("WindowInterop.Configure(this, topInsetPx", configureWindow);
        Assert.Contains("alreadyVisible)", configureWindow);
        Assert.True(configureWindow.IndexOf("WindowInterop.Configure(this, topInsetPx", StringComparison.Ordinal)
            < configureWindow.IndexOf("_lastAppliedClawHudTopInsetPx = topInsetPx", StringComparison.Ordinal));

        var liveReconcile = SliceMethod(presentation, "private void ReconcileVisibleClawHudTopInset(", "private void UpdateRailClockText()");
        Assert.Contains("if (!_isVisible || topInsetPx == _lastAppliedClawHudTopInsetPx)", liveReconcile);
        Assert.Contains("if (WindowInterop.TryApplyVisibleClawHudTopInset(this, topInsetPx, out _))", liveReconcile);

        var livePlacement = SliceMethod(interop,
            "internal static bool TryApplyVisibleClawHudTopInset(", "internal static void ShowWithoutActivation(");
        Assert.Equal(1, CountOccurrences(livePlacement, "SetWindowPos("));
        Assert.Contains("GetWindowRect(hwnd, out var currentWindowRect)", livePlacement);
        Assert.Contains("MonitorFromWindow(hwnd, MonitorDefaultToNearest)", livePlacement);
        Assert.Contains("currentWindowRect.Left", livePlacement);
        Assert.Contains("currentWindowRect.Right - currentWindowRect.Left", livePlacement);
        Assert.DoesNotContain("SwpNoSendChanging", livePlacement);
        Assert.DoesNotContain("SwpNoZOrder", livePlacement);
        Assert.DoesNotContain("GetDpiForWindow", livePlacement);
        Assert.DoesNotContain("Configure(", livePlacement);
        Assert.Contains("retaining the last successful geometry", livePlacement);
        Assert.Contains("OverlayLog.Warn", livePlacement);

        foreach (var (start, end) in new[]
        {
            ("internal void ApplyClawHudState(", "internal void CompleteClawHudMutation("),
            ("internal void CompleteClawHudMutation(", "internal void ApplyClawHudFailure("),
            ("internal void ApplyClawHudFailure(", "private void RequestClawHudEnabled("),
            ("private void RequestClawHudEnabled(", "private void RequestClawHudSetting("),
            ("private void RequestClawHudSetting(", "private int CurrentClawHudTopInsetPx()"),
        })
        {
            var statePath = SliceMethod(clawHud, start, end);
            Assert.Contains("CurrentClawHudTopInsetPx()", statePath);
            Assert.Contains("ReconcileClawHudTopInsetIfChanged(previousTopInsetPx)", statePath);
        }

        Assert.DoesNotContain("ClawHudHwnd", livePlacement);
        Assert.DoesNotContain("GetWindowHandle(clawHud", livePlacement);
    }

    [Fact]
    public void Settings_pending_preview_and_selection_exception_remain_local_and_availability_bounded()
    {
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        var requestStart = clawHud.IndexOf("private void RequestQuickSettingsCurrentPowerSourceOnly", StringComparison.Ordinal);
        var requestEnd = clawHud.IndexOf("private void SetQuickSettingsCurrentPowerSourceMutationInFlight", requestStart, StringComparison.Ordinal);
        var request = clawHud[requestStart..requestEnd];
        var renderStart = clawHud.IndexOf("internal void RenderQuickSettingsCurrentPowerSourceRow", StringComparison.Ordinal);
        var renderEnd = clawHud.IndexOf("private StackPanel BuildClawHudPage", renderStart, StringComparison.Ordinal);
        var render = clawHud[renderStart..renderEnd];
        var completionStart = controller.IndexOf("internal void ApplyFrontendSettingsMutationResult(", StringComparison.Ordinal);
        var completionEnd = controller.IndexOf("internal void ApplyFrontendSettingsMutationFailure(", completionStart, StringComparison.Ordinal);
        var completion = controller[completionStart..completionEnd];

        var pendingPreviewIndex = request.IndexOf("_pendingQuickSettingsCurrentPowerSourceOnly = enabled", StringComparison.Ordinal);
        Assert.True(pendingPreviewIndex >= 0);
        Assert.True(pendingPreviewIndex < request.IndexOf("RenderQuickSettingsCurrentPowerSourceRow();", pendingPreviewIndex, StringComparison.Ordinal));
        Assert.Contains("_pendingQuickSettingsCurrentPowerSourceOnly ?? _quickSettingsCurrentPowerSourceOnly", render);
        Assert.Contains("_quickSettingsSettingsAvailable && !_quickSettingsMutationInFlight", render);
        Assert.True(completion.IndexOf("ApplyFrontendSettingsState(response.Settings", StringComparison.Ordinal)
            < completion.IndexOf("SetQuickSettingsCurrentPowerSourceMutationInFlight(false)", StringComparison.Ordinal));

        Assert.Contains("IsSettingDetailActive(detail) &&", clawHud);
        Assert.Contains("capabilities.IsSelectable() || IsNavigableDuringPendingSettingMutation(detail, row.Container)", clawHud);
        Assert.Contains("ReferenceEquals(rows[selectedIndex].Container, container)", clawHud);
        Assert.Contains("snapshot?.RuntimeState == FrontendClawHudRuntimeState.Ready &&", clawHud);
        Assert.Contains("snapshot.Settings is not null &&", clawHud);
        Assert.Contains("_clawHudRows.Any(row => ReferenceEquals(row.Container, container))", clawHud);
        Assert.DoesNotContain("GetPendingClawHudRowContainer", clawHud);
        Assert.Contains("_quickSettingsSettingsAvailable &&", clawHud);
        Assert.Contains("detail == SettingDetailId.QuickSettings && _quickSettingsMutationInFlight", clawHud);

        var clawHudRenderStart = clawHud.IndexOf("private void RenderClawHudControls()", StringComparison.Ordinal);
        var clawHudRender = clawHud[clawHudRenderStart..];
        Assert.DoesNotContain("BringSelectedRowIntoView", clawHudRender);
        Assert.DoesNotContain("BringSelectedRowIntoView", render);
    }

    [Fact]
    public void ClawHud_detail_rows_use_the_existing_logical_selection_capabilities()
    {
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var wrapperStart = clawHud.IndexOf("private OverlayRow CreateSettingDetailRow(", StringComparison.Ordinal);
        var wrapperEnd = clawHud.IndexOf("private bool IsSettingDetailActive", wrapperStart, StringComparison.Ordinal);
        var wrapper = clawHud[wrapperStart..wrapperEnd];

        Assert.Contains("IsSelectable: () => IsSettingDetailActive(detail) &&", wrapper);
        Assert.Contains("capabilities.IsSelectable() || IsNavigableDuringPendingSettingMutation(detail, row.Container)", wrapper);
        Assert.Contains("Activate: capabilities.Activate", wrapper);
        Assert.Contains("Adjust: capabilities.Adjust", wrapper);
        Assert.DoesNotContain("rowIndex", clawHud);
        Assert.DoesNotContain("CanInvokeSettingDetailAction", clawHud);
    }



    [Fact]
    public void Rail_clock_refreshes_on_show_and_stops_on_hide_or_close()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml");
        var presentation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs");
        var window = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs");
        var show = SliceMethod(presentation, "internal async Task ShowForPocAsync()", "internal Task HideForPocAsync()");
        var hide = SliceMethod(presentation, "internal Task HideForPocAsync()", "private void ConfigureWindow(bool alreadyVisible = false)");
        var clock = SliceMethod(presentation, "private void UpdateRailClockText()", "private void LogSurfaceBounds(");

        Assert.Contains("UpdateRailClockText();", show);
        Assert.Contains("StartRailClockUpdates();", show);
        Assert.Contains("StopRailClockUpdates();", hide);
        Assert.Contains("StopRailClockUpdates();", window);
        Assert.Contains("UpdateRailClockText();", clock);
        Assert.Contains("TimeSpan.FromSeconds(20)", clock);
        Assert.Contains("_railClockTimer.Tick += OnRailClockTimerTick;", clock);
        Assert.Contains("catch (Exception exception)", clock);
        Assert.Contains("continuing without affecting Overlay setup", clock);
        Assert.Contains("_railClockTimer?.Stop();", presentation);
        Assert.Contains("RailClockText", xaml);
    }




    private static string ReadOverlayWindowSource() => string.Join(
        Environment.NewLine,
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs"));

    private static string ReadNoGameProfileMessage() =>
        (string?)typeof(OverlayWindow).GetField(
            "NoActiveGameMessage",
            BindingFlags.Static | BindingFlags.NonPublic)?.GetRawConstantValue()
        ?? throw new InvalidOperationException("The no-game profile message is unavailable.");

    private static string ReadWindowInteropSource() => ReadSource("src", "SteamInputAddonforClaw.Overlay", "WindowInterop.cs");

    private static string SliceMethod(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

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

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var openBrace = source.IndexOf('{', start);
        var depth = 0;
        var index = openBrace;
        for (; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) break;
        }
        return source[start..(index + 1)];
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
