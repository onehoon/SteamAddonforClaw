using SteamInputAddonforClaw.Contracts.FrontButtons;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// App UI PR-C section 22.1 / 22.2 / 22.3: the one atomic front-button mapping contract -- frozen
/// defaults, no <c>None</c>, no persisted Double slots, the shared per-domain capability table, and
/// the same-domain uniqueness invariant.
/// </summary>
public sealed class FrontButtonMappingContractTests
{
    [Fact]
    public void Frozen_defaults_match_the_work_order()
    {
        var d = FrontButtonMappingSettings.Default;

        Assert.Equal(FrontButtonAction.QuickSettingsOverlay, d.Resolve(FrontButtonKind.Gamebar, FrontButtonDomain.Normal).Action);
        Assert.Equal(FrontButtonAction.SteamBigPicture, d.Resolve(FrontButtonKind.CenterM, FrontButtonDomain.Normal).Action);
        Assert.Equal(FrontButtonAction.SteamButton, d.Resolve(FrontButtonKind.Gamebar, FrontButtonDomain.Steam).Action);
        Assert.Equal(FrontButtonAction.SteamQuickAccess, d.Resolve(FrontButtonKind.CenterM, FrontButtonDomain.Steam).Action);
    }

    [Fact]
    public void Defaults_are_a_valid_mapping()
        => Assert.Null(FrontButtonMappingValidation.Validate(FrontButtonMappingSettings.Default));

    [Fact]
    public void Action_vocabulary_has_no_none_member()
        => Assert.DoesNotContain("None", Enum.GetNames<FrontButtonAction>());

    [Fact]
    public void Mapping_contract_carries_no_persisted_double_slots()
    {
        foreach (var type in new[] { typeof(FrontButtonMappingSettings), typeof(FrontButtonDomainMapping), typeof(FrontButtonBinding) })
            foreach (var property in type.GetProperties())
            {
                Assert.DoesNotContain("Double", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Single", property.Name, StringComparison.OrdinalIgnoreCase);
            }
    }

    [Fact]
    public void Binding_always_carries_non_null_action_hotkey_and_launch()
    {
        var binding = new FrontButtonBinding();
        Assert.NotNull(binding.Hotkey);
        Assert.NotNull(binding.Launch);
        Assert.True(Enum.IsDefined(binding.Action));
    }

    [Fact]
    public void Normal_domain_offers_exactly_the_locked_set()
        => Assert.Equal(
            [FrontButtonAction.QuickSettingsOverlay, FrontButtonAction.SteamBigPicture, FrontButtonAction.KeyboardHotkey, FrontButtonAction.LaunchApplication],
            FrontButtonActionCapabilities.ActionsFor(FrontButtonDomain.Normal));

    [Fact]
    public void Steam_domain_offers_exactly_the_locked_set()
        => Assert.Equal(
            [FrontButtonAction.QuickSettingsOverlay, FrontButtonAction.SteamButton, FrontButtonAction.SteamQuickAccess, FrontButtonAction.KeyboardHotkey, FrontButtonAction.LaunchApplication],
            FrontButtonActionCapabilities.ActionsFor(FrontButtonDomain.Steam));

    [Theory]
    [InlineData(FrontButtonAction.SteamBigPicture, true, false)]
    [InlineData(FrontButtonAction.SteamButton, false, true)]
    [InlineData(FrontButtonAction.SteamQuickAccess, false, true)]
    [InlineData(FrontButtonAction.QuickSettingsOverlay, true, true)]
    [InlineData(FrontButtonAction.KeyboardHotkey, true, true)]
    [InlineData(FrontButtonAction.LaunchApplication, true, true)]
    public void Capability_matrix_matches_the_locked_specification(FrontButtonAction action, bool normal, bool steam)
    {
        Assert.Equal(normal, FrontButtonActionCapabilities.Supports(action, FrontButtonDomain.Normal));
        Assert.Equal(steam, FrontButtonActionCapabilities.Supports(action, FrontButtonDomain.Steam));
    }

    [Fact]
    public void An_unknown_action_value_is_supported_in_no_domain()
    {
        var unknown = (FrontButtonAction)9999;
        Assert.False(FrontButtonActionCapabilities.Supports(unknown, FrontButtonDomain.Normal));
        Assert.False(FrontButtonActionCapabilities.Supports(unknown, FrontButtonDomain.Steam));
    }

    [Fact]
    public void The_ui_offered_set_and_runtime_validation_agree_for_every_domain()
    {
        foreach (var domain in Enum.GetValues<FrontButtonDomain>())
        {
            var offered = FrontButtonActionCapabilities.ActionsFor(domain);
            foreach (var action in Enum.GetValues<FrontButtonAction>())
                Assert.Equal(offered.Contains(action), FrontButtonActionCapabilities.Supports(action, domain));
        }
    }

    [Fact]
    public void Same_domain_duplicate_is_rejected()
    {
        var mapping = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.CenterM, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay));

