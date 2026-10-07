using System;
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
            source.IndexOf("internal static void ShowWithoutActivation", StringComparison.Ordinal)];
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
            windowInterop.IndexOf("internal static void ShowWithoutActivation", StringComparison.Ordinal)];
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

        var renderQuickSettingsPage = source[source.IndexOf("private void RenderQuickSettingsPage(QuickSettingsSurface surface)", StringComparison.Ordinal)..
            source.IndexOf("private static void UpdateQuickSettingsRowValues", StringComparison.Ordinal)];
        Assert.Contains("ReconcileQuickSettingsSections(surface, page);", renderQuickSettingsPage);
        Assert.Contains("RebuildQuickSettingsContent(surface, page);", renderQuickSettingsPage);
    }

    [Fact]
    public void Unavailable_quick_settings_page_clears_controls_without_adding_a_message_element()
    {
        var source = ReadOverlayWindowSource();
        var build = source[source.IndexOf("private FrameworkElement BuildQuickSettingsPage(", StringComparison.Ordinal)..
            source.IndexOf("private void RenderQuickSettingsPage(", StringComparison.Ordinal)];
        var rebuild = source[source.IndexOf("private void RebuildQuickSettingsContent(", StringComparison.Ordinal)..
            source.IndexOf("private void ReconcileQuickSettingsSections(", StringComparison.Ordinal)];
        var unavailable = rebuild[rebuild.IndexOf("if (!page.Available)", StringComparison.Ordinal)..
            rebuild.IndexOf("else", rebuild.IndexOf("if (!page.Available)", StringComparison.Ordinal), StringComparison.Ordinal)];

        Assert.Contains("Content = content", build);
        Assert.Contains("return content", build);
        Assert.DoesNotContain("TextBlock", build);
        Assert.Contains("surface.Content.Children.Clear()", rebuild);
        Assert.Contains("surface.RenderedAvailable = false", unavailable);
        Assert.DoesNotContain("TextBlock", unavailable);
        Assert.DoesNotContain("Children.Add", unavailable);
        Assert.DoesNotContain("page.Message", source);
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
    public void Quick_settings_section_separators_follow_the_current_visible_feature_order()
    {
        var source = ReadOverlayWindowSource();
        var rebuild = source[source.IndexOf("private void RebuildQuickSettingsContent", StringComparison.Ordinal)..
            source.IndexOf("private void ReconcileQuickSettingsSections", StringComparison.Ordinal)];
        var reconcile = source[source.IndexOf("private void ReconcileQuickSettingsSections", StringComparison.Ordinal)..
            source.IndexOf("private RenderedQuickSettingsSection BuildQuickSettingsSection", StringComparison.Ordinal)];
        var separatorPolicy = source[source.IndexOf("private static void ApplyQuickSettingsSectionSeparators", StringComparison.Ordinal)..
            source.IndexOf("private static Border CreateOverlaySectionCard", StringComparison.Ordinal)];

        Assert.Contains("ApplyQuickSettingsSectionSeparators(surface, page);", rebuild);
        Assert.Contains("ReorderQuickSettingsSectionCards(surface, page);", reconcile);
        Assert.Contains("ApplyQuickSettingsSectionSeparators(surface, page);", reconcile);
        Assert.Contains("rendered.Card is null", separatorPolicy);
        Assert.Contains("surface.PageId == QuickSettingsPageId.Profile", separatorPolicy);
        Assert.Contains("section.SectionId == QuickSettingsSectionId.ProfileGeneral", separatorPolicy);
        Assert.Contains("var hasFeature = false;", separatorPolicy);
        Assert.Contains("SetOverlaySectionSeparator(rendered.Card, hasFeature)", separatorPolicy);
        Assert.True(separatorPolicy.IndexOf("SetOverlaySectionSeparator(rendered.Card, hasFeature)", StringComparison.Ordinal)
            < separatorPolicy.IndexOf("hasFeature = true", StringComparison.Ordinal));
        Assert.DoesNotContain("QuickSettingsSectionId.ProfileTdp", separatorPolicy);
    }

    [Fact]
    public void Quick_settings_section_updates_preserve_selected_row_identity_and_scroll_only_when_needed()
    {
        var source = ReadOverlayWindowSource();
        var selection = source[source.IndexOf("private void UpdateQuickSettingsPageRows", StringComparison.Ordinal)..
            source.IndexOf("private static Border CreateOverlaySectionCard", StringComparison.Ordinal)];

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
        Assert.DoesNotContain("previous.Message", comparer);
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
    public void Active_profile_first_show_selects_profile_and_uses_the_exact_centered_empty_state()
    {
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var profile = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");

        Assert.Contains("if (preferActiveProfile) PrepareActiveProfileFirstShow();", shell);
        Assert.Contains("if (preferActiveProfile) _tabState.Select(AddonQuickSettingsTabId.Profile);", shell);
        Assert.Contains("case OverlayCommand.ShowActiveProfile:", app);
        Assert.Contains("ShowForPocAsync(preferActiveProfile: true)", app);
        Assert.Contains("No game is currently running. Start a game to configure its profile.", profile);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Center", profile);
        Assert.Contains("VerticalAlignment = VerticalAlignment.Center", profile);
        Assert.Contains("_noActiveGameMessage.FontSize = 20", profile);
        Assert.Contains("page is not { PageId: QuickSettingsPageId.Profile, Available: true, ProfileTarget: { IsStructurallyValid: true } }", profile);
        Assert.Contains("_noActiveGameMessage.Visibility = noActiveGame ? Visibility.Visible : Visibility.Collapsed", profile);
        Assert.Contains("_profileDetailRoot.Visibility = noActiveGame ? Visibility.Collapsed : Visibility.Visible", profile);
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
        Assert.Contains("QamValueButtonStyle", source);
        Assert.Contains("QamValueButtonWidth", source);
        Assert.Contains("QamValueButtonHeight", source);
        Assert.Contains("QamValueButtonCornerRadius", source);
        Assert.Contains("button.Click += click", source);
        Assert.Contains("_model.RequestAdjust(-1)", source);
        Assert.Contains("_model.RequestAdjust(+1)", source);
        Assert.Contains("Adjust: OnControllerAdjust", source);
        Assert.Contains("Activate: null", source);
        Assert.DoesNotContain("CreateArrowButton", source);
        Assert.DoesNotContain("‹", source);
        Assert.DoesNotContain("›", source);
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
        Assert.Contains("var rowStack = new StackPanel { Spacing = OverlayQamResources.Get(\"QamRowSpacing\", 0.0) }", quickSettings);
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
    public void Profile_resolution_row_uses_the_shared_detail_indent_in_the_generic_renderer()
    {
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var buildStart = quickSettings.IndexOf("private RenderedQuickSettingsSection BuildQuickSettingsSection", StringComparison.Ordinal);
        var buildEnd = quickSettings.IndexOf("private static void RemoveQuickSettingsSection", buildStart, StringComparison.Ordinal);
        var buildSection = quickSettings[buildStart..buildEnd];

        Assert.Contains("var detailStack = CreateOverlayDetailStack();", buildSection);
        Assert.Contains("detailStack.Children.Add(overlayRow.Container);", buildSection);
        Assert.Contains("rowStack.Children.Add(detailStack);", buildSection);
        Assert.DoesNotContain("QamDetailIndent", buildSection);
        var detailStackStart = quickSettings.IndexOf("private static StackPanel CreateOverlayDetailStack()", StringComparison.Ordinal);
        var detailStackEnd = quickSettings.IndexOf("private static void SetOverlaySectionSeparator", detailStackStart, StringComparison.Ordinal);
        var detailStack = quickSettings[detailStackStart..detailStackEnd];
        Assert.Contains("QamRowSpacing", detailStack);
        Assert.Contains("QamDetailIndent", detailStack);
    }

    [Fact]
    public void Heading_only_quick_settings_sections_use_shared_row_chrome_without_an_extra_header_gap()
    {
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var helperStart = quickSettings.IndexOf("private static Border CreateOverlaySectionHeadingRow", StringComparison.Ordinal);
        var helperEnd = quickSettings.IndexOf("private static StackPanel CreateOverlayDetailStack", helperStart, StringComparison.Ordinal);
        var headingHelper = quickSettings[helperStart..helperEnd];
        var buildStart = quickSettings.IndexOf("private RenderedQuickSettingsSection BuildQuickSettingsSection", StringComparison.Ordinal);
        var buildEnd = quickSettings.IndexOf("private static void RemoveQuickSettingsSection", buildStart, StringComparison.Ordinal);
        var build = quickSettings[buildStart..buildEnd];
        var rowStackStart = build.IndexOf("var rowStack = new StackPanel", StringComparison.Ordinal);
        var rowStackEnd = build.IndexOf("sectionPanel.Children.Add(rowStack);", rowStackStart, StringComparison.Ordinal);
        var rowStack = build[rowStackStart..rowStackEnd];

        Assert.True(helperStart >= 0);
        Assert.True(helperEnd > helperStart);
        Assert.Contains("QamBodyStrongTextStyle", headingHelper);
        Assert.Contains("OverlayRowChrome.Create(heading)", headingHelper);
        Assert.DoesNotContain("RegisterRowPointerSelection", headingHelper);
        Assert.DoesNotContain("new OverlayRow", headingHelper);
        Assert.DoesNotContain("_pageRows", headingHelper);
        Assert.Contains("QamRowSpacing", rowStack);
        Assert.Contains("rowStack.Children.Add(CreateOverlaySectionHeadingRow(section.Label));", rowStack);
        Assert.Contains("var detailStack = CreateOverlayDetailStack();", rowStack);
        Assert.Contains("rowStack.Children.Add(detailStack);", rowStack);
        Assert.DoesNotContain("QamSectionHeaderSpacing", rowStack);
        Assert.DoesNotContain("new TextBlock { Text = section.Label", build);
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
        Assert.Contains("CreateSettingCard(SettingCardId.QuickSettings", clawHud);
        Assert.Contains("CreateSettingCard(SettingCardId.TabOrder", clawHud);
        Assert.Contains("Quick Settings", clawHud);
        Assert.Contains("Show only current power source", clawHud);
        Assert.Contains("ApplyQuickSettingsCurrentPowerSourceOnly(settings.QuickSettingsCurrentPowerSourceOnly, settingsAvailable)",
            ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs"));
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
    public void Setting_cards_use_the_shared_separator_only_on_outer_sections()
    {
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var settingPage = clawHud[clawHud.IndexOf("private FrameworkElement BuildSettingPage", StringComparison.Ordinal)..
            clawHud.IndexOf("private SettingCardView CreateSettingCard", StringComparison.Ordinal)];
        var createCard = clawHud[clawHud.IndexOf("private SettingCardView CreateSettingCard", StringComparison.Ordinal)..
            clawHud.IndexOf("private List<OverlayRow> BuildSettingRows", StringComparison.Ordinal)];

        Assert.Contains("SetOverlaySectionSeparator(_clawHudCard.Container, visible: false)", settingPage);
        Assert.Contains("SetOverlaySectionSeparator(_quickSettingsCard.Container, visible: true)", settingPage);
        Assert.Contains("SetOverlaySectionSeparator(_tabOrderCard.Container, visible: true)", settingPage);
        Assert.Contains("CreateOverlaySectionCard(content)", createCard);
        Assert.DoesNotContain("SetOverlaySectionSeparator", createCard);
        Assert.DoesNotContain("QamSeparatorBrush", createCard);
    }

    [Fact]
    public void Quick_settings_setting_card_uses_the_existing_runtime_preference_and_dynamic_row_remap()
    {
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var overlayApp = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");
        var host = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");

        Assert.True(clawHud.IndexOf("CreateSettingCard(SettingCardId.ClawHud", StringComparison.Ordinal)
            < clawHud.IndexOf("CreateSettingCard(SettingCardId.QuickSettings", StringComparison.Ordinal));
        Assert.True(clawHud.IndexOf("CreateSettingCard(SettingCardId.QuickSettings", StringComparison.Ordinal)
            < clawHud.IndexOf("CreateSettingCard(SettingCardId.TabOrder", StringComparison.Ordinal));
        Assert.Contains("SetQuickSettingsCurrentPowerSourceOnlyAsync(request.CurrentPowerSourceOnly!.Value", host);
        Assert.Contains("SendCurrentPowerSourceMutationAsync", overlayApp);
        Assert.Contains("QuickSettingsCurrentPowerSourceOnlyRequested", clawHud);
        Assert.Contains("request.CurrentPowerSourceOnly!.Value", host);
        Assert.Contains("FindSettingRowIndex(previousSettingRows, firstPreviousEditor.Container)", shell);
        Assert.DoesNotContain("_clawHudRows.Count + 2", shell);
        Assert.Contains("GetSettingCard(expanded).Row.Container", clawHud);
        Assert.Contains("UpdateSettingCardVisual(_quickSettingsCard)", clawHud);
    }

    [Fact]
    public void ClawHud_detail_rows_use_the_existing_logical_selection_capabilities()
    {
        var clawHud = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs");
        var wrapperStart = clawHud.IndexOf("private OverlayRow CreateSettingDetailRow(", StringComparison.Ordinal);
        var wrapperEnd = clawHud.IndexOf("private void ToggleSettingCard(", wrapperStart, StringComparison.Ordinal);
        var wrapper = clawHud[wrapperStart..wrapperEnd];

        Assert.Contains("IsSelectable: () => _expandedSettingCard == card && capabilities.IsSelectable()", wrapper);
        Assert.Contains("Activate: capabilities.Activate", wrapper);
        Assert.Contains("Adjust: capabilities.Adjust", wrapper);
        Assert.DoesNotContain("rowIndex", clawHud);
        Assert.DoesNotContain("CanInvokeSettingDetailAction", clawHud);
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
    public void Overlay_shell_uses_a_left_vertical_fluent_icon_rail_and_passive_bumper_hints()
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
        var document = XDocument.Parse(xaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var railElement = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "TabRail");
        var railCluster = Assert.Single(railElement.Elements(), element =>
            element.Name.LocalName == "StackPanel" && (string?)element.Attribute(x + "Name") == "TabRailCluster");
        var clusterChildren = railCluster.Elements().ToArray();
        var tabStrip = clusterChildren.Single(element => (string?)element.Attribute(x + "Name") == "TabStrip");
        var previousHint = clusterChildren.Single(element => (string?)element.Attribute(x + "Name") == "PreviousTabBumperHint");
        var nextHint = clusterChildren.Single(element => (string?)element.Attribute(x + "Name") == "NextTabBumperHint");
        var railFontIcons = railElement.Descendants().Where(element => element.Name.LocalName == "FontIcon").ToArray();

        Assert.Contains("x:Name=\"SurfaceHost\"", xaml);
        Assert.Contains("x:Name=\"OpaquePanel\"", xaml);
        Assert.Contains("MaxWidth=\"432\"", xaml);
        Assert.Contains("x:Name=\"QamShell\"", xaml);
        Assert.Contains("<ColumnDefinition Width=\"52\" />", xaml);
        Assert.Contains("<ColumnDefinition Width=\"*\" />", xaml);
        Assert.Contains("x:Name=\"TabRail\"", tabRail);
        Assert.Contains("Grid.Column=\"0\"", tabRail);
        Assert.Contains("x:Name=\"TabStrip\"", tabRail);
        Assert.Contains("x:Name=\"TabRailCluster\"", tabRail);
        Assert.Equal("Center", (string?)railCluster.Attribute("VerticalAlignment"));
        Assert.Equal("Stretch", (string?)railCluster.Attribute("HorizontalAlignment"));
        Assert.Equal("{StaticResource QamRailHintGap}", (string?)railCluster.Attribute("Spacing"));
        Assert.Equal(
            new[] { "PreviousTabBumperHint", "TabStrip", "NextTabBumperHint" },
            clusterChildren.Select(element => (string?)element.Attribute(x + "Name")));
        Assert.Equal("{StaticResource QamRailSpacing}", (string?)tabStrip.Attribute("RowSpacing"));
        Assert.Null(previousHint.Attribute("Margin"));
        Assert.Null(nextHint.Attribute("Margin"));
        Assert.DoesNotContain("QamRailHintMargin", xaml);
        Assert.DoesNotContain("ColumnDefinitions", tabRail);
        Assert.DoesNotContain("Grid.ColumnDefinitions", bodyColumn);
        Assert.Contains("Grid.Column=\"1\"", bodyColumn);
        Assert.DoesNotContain("Margin=\"{StaticResource QamBodyContentPadding}\"", bodyColumn);
        Assert.Contains("Padding=\"{StaticResource QamBodyContentPadding}\"", bodyColumn);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xaml, "<ScrollViewer\\b"));
        Assert.Contains("x:Name=\"BodyScroll\"", xaml);
        Assert.Contains("x:Name=\"TabBody\"", xaml);
        Assert.DoesNotContain("Steam_LB.png", xaml);
        Assert.DoesNotContain("Steam_RB.png", xaml);
        Assert.DoesNotContain("Assets\\Controller\\Steam_LB.png", project);
        Assert.DoesNotContain("Assets\\Controller\\Steam_RB.png", project);
        Assert.Equal("\uF10C", (string?)previousHint.Attribute("Glyph"));
        Assert.Equal("\uF10D", (string?)nextHint.Attribute("Glyph"));
        Assert.Equal("Segoe Fluent Icons", (string?)previousHint.Attribute("FontFamily"));
        Assert.Equal("Segoe Fluent Icons", (string?)nextHint.Attribute("FontFamily"));
        Assert.Equal("{StaticResource QamRailHintIconSize}", (string?)previousHint.Attribute("FontSize"));
        Assert.Equal("{StaticResource QamRailHintIconSize}", (string?)nextHint.Attribute("FontSize"));
        Assert.Equal("{StaticResource QamDisabledTextBrush}", (string?)previousHint.Attribute("Foreground"));
        Assert.Equal("{StaticResource QamDisabledTextBrush}", (string?)nextHint.Attribute("Foreground"));
        Assert.Equal("False", (string?)previousHint.Attribute("IsHitTestVisible"));
        Assert.Equal("False", (string?)nextHint.Attribute("IsHitTestVisible"));
        Assert.Single(railFontIcons, element => (string?)element.Attribute("Glyph") == "\uF10C");
        Assert.Single(railFontIcons, element => (string?)element.Attribute("Glyph") == "\uF10D");
        Assert.Equal("LB, previous tab", (string?)previousHint.Attribute("AutomationProperties.Name"));
        Assert.Equal("RB, next tab", (string?)nextHint.Attribute("AutomationProperties.Name"));
        Assert.Same(railCluster, tabStrip.Parent);
        Assert.Same(railCluster, previousHint.Parent);
        Assert.Same(railCluster, nextHint.Parent);
        Assert.DoesNotContain(tabStrip.DescendantsAndSelf(), element => ReferenceEquals(element, previousHint));
        Assert.DoesNotContain(tabStrip.DescendantsAndSelf(), element => ReferenceEquals(element, nextHint));
        Assert.Contains("Grid.SetRow(tabHost, position)", shell);
        Assert.DoesNotContain("position + 1", shell);
        Assert.DoesNotContain("Grid.SetColumn(tabHost, position)", shell);
        Assert.Contains("TabStrip.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });", shell);
        Assert.Contains("AutomationProperties.SetName(button, label)", shell);
        Assert.Contains("ToolTipService.SetToolTip(button, label)", shell);
        Assert.Contains("Content = new FontIcon", shell);
        Assert.Contains("FontFamily = new FontFamily(\"Segoe Fluent Icons\")", shell);
        Assert.Contains("Glyph = GlyphFor(id)", shell);
        Assert.Contains("FontSize = OverlayQamResources.Get(\"QamRailIconSize\", 24.0)", shell);
        Assert.Contains("private static string GlyphFor(AddonQuickSettingsTabId id)", shell);
        Assert.Contains("AddonQuickSettingsTabId.Device => \"\\uE945\"", shell);
        Assert.Contains("AddonQuickSettingsTabId.Profile => \"\\uE71D\"", shell);
        Assert.Contains("AddonQuickSettingsTabId.Controller => \"\\uE7FC\"", shell);
        Assert.Contains("AddonQuickSettingsTabId.Shortcut => \"\\uE75F\"", shell);
        Assert.Contains("AddonQuickSettingsTabId.Setting => \"\\uE713\"", shell);
        Assert.DoesNotContain("SymbolIcon", shell);
        Assert.DoesNotContain("SymbolFor(", shell);
        Assert.Contains("QamRailButtonStyle", shell);
        Assert.Contains("QamRailIconSize", shell);
        Assert.DoesNotContain("Foreground =", shell[shell.IndexOf("Content = new FontIcon", StringComparison.Ordinal)..shell.IndexOf("Tag = id", StringComparison.Ordinal)]);
        var selectedVisual = shell[shell.IndexOf("private void ApplySelectedHeaderVisual()", StringComparison.Ordinal)..
            shell.IndexOf("// s.12: deterministic tab-change ordering", StringComparison.Ordinal)];
        Assert.Contains("button.Background =", selectedVisual);
        Assert.Contains("button.Foreground =", selectedVisual);
        Assert.DoesNotContain("FontIcon", selectedVisual);
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
        Assert.DoesNotContain("QamRowSeparatorThickness", chrome);
        Assert.DoesNotContain("QamSeparatorBrush", chrome);
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
