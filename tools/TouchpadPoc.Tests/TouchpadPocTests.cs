using System.Runtime.InteropServices;
using System.Text.Json;
using TouchpadPoc;
using Xunit;

namespace TouchpadPoc.Tests;

public sealed class TouchpadPocTests
{
    [Fact]
    public void BottomRightQuarter_UsesPhysicalPixelBounds()
    {
        var zone = PixelRect.BottomRightQuarter(new PixelRect(0, 0, 1920, 1200));

        Assert.Equal(new PixelRect(960, 600, 960, 600), zone);
        Assert.True(zone.Contains(960, 600));
        Assert.True(zone.Contains(1919, 1199));
        Assert.False(zone.Contains(959, 600));
        Assert.False(zone.Contains(960, 599));
        Assert.False(zone.Contains(1920, 1200));
    }

    [Fact]
    public void BottomRightQuarter_CoversRemainderForOddBoundsAndNonZeroOrigin()
    {
        var bounds = new PixelRect(100, -200, 1919, 1199);
        var zone = PixelRect.BottomRightQuarter(bounds);

        Assert.Equal(new PixelRect(1059, 399, 960, 600), zone);
        Assert.Equal(bounds.Right, zone.Right);
        Assert.Equal(bounds.Bottom, zone.Bottom);
    }

    [Fact]
    public void ToLocal_MapsPhysicalScreenCoordinatesWithoutDpiScaling()
    {
        var zone = PixelRect.BottomRightQuarter(new PixelRect(0, 0, 1920, 1200));

        Assert.Equal(new PixelPoint(0, 0), zone.ToLocal(960, 600));
        Assert.Equal(new PixelPoint(959, 599), zone.ToLocal(1919, 1199));
    }

    [Fact]
    public void PointerTracker_DoesNotAcquireAContactThatStartedOutside()
    {
        var tracker = new PointerContactTracker();

        Assert.True(tracker.Begin(14, startsInside: false, out var acquired));
        Assert.False(acquired);
        Assert.True(tracker.IsKnown(14));
        Assert.False(tracker.IsAcquired(14));
        Assert.False(tracker.IsAcquired(14));
        Assert.True(tracker.End(14, out acquired));
        Assert.False(acquired);
        Assert.False(tracker.IsKnown(14));
    }

    [Fact]
    public void PointerTracker_TracksTwoContactsAndClearsMissingUpOnCaptureLoss()
    {
        var tracker = new PointerContactTracker();

        Assert.True(tracker.Begin(21, startsInside: true, out var firstAcquired));
        Assert.True(firstAcquired);
        Assert.True(tracker.Begin(22, startsInside: true, out var secondAcquired));
        Assert.True(secondAcquired);
        Assert.Equal(2, tracker.AcquiredCount);

        Assert.True(tracker.End(21, out var captureLostContact));
        Assert.True(captureLostContact);
        Assert.Equal(1, tracker.AcquiredCount);
        Assert.False(tracker.End(21, out _));

        var remaining = tracker.Clear();
        Assert.Equal(new[] { (22u, true) }, remaining);
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void PointerTracker_DuplicateDownDoesNotReplaceOriginalContact()
    {
        var tracker = new PointerContactTracker();

        Assert.True(tracker.Begin(31, startsInside: false, out _));
        Assert.False(tracker.Begin(31, startsInside: true, out var acquired));

        Assert.False(acquired);
        Assert.Equal(0, tracker.AcquiredCount);
    }

    [Fact]
    public void LogFormatter_ProducesSingleJsonLineAndEscapesErrorText()
    {
        var logEvent = new PocLogEvent
        {
            Sequence = 3,
            Event = "Error",
            Message = "first line\r\n\"quoted\""
        };

        var formatted = PocLogFormatter.Format(logEvent);
        using var document = JsonDocument.Parse(formatted);

        Assert.Equal(3, document.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal("Error", document.RootElement.GetProperty("event").GetString());
        Assert.Equal("first line\r\n\"quoted\"", document.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain('\n', formatted);
    }

    [Fact]
    public void NativeStructureLayouts_MatchX64Win32()
    {
        Assert.Equal(8, Marshal.SizeOf<NativeMethods.NativePoint>());
        Assert.Equal(16, Marshal.SizeOf<NativeMethods.NativeRect>());
        Assert.Equal(40, Marshal.SizeOf<NativeMethods.MonitorInfo>());
        Assert.Equal(96, Marshal.SizeOf<NativeMethods.PointerInfo>());
        Assert.Equal(80, Marshal.SizeOf<NativeMethods.WindowClassEx>());
        Assert.Equal(48, Marshal.SizeOf<NativeMethods.NativeMessage>());
    }

    [Fact]
    public void SessionHasTheRequiredNinetySecondMaximum()
    {
        Assert.Equal(90, TouchCaptureWindow.DefaultDurationSeconds);
    }

    [Fact]
    public void CandidateStyles_DoNotUseClickThroughOrLayeredWindowFlags()
    {
        Assert.Equal(0u, NativeMethods.RequestedExtendedStyle & 0x00000020u);
        Assert.Equal(0u, NativeMethods.RequestedExtendedStyle & 0x00080000u);
        Assert.Equal(3, NativeMethods.MaNoActivate);
        Assert.Equal(3, NativeMethods.PaNoActivate);
    }
}
