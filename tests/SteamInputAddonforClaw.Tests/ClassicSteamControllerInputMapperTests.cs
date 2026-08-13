using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ClassicSteamControllerInputMapperTests
{
    [Fact]
    public void Centered_stick_without_click_is_untouched()
    {
        var mapped = Map(0, 0, click: false);
        Assert.False(mapped.RightPadTouch);
        Assert.False(mapped.RightPadPress);
    }

    [Fact]
    public void Existing_center_quantization_remains_safe()
    {
        var mapped = Map(1, -1, click: false);
        Assert.False(mapped.RightPadTouch);
    }

    [Theory]
    [InlineData((short)500, false)]
    [InlineData((short)501, true)]
    [InlineData((short)-500, false)]
    [InlineData((short)-501, true)]
    public void X_axis_touch_activation_boundary(short x, bool expectedTouch)
    {
        var mapped = Map(x, 0, click: false);
        Assert.Equal(expectedTouch, mapped.RightPadTouch);
    }

    [Theory]
    [InlineData((short)500, false)]
    [InlineData((short)501, true)]
    public void Y_axis_touch_activation_boundary(short y, bool expectedTouch)
    {
        var mapped = Map(0, y, click: false);
        Assert.Equal(expectedTouch, mapped.RightPadTouch);
    }

    [Fact]
    public void Threshold_is_per_axis_not_radial()
    {
        // Both axes sit exactly at the threshold (not beyond it); a radial/vector-length
        // threshold would report touch here, but the per-axis rule must not.
        var mapped = Map(500, 500, click: false);
        Assert.False(mapped.RightPadTouch);
    }

    [Fact]
    public void RightStickClick_at_center_reports_press_and_touch_in_the_gordon_report()
    {
        var mapped = Map(0, 0, click: true);
        Assert.True(mapped.RightPadPress);
        Assert.True(mapped.RightPadTouch);

        var report = new byte[64];
        ClassicSteamControllerReportBuilder.Write(report, 0, mapped);
        Assert.Equal(0x04, report[10] & 0x04);
        Assert.Equal(0x10, report[10] & 0x10);
    }

    [Fact]
    public void Movement_past_threshold_without_click_touches_but_does_not_press()
    {
        var mapped = Map(501, 0, click: false);
        Assert.False(mapped.RightPadPress);
        Assert.True(mapped.RightPadTouch);
    }

    [Fact]
    public void Coordinates_below_threshold_are_preserved_exactly_and_untouched()
    {
        var mapped = Map(499, 0, click: false);
        Assert.False(mapped.RightPadTouch);

        var report = new byte[64];
        ClassicSteamControllerReportBuilder.Write(report, 0, mapped);
        Assert.Equal(499, BitConverter.ToInt16(report, 20));
    }

    [Fact]
    public void Coordinates_above_threshold_are_preserved_exactly_and_touched()
    {
        var mapped = Map(501, 0, click: false);
        Assert.True(mapped.RightPadTouch);

        var report = new byte[64];
        ClassicSteamControllerReportBuilder.Write(report, 0, mapped);
        Assert.Equal(501, BitConverter.ToInt16(report, 20));
    }

    [Fact]
    public void Negative_coordinates_are_preserved_exactly()
    {
        var mapped = Map(-501, 0, click: false);

        var report = new byte[64];
        ClassicSteamControllerReportBuilder.Write(report, 0, mapped);
        Assert.Equal(-501, BitConverter.ToInt16(report, 20));
    }

    private static ClassicSteamControllerInput Map(short x, short y, bool click)
    {
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, false, click, false, false);
        var state = new ControllerState(buttons, default, new StickState(x, y), default, new([false, false]));
        return ClassicSteamControllerInputMapper.Map(state);
    }
}
