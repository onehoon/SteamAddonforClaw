using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// SF-V2-07 section 52: OverlayWindow/App's actual WinUI wiring can't be exercised directly in this
// test project -- constructing OverlayWindow needs a XAML host, the same limitation
// OverlayToggleRowTests/OverlaySliderRowTests already accept for the row primitives (validated on
// hardware per section 54). These are focused reflection/composition regressions over the compiled
// shape instead, mirroring the SF-V2-06 precedent (the removed-v6-dispatch-authority regression).
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
    // assertions -- proving RenderDevicePage() actually consumes and clears the binder's local
    // failure fact in BOTH the fast (value-only) path and the structural-rebuild path, per the PR
    // #508 review that flagged a mutation failure being retained in the binder but never surfaced.
    [Fact]
    public void RenderDevicePage_surfaces_and_clears_the_binder_local_failure_message_on_both_paths()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs");

        Assert.Contains("private void ApplyDeviceLocalFailure()", source);
        Assert.Contains("_deviceBinding.LastLocalFailureMessage", source);
        // Cleared, not just shown: no message collapses the banner again.
        Assert.Contains("Visibility.Collapsed", source);

        var renderDevicePage = source[source.IndexOf("private void RenderDevicePage()", StringComparison.Ordinal)..
            source.IndexOf("private static DeviceRowShape DeviceRowShapeOf", StringComparison.Ordinal)];
        var fastPathCallCount = CountOccurrences(renderDevicePage, "ApplyDeviceLocalFailure();");
        Assert.Equal(2, fastPathCallCount); // once after the fast-path update, once after a rebuild
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

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
