using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ShortcutIconCatalogTests
{
    [Fact]
    public void Action_defaults_and_curated_choices_resolve_to_valid_icon_data()
    {
        Assert.NotEmpty(ShortcutIconCatalog.Choices);
        Assert.Equal(ShortcutIconCatalog.Choices.Count,
            ShortcutIconCatalog.Choices.Select(choice => choice.Glyph).Distinct(StringComparer.Ordinal).Count());
        Assert.All(ShortcutIconCatalog.Choices, choice =>
        {
            Assert.False(string.IsNullOrWhiteSpace(choice.Name));
            Assert.False(string.IsNullOrWhiteSpace(choice.Glyph));
            Assert.True(ShortcutIconCatalog.IsKnownGlyph(choice.Glyph));
            Assert.True(ShortcutIconCatalog.TryGetChoice(choice.Glyph, out var resolved));
            Assert.Equal(choice, resolved);
            Assert.Equal(choice.Name, ShortcutIconCatalog.GetEffectiveName(
                FrontendShortcutEditorActionKind.Unsupported, choice.Glyph));
        });

        foreach (var kind in Enum.GetValues<FrontendShortcutEditorActionKind>())
        {
            var glyph = ShortcutIconCatalog.GetDefaultGlyph(kind);
            Assert.False(string.IsNullOrWhiteSpace(glyph));
            Assert.True(ShortcutIconCatalog.IsKnownGlyph(glyph));
            Assert.Equal(glyph, ShortcutIconCatalog.GetEffectiveGlyph(kind, null));
        }
    }

    [Fact]
    public void User_override_is_preserved_and_invalid_override_falls_back_to_a_valid_default()
    {
        var selected = ShortcutIconCatalog.Choices[1];
        Assert.Equal(selected.Glyph, ShortcutIconCatalog.GetEffectiveGlyph(
            FrontendShortcutEditorActionKind.SteamClient, selected.Glyph));
        Assert.Equal(selected.Name, ShortcutIconCatalog.GetEffectiveName(
            FrontendShortcutEditorActionKind.SteamClient, selected.Glyph));

        var defaultGlyph = ShortcutIconCatalog.GetDefaultGlyph(FrontendShortcutEditorActionKind.SteamClient);
        Assert.Equal(defaultGlyph, ShortcutIconCatalog.GetEffectiveGlyph(
            FrontendShortcutEditorActionKind.SteamClient, "invalid"));
        Assert.True(ShortcutIconCatalog.IsKnownGlyph(ShortcutIconCatalog.FallbackGlyph));
        Assert.Equal(ShortcutIconCatalog.FallbackGlyph, ShortcutIconCatalog.GetEffectiveGlyph(
            FrontendShortcutEditorActionKind.Unsupported, null));
    }
}
