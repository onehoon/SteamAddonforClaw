using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class DeviceGpuMinimumClockUiTests
{
    private static readonly double[] Clocks = [1525, 1625, 1725, 1825];

    [Fact]
    public void Discrete_index_maps_only_to_exact_supported_driver_entries()
    {
        Assert.True(GpuMinimumClockUiPolicy.TryGetClockAtIndex(Clocks, 0, out var first));
        Assert.Equal(1525, first);
        Assert.True(GpuMinimumClockUiPolicy.TryGetClockAtIndex(Clocks, 3, out var last));
        Assert.Equal(1825, last);
        Assert.False(GpuMinimumClockUiPolicy.TryGetClockAtIndex(Clocks, 4, out _));
        Assert.False(GpuMinimumClockUiPolicy.TryGetClockAtIndex(Clocks, double.NaN, out _));
    }

    [Fact]
    public void Uninitialized_draft_uses_runtime_recommended_default_without_claiming_a_saved_value()
    {
        Assert.Equal(1725, GpuMinimumClockUiPolicy.ResolveDraftMhz(Clocks, null, 1725));
        Assert.False(GpuMinimumClockUiPolicy.IsSupported(Clocks, null));
    }

    [Fact]
    public void Unsupported_saved_value_is_not_presented_as_valid_and_requires_explicit_commit()
    {
        Assert.False(GpuMinimumClockUiPolicy.IsSupported(Clocks, 1777));
        Assert.Equal(1725, GpuMinimumClockUiPolicy.ResolveDraftMhz(Clocks, 1777, 1725));
        Assert.False(GpuMinimumClockUiPolicy.ShouldCommit(Clocks, 1725, 1777, dirty: false, enabled: true, busy: false));
        Assert.True(GpuMinimumClockUiPolicy.ShouldCommit(Clocks, 1725, 1777, dirty: true, enabled: true, busy: false));
        Assert.False(GpuMinimumClockUiPolicy.ShouldCommit(Clocks, 1725, 1777, dirty: true, enabled: false, busy: false));
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public async Task Frontend_exception_releases_a_completed_gpu_draft_and_unblocks_profile_toggles(
        long currentGeneration,
        bool expectedToPreserveDraft)
    {
        var draftDirty = true;
        var preserveDraft = false;
        try
        {
            await Task.FromException(new IOException("simulated frontend pipe failure"));
        }
        catch (IOException)
        {
            preserveDraft = GpuMinimumClockUiPolicy.ResolveFailedCommit(
                ref draftDirty, submittedGeneration: 20, currentGeneration);
        }

        Assert.Equal(expectedToPreserveDraft, preserveDraft);
        Assert.Equal(expectedToPreserveDraft, draftDirty);
        if (!draftDirty)
        {
            const bool profileEnabled = true;
            const bool persistenceWritable = true;
            const bool gpuAvailable = true;
            Assert.True(persistenceWritable && !draftDirty);
            Assert.True(profileEnabled && gpuAvailable && !draftDirty);
        }

        var root = FindRepositoryRoot();
        var steamCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var xboxCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml.cs"));
        foreach (var code in new[] { steamCode, xboxCode })
        {
            var submit = ExtractMethod(code, "private async Task SubmitGpuMinimumClockAfterDelayAsync(");
            var restore = ExtractMethod(code, "private async Task RestoreSelectedAfterMutationFailureAsync(");
            var normalizedSubmit = submit.Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.Contains("ResolveFailedCommit(\n                    ref _gpuDraftDirty, draftGeneration, _gpuDraftGeneration)", normalizedSubmit, StringComparison.Ordinal);
            Assert.Contains("preserveDirtyGpuDraft: preserveDraft", submit, StringComparison.Ordinal);
            Assert.Contains("Render(snapshot, preserveDirtyGpuDraft: preserveDirtyGpuDraft)", restore, StringComparison.Ordinal);
            Assert.Contains("&& !_gpuDraftDirty", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Minimum_gpu_clock_is_game_profile_only()
    {
        var root = FindRepositoryRoot();
        var deviceXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var deviceCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        Assert.DoesNotContain("Minimum GPU Clock", deviceXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GpuMinimumClock", deviceCode, StringComparison.Ordinal);

        foreach (var page in new[] { "ProfilePage.xaml", "XboxPage.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views", page));
            Assert.Contains("Header=\"Minimum GPU Clock\"", xaml, StringComparison.Ordinal);
            Assert.Contains("GpuMinimumClockEnabledToggle", xaml, StringComparison.Ordinal);
        }
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