        Assert.NotNull(FrontButtonMappingValidation.Validate(mapping));
    }

    [Fact]
    public void Same_action_across_different_domains_is_allowed()
    {
        var mapping = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.Gamebar, FrontButtonDomain.Steam, FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay));

        // Normal.Gamebar is also QuickSettingsOverlay by default; cross-domain reuse must be fine.
        Assert.Null(FrontButtonMappingValidation.Validate(mapping));
    }

    [Fact]
    public void Duplicate_hotkey_actions_are_rejected_even_with_different_payloads()
    {
        var mapping = FrontButtonMappingSettings.Default
            .With(FrontButtonKind.Gamebar, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.KeyboardHotkey) with
            {
                Hotkey = new FrontButtonHotkeyBinding(FrontButtonHotkeyModifiers.Control, FrontButtonHotkeyKey.F1)
            })
            .With(FrontButtonKind.CenterM, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.KeyboardHotkey) with
            {
                Hotkey = new FrontButtonHotkeyBinding(FrontButtonHotkeyModifiers.Alt, FrontButtonHotkeyKey.F2)
            });

        Assert.NotNull(FrontButtonMappingValidation.Validate(mapping));
    }

    [Fact]
    public void Duplicate_launch_application_actions_are_rejected_even_with_different_executables()
    {
        var mapping = FrontButtonMappingSettings.Default
            .With(FrontButtonKind.Gamebar, FrontButtonDomain.Steam, FrontButtonBinding.Of(FrontButtonAction.LaunchApplication) with
            {
                Launch = new FrontButtonLaunchApplicationBinding(@"C:\a.exe")
            })
            .With(FrontButtonKind.CenterM, FrontButtonDomain.Steam, FrontButtonBinding.Of(FrontButtonAction.LaunchApplication) with
            {
                Launch = new FrontButtonLaunchApplicationBinding(@"C:\b.exe")
            });

        Assert.NotNull(FrontButtonMappingValidation.Validate(mapping));
    }

    [Theory]
    [InlineData(FrontButtonDomain.Normal)]
    [InlineData(FrontButtonDomain.Steam)]
    public void A_gamebar_win_g_hotkey_binding_is_rejected_at_the_validation_boundary(FrontButtonDomain domain)
    {
        var mapping = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.Gamebar, domain, FrontButtonBinding.Of(FrontButtonAction.KeyboardHotkey) with
            {
                Hotkey = new FrontButtonHotkeyBinding(FrontButtonHotkeyModifiers.Windows, FrontButtonHotkeyKey.G)
            });

        Assert.NotNull(FrontButtonMappingValidation.Validate(mapping));
    }

    [Fact]
    public void A_center_m_win_g_hotkey_binding_is_allowed()
    {
        var mapping = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.CenterM, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.KeyboardHotkey) with
            {
                Hotkey = new FrontButtonHotkeyBinding(FrontButtonHotkeyModifiers.Windows, FrontButtonHotkeyKey.G)
            });

        Assert.Null(FrontButtonMappingValidation.Validate(mapping));
    }

    [Fact]
    public void A_domain_invalid_action_is_rejected()
    {
        var mapping = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.CenterM, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.SteamButton));

        Assert.NotNull(FrontButtonMappingValidation.Validate(mapping));
    }

    [Fact]
    public void With_addresses_exactly_one_binding()
    {
        var mapping = FrontButtonMappingSettings.Default;
        var updated = mapping.With(FrontButtonKind.Gamebar, FrontButtonDomain.Steam, FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay));

        Assert.Equal(FrontButtonAction.QuickSettingsOverlay, updated.Resolve(FrontButtonKind.Gamebar, FrontButtonDomain.Steam).Action);
        Assert.Equal(mapping.Resolve(FrontButtonKind.CenterM, FrontButtonDomain.Steam), updated.Resolve(FrontButtonKind.CenterM, FrontButtonDomain.Steam));
        Assert.Equal(mapping.Normal, updated.Normal);
    }
}
