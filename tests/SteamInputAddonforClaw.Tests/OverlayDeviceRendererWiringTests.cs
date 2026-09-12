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
}
