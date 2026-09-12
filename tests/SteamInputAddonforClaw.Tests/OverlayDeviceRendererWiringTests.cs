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
// OverlayToggleRowTests/OverlaySliderRowTests already accept for the row primitives (validated on
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
        Assert.Contains("HwndTopmost", show);
        Assert.Contains("SwpNoActivate", show);
        Assert.Contains("SwpShowWindow", show);
        Assert.DoesNotContain("SwpNoZOrder", show);

        var hide = source[source.IndexOf("internal static void Hide", StringComparison.Ordinal)..
            source.IndexOf("private static void LogTopmostStateAfterShow", StringComparison.Ordinal)];
        Assert.Contains("SwpNoZOrder", hide);
        Assert.Contains("SwpNoSize", hide);
        Assert.Contains("SwpNoMove", hide);
        Assert.Contains("SwpHideWindow", hide);
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
        Assert.Contains("surface.RowShape.SequenceEqual(rowShape)", renderQuickSettingsPage);

        var rebuildContent = source[source.IndexOf("private void RebuildQuickSettingsContent(QuickSettingsSurface surface", StringComparison.Ordinal)..
            source.IndexOf("private static TextBlock CreateQuickSettingsMessageText", StringComparison.Ordinal)];
        Assert.Contains("surface.RenderedAppId = page.AppId;", rebuildContent);
        Assert.Contains("surface.RenderedSections = QuickSettingsSectionShapeOf(page);", rebuildContent);
    }

    // SF-V2-09 section 32: Device and Profile share one generic renderer/binder path -- BuildPage's
    // dispatch calls the SAME BuildQuickSettingsPage helper for both tab identities, and the historic
    // Profile placeholder is gone.
    [Fact]
    public void Device_and_profile_tabs_both_route_through_the_same_generic_quick_settings_builder()
    {
        var source = ReadOverlayWindowSource();

        Assert.Contains("OverlayTabId.Device => BuildQuickSettingsPage(id, QuickSettingsPageId.Device)", source);
        Assert.Contains("OverlayTabId.Profile => BuildQuickSettingsPage(id, QuickSettingsPageId.Profile)", source);
        Assert.DoesNotContain("CreatePlaceholderPage(OverlayTabId.Profile)", source);
        Assert.DoesNotContain("CreatePlaceholderPage(id: OverlayTabId.Profile)", source);
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

    private static string ReadOverlayWindowSource() => ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs");

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
