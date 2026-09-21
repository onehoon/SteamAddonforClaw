using SteamInputAddonforClaw.ClawHud;
using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ClawHudFrontendMappingTests
{
    [Fact]
    public void MutationIntent_RejectsUnexpectedFieldsAndOutOfRangeValues()
    {
        Assert.False(new FrontendClawHudMutationIntent(
            FrontendClawHudMutationKind.DisplayMode,
            DisplayMode: FrontendClawHudDisplayMode.Always,
            Font: FrontendClawHudFont.Unispace).TryValidate(out _));
        Assert.False(new FrontendClawHudMutationIntent(
            FrontendClawHudMutationKind.HudSizeOffset,
            HudSizeOffset: 3).TryValidate(out _));
        Assert.False(new FrontendClawHudMutationIntent(
            FrontendClawHudMutationKind.CommitOpacity,
            OpacityPercent: 52).TryValidate(out _));
    }

    [Fact]
    public void MutationIntent_MapsEachClosedSettingToTheExpectedControlOperation()
    {
        var cases = new (FrontendClawHudMutationIntent Intent, ClawHudControlOperation Operation)[]
        {
            (new(FrontendClawHudMutationKind.DisplayMode, DisplayMode: FrontendClawHudDisplayMode.InGameOnly), ClawHudControlOperation.SetHudVisibilityMode),
            (new(FrontendClawHudMutationKind.HudSizeOffset, HudSizeOffset: -1), ClawHudControlOperation.SetHudSizeOffset),
            (new(FrontendClawHudMutationKind.Font, Font: FrontendClawHudFont.SegoeUiVariable), ClawHudControlOperation.SetHudFont),
            (new(FrontendClawHudMutationKind.Alignment, Alignment: FrontendClawHudAlignment.Center), ClawHudControlOperation.SetHudAlignment),
            (new(FrontendClawHudMutationKind.BackgroundMode, BackgroundMode: FrontendClawHudBackgroundMode.ContentWidth), ClawHudControlOperation.SetHudBackgroundMode),
            (new(FrontendClawHudMutationKind.PreviewOpacity, OpacityPercent: 75), ClawHudControlOperation.PreviewHudOpacity),
            (new(FrontendClawHudMutationKind.CommitOpacity, OpacityPercent: 80), ClawHudControlOperation.CommitHudOpacity),
            (new(FrontendClawHudMutationKind.IntelVrrRangeFixEnabled, IntelVrrRangeFixEnabled: true), ClawHudControlOperation.SetIntelVrrRangeFixEnabled),
        };

        foreach (var (intent, expected) in cases)
        {
            Assert.True(ClawHudFrontendMapping.TryCreateRequest(intent, out var request, out var failure), failure);
            Assert.Equal(expected, request.Operation);
            Assert.Equal((uint)1, request.RequestId);
        }
    }

    [Fact]
    public void SettingsMapping_PreservesAllSupportedValuesAndVrrResult()
    {
        var source = new ClawHudSettingsSnapshot(
            StartWithWindows: true,
            HudEnabled: true,
            HudSizeOffset: 2,
            HudFont: ClawHudWireFont.SegoeUiVariable,
            VisibilityMode: ClawHudWireVisibilityMode.InGameOnly,
            Alignment: ClawHudWireAlignment.Right,
            BackgroundMode: ClawHudWireBackgroundMode.ContentWidth,
            BackgroundOpacityPercent: 85,
            IntelVrrRangeFixEnabled: true,
            IntelVrrLastResult: new(ClawHudWireIntelVrrStatus.VerificationFailed, "Panel", "48-120", "48-120", "failed", "2026-09-22T00:00:00Z"));

        Assert.True(ClawHudFrontendMapping.TryMapSettings(source, out var mapped));
        Assert.Equal(FrontendClawHudDisplayMode.InGameOnly, mapped.DisplayMode);
        Assert.Equal(2, mapped.HudSizeOffset);
        Assert.Equal(FrontendClawHudFont.SegoeUiVariable, mapped.Font);
        Assert.Equal(FrontendClawHudAlignment.Right, mapped.Alignment);
        Assert.Equal(FrontendClawHudBackgroundMode.ContentWidth, mapped.BackgroundMode);
        Assert.Equal(85, mapped.BackgroundOpacityPercent);
        Assert.True(mapped.IntelVrrRangeFixEnabled);
        Assert.Equal(FrontendClawHudIntelVrrStatus.VerificationFailed, mapped.IntelVrrLastResult?.Status);
    }
}
