using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.GameDetection.Windows;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WindowsGameDetectionFoundationTests
{
    [Theory]
    [InlineData(0x8000, 0, 0, 123, true, true)]
    [InlineData(0x8002, 0, 0, 123, true, true)]
    [InlineData(0x0003, -1, -1, 123, true, true)]
    [InlineData(0x8000, 1, 0, 123, true, false)]
    [InlineData(0x8002, 0, 1, 123, true, false)]
    [InlineData(0x8000, 0, 0, 0, true, false)]
    [InlineData(0x8000, 0, 0, 123, false, false)]
    [InlineData(0x8001, 0, 0, 123, true, false)]
    public void WinEvent_filter_requires_allowed_event_top_level_window_and_self_object(
        int eventType, int objectId, int childId, int window, bool isTopLevel, bool expected)
    {
        Assert.Equal(expected, WindowsGameWindowEventSource.IsRelevantObservation(
            checked((uint)eventType), objectId, childId, window, isTopLevel));
    }

    [Fact]
    public void Bounded_top_level_window_enumeration_returns_unique_pids_within_limit()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var processIds = new WindowsGameWindowEventSource().EnumerateTopLevelProcessIds(8);

        Assert.InRange(processIds.Count, 0, 8);
        Assert.Equal(processIds.Count, processIds.Distinct().Count());
    }

    [Fact]
    public void Process_generation_key_uses_pid_and_creation_time()
    {
        var first = new GameProcessGenerationKey(42, 100);
        var sameGeneration = new GameProcessGenerationKey(42, 100);
        var reusedPid = new GameProcessGenerationKey(42, 101);

        Assert.Equal(first, sameGeneration);
        Assert.NotEqual(first, reusedPid);
    }

    [Fact]
    public void Current_process_generation_queries_and_caches_its_image_path_and_disposes_idempotently()
    {
        var source = new WindowsGameProcessSource();
        var opened = source.Open(checked((uint)Environment.ProcessId));
        var generation = Assert.IsType<WindowsGameProcessGeneration>(opened.Generation);

        Assert.Equal(0, opened.ErrorCode);
        Assert.Equal(checked((uint)Environment.ProcessId), generation.ProcessId);
        Assert.Equal(generation.ProcessId, generation.Key.ProcessId);

        var first = generation.QueryImagePath();
        var second = generation.QueryImagePath();

        Assert.True(first.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(first.ImagePath));
        Assert.Equal(first, second);

        generation.Dispose();
        generation.Dispose();
    }

    [Fact]
    public void Image_path_query_failure_preserves_the_native_error_code()
    {
        using var invalidHandle = new SafeProcessHandle(IntPtr.Zero, ownsHandle: false);

        var result = WindowsGameProcessGeneration.QueryImagePath(invalidHandle);

        Assert.False(result.Succeeded);
        Assert.Null(result.ImagePath);
        Assert.NotEqual(0, result.ErrorCode);
    }

    [Fact]
    public void Production_process_wait_is_registered_as_one_shot_and_image_path_cache_is_generation_local()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "GameDetection", "Windows", "WindowsGameProcess.cs"));

        Assert.Contains("ThreadPool.RegisterWaitForSingleObject", source, StringComparison.Ordinal);
        Assert.Contains("executeOnlyOnce: true", source, StringComparison.Ordinal);
        Assert.Contains("GameProcessImageQueryResult? _imagePathQueryResult", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_windows_foundation_contains_no_platform_identity_or_profile_logic()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "GameDetection", "Windows");
        var source = string.Join("\n", Directory.GetFiles(root, "*.cs").Select(File.ReadAllText));
        var domainSource = source.Replace("SteamInputAddonforClaw.", "<assembly-root>.", StringComparison.Ordinal);

        foreach (var forbidden in new[]
        {
            "Xbox", "MicrosoftGame.config", "GetPackageFullName", "GetPackageFamilyName", "PackageManager",
            "Steam", "Epic", "GOG", "Custom", "Profile",
        })
        {
            Assert.DoesNotContain(forbidden, domainSource, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
