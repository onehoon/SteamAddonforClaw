using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class DeviceGpuMinimumClockUiTests
{
    private static readonly double[] Clocks = [1525, 1625, 1725, 1825];

    [Fact]
    public void Discrete_index_maps_only_to_exact_supported_driver_entries()
    {
        Assert.True(DevicePage.GpuMinimumClockDraftPolicy.TryGetClockAtIndex(Clocks, 0, out var first));
        Assert.Equal(1525, first);
        Assert.True(DevicePage.GpuMinimumClockDraftPolicy.TryGetClockAtIndex(Clocks, 3, out var last));
        Assert.Equal(1825, last);
        Assert.False(DevicePage.GpuMinimumClockDraftPolicy.TryGetClockAtIndex(Clocks, 4, out _));
        Assert.False(DevicePage.GpuMinimumClockDraftPolicy.TryGetClockAtIndex(Clocks, double.NaN, out _));
    }

    [Fact]
    public void Uninitialized_draft_uses_runtime_recommended_default_without_claiming_a_saved_value()
    {
        Assert.Equal(1725, DevicePage.GpuMinimumClockDraftPolicy.ResolveDraftMhz(Clocks, null, 1725));
        Assert.False(DevicePage.GpuMinimumClockDraftPolicy.IsSupported(Clocks, null));
    }

    [Fact]
    public void Unsupported_saved_value_is_not_presented_as_valid_and_requires_explicit_commit()
    {
        Assert.False(DevicePage.GpuMinimumClockDraftPolicy.IsSupported(Clocks, 1777));
        Assert.Equal(1725, DevicePage.GpuMinimumClockDraftPolicy.ResolveDraftMhz(Clocks, 1777, 1725));
        Assert.False(DevicePage.GpuMinimumClockDraftPolicy.ShouldCommit(Clocks, 1725, 1777, dirty: false, enabled: true, busy: false));
        Assert.True(DevicePage.GpuMinimumClockDraftPolicy.ShouldCommit(Clocks, 1725, 1777, dirty: true, enabled: true, busy: false));
        Assert.False(DevicePage.GpuMinimumClockDraftPolicy.ShouldCommit(Clocks, 1725, 1777, dirty: true, enabled: false, busy: false));
    }

    [Fact]
    public void Device_sliders_are_discrete_and_value_changed_only_updates_a_draft()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        var valueChanged = ExtractMethod(code, "private void GpuMinimumClockSlider_ValueChanged(");
        var enabledState = ExtractMethod(code, "private void UpdateGpuMinimumClockControlEnabledState()");

        foreach (var name in new[] { "GpuMinimumClockAcSlider", "GpuMinimumClockDcSlider" })
        {
            var slider = xaml[xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal)..];
            slider = slider[..slider.IndexOf(" />", StringComparison.Ordinal)];
            Assert.Contains("Minimum=\"0\"", slider, StringComparison.Ordinal);
            Assert.Contains("Maximum=\"0\"", slider, StringComparison.Ordinal);
            Assert.Contains("StepFrequency=\"1\"", slider, StringComparison.Ordinal);
        }

        Assert.Contains("Maximum = Math.Max(0, clocks.Count - 1)", code, StringComparison.Ordinal);
        Assert.Contains("StepFrequency = 1", code, StringComparison.Ordinal);
        Assert.Contains("_gpuMinimumClockAcDraftMhz = mhz", valueChanged, StringComparison.Ordinal);
        Assert.Contains("_gpuMinimumClockDcDraftMhz = mhz", valueChanged, StringComparison.Ordinal);
        Assert.DoesNotContain("SetDeviceGpuMinimumClock", valueChanged, StringComparison.Ordinal);
        Assert.Contains("editable && _gpuMinimumClockSnapshot.Enabled", enabledState, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var openBrace = source.IndexOf('{', start);
        var depth = 0;
        var index = openBrace;
        for (; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) break;
        }
        return source[start..(index + 1)];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
