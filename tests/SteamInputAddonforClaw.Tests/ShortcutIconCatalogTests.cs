using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ShortcutIconCatalogTests
{
    [Theory]
    [InlineData(FrontendShortcutEditorActionKind.ScreenshotFullscreen, "\uE722")]
    [InlineData(FrontendShortcutEditorActionKind.SteamClient, "\uE7FC")]
    [InlineData(FrontendShortcutEditorActionKind.SteamBigPicture, "\uE7F4")]
    [InlineData(FrontendShortcutEditorActionKind.XboxApp, "\uE990")]
    [InlineData(FrontendShortcutEditorActionKind.Executable, "\uE71D")]
    [InlineData(FrontendShortcutEditorActionKind.PowerShell, "\uE756")]
    [InlineData(FrontendShortcutEditorActionKind.Url, "\uE774")]
    public void Action_defaults_match_the_canonical_glyphs(
        FrontendShortcutEditorActionKind kind,
        string expectedGlyph)
    {
        Assert.Equal(expectedGlyph, ShortcutIconCatalog.GetDefaultGlyph(kind));
        Assert.Equal(expectedGlyph, ShortcutIconCatalog.GetEffectiveGlyph(kind, null));
    }

    [Fact]
    public void Curated_choices_have_unique_names_and_single_supported_glyphs()
    {
        Assert.InRange(ShortcutIconCatalog.Choices.Count, 24, 40);
        Assert.Equal(ShortcutIconCatalog.Choices.Count,
            ShortcutIconCatalog.Choices.Select(choice => choice.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ShortcutIconCatalog.Choices.Count,
            ShortcutIconCatalog.Choices.Select(choice => choice.Glyph).Distinct(StringComparer.Ordinal).Count());

        Assert.All(ShortcutIconCatalog.Choices, choice =>
        {
            Assert.False(string.IsNullOrWhiteSpace(choice.Name));
            Assert.Equal(1, choice.Glyph.Length);
            Assert.True(ShortcutIconCatalog.IsKnownGlyph(choice.Glyph));
            Assert.Equal(choice.Name, ShortcutIconCatalog.GetEffectiveName(
                FrontendShortcutEditorActionKind.Unsupported, choice.Glyph));
        });
    }

    [Fact]
    public void Invalid_override_uses_action_default_and_unsupported_action_uses_generic_default()
    {
        Assert.Equal("\uE7FC", ShortcutIconCatalog.GetEffectiveGlyph(
            FrontendShortcutEditorActionKind.SteamClient, "not-a-glyph"));
        Assert.Equal(ShortcutIconCatalog.FallbackGlyph, ShortcutIconCatalog.GetDefaultGlyph(
            FrontendShortcutEditorActionKind.Unsupported));
        Assert.Equal(ShortcutIconCatalog.FallbackGlyph, ShortcutIconCatalog.GetEffectiveGlyph(
            FrontendShortcutEditorActionKind.Unsupported, null));
    }
}
