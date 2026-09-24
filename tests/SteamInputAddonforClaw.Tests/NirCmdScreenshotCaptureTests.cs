using System.Diagnostics;
using SteamInputAddonforClaw.Shortcuts;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class NirCmdScreenshotCaptureTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SteamInputAddonforClaw.NirCmdScreenshot.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void Null_folder_resolves_to_pictures_screenshots_and_custom_folder_is_preserved()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"),
            NirCmdScreenshotCapture.ResolveFolder(null));
        Assert.Equal(@"C:\User Pictures\Game Captures", NirCmdScreenshotCapture.ResolveFolder(@"C:\User Pictures\Game Captures"));
    }

    [Fact]
    public void Filename_uses_local_second_precision_and_jpeg_extension()
    {
        var timestamp = new DateTime(2026, 9, 26, 17, 37, 58, DateTimeKind.Local);

        var path = NirCmdScreenshotCapture.SelectOutputPath(
            @"C:\Captures", timestamp, _ => false);

        Assert.Equal(@"C:\Captures\20260926-173758.jpg", path);
    }

    [Fact]
    public void Filename_collision_uses_bounded_suffixes_without_overwriting()
    {
        var timestamp = new DateTime(2026, 9, 26, 17, 37, 58, DateTimeKind.Local);
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Captures\20260926-173758.jpg",
            @"C:\Captures\20260926-173758-01.jpg"
        };

        var path = NirCmdScreenshotCapture.SelectOutputPath(@"C:\Captures", timestamp, occupied.Contains);

        Assert.Equal(@"C:\Captures\20260926-173758-02.jpg", path);
        Assert.Equal(2, occupied.Count);
    }

    [Fact]
    public void Filename_collision_search_stops_after_base_and_99_suffixes()
    {
        var calls = 0;
        var path = NirCmdScreenshotCapture.SelectOutputPath(
            @"C:\Captures",
            new DateTime(2026, 9, 26, 17, 37, 58, DateTimeKind.Local),
            _ => { calls++; return true; });

        Assert.Null(path);
        Assert.Equal(100, calls);
    }

    [Fact]
    public async Task Successful_capture_uses_nircmdc_and_verifies_nonempty_jpeg()
    {
        Directory.CreateDirectory(_testDirectory);
        ProcessStartInfo? captured = null;
        var capture = CreateCapture((info, _) =>
        {
            captured = info;
            File.WriteAllBytes(info.ArgumentList[1], [0xFF, 0xD8, 0xFF]);
            return Task.FromResult(new NirCmdProcessResult(true, false, 0));
        });

        var result = await capture.CaptureAsync(_testDirectory);

        Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
        Assert.NotNull(captured);
        var expectedExecutable = Path.Combine(AppContext.BaseDirectory, "Dependencies", "NirCmd", "nircmdc.exe");
        Assert.Equal(expectedExecutable, captured!.FileName);
        Assert.False(captured.UseShellExecute);
        Assert.True(captured.CreateNoWindow);
        Assert.Equal(Path.GetDirectoryName(expectedExecutable), captured.WorkingDirectory);
        Assert.Equal(["savescreenshot", Assert.Single(captured.ArgumentList.Skip(1))], captured.ArgumentList);
        Assert.EndsWith(".jpg", captured.ArgumentList[1], StringComparison.OrdinalIgnoreCase);
        Assert.True(new FileInfo(captured.ArgumentList[1]).Length > 0);
    }

    [Theory]
    [InlineData(false, false, null)] // process could not be started
    [InlineData(true, true, null)]   // bounded timeout
    [InlineData(true, false, 1)]    // nonzero exit
    [InlineData(true, false, 0)]    // zero exit but no output file
    public async Task Failed_process_results_are_classified_without_leaking_paths(
        bool started,
        bool timedOut,
        int? exitCode)
    {
        Directory.CreateDirectory(_testDirectory);
        var capture = CreateCapture((_, _) => Task.FromResult(new NirCmdProcessResult(started, timedOut, exitCode)));

        var result = await capture.CaptureAsync(_testDirectory);

        Assert.Equal(ShortcutExecutionOutcome.Failed, result.Outcome);
        Assert.Equal("Screenshot could not be saved.", result.FailureMessage);
        Assert.DoesNotContain(_testDirectory, result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeout_and_zero_length_output_remove_only_the_attempt_file()
    {
        Directory.CreateDirectory(_testDirectory);
        string? partialPath = null;
        var capture = CreateCapture((info, _) =>
        {
            partialPath = info.ArgumentList[1];
            File.WriteAllBytes(partialPath, []);
            return Task.FromResult(new NirCmdProcessResult(true, true, null));
        });

        var result = await capture.CaptureAsync(_testDirectory);

        Assert.Equal(ShortcutExecutionOutcome.Failed, result.Outcome);
        Assert.NotNull(partialPath);
        Assert.False(File.Exists(partialPath));
    }

    [Fact]
    public async Task Zero_exit_with_a_zero_length_output_is_a_failure_and_removes_the_file()
    {
        Directory.CreateDirectory(_testDirectory);
        string? outputPath = null;
        var capture = CreateCapture((info, _) =>
        {
            outputPath = info.ArgumentList[1];
            File.WriteAllBytes(outputPath, []);
            return Task.FromResult(new NirCmdProcessResult(true, false, 0));
        });

        var result = await capture.CaptureAsync(_testDirectory);

        Assert.Equal(ShortcutExecutionOutcome.Failed, result.Outcome);
        Assert.NotNull(outputPath);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task Failed_capture_preserves_a_preexisting_screenshot_and_cleans_its_partial_file()
    {
        Directory.CreateDirectory(_testDirectory);
        string? preexistingPath = null;
        string? attemptedPath = null;
        var executablePath = Path.Combine(AppContext.BaseDirectory, "Dependencies", "NirCmd", "nircmdc.exe");
        var exists = true;
        bool FileExists(string path)
        {
            if (string.Equals(path, executablePath, StringComparison.OrdinalIgnoreCase)) return true;
            if (exists)
            {
                exists = false;
                preexistingPath = path;
                File.WriteAllBytes(path, [7, 8, 9]);
                return true;
            }
            return File.Exists(path);
        }

        var capture = new NirCmdScreenshotCapture((info, _) =>
        {
            attemptedPath = info.ArgumentList[1];
            File.WriteAllBytes(attemptedPath, [1, 2]);
            return Task.FromResult(new NirCmdProcessResult(true, false, 2));
        }, FileExists);

        await capture.CaptureAsync(_testDirectory);

        Assert.NotNull(preexistingPath);
        Assert.NotNull(attemptedPath);
        Assert.NotEqual(preexistingPath, attemptedPath);
        Assert.Equal([7, 8, 9], File.ReadAllBytes(preexistingPath));
        Assert.False(File.Exists(attemptedPath));
    }

    [Fact]
    public async Task Missing_nircmd_payload_is_unavailable_and_does_not_launch()
    {
        var launchCount = 0;
        var capture = new NirCmdScreenshotCapture((_, _) =>
        {
            launchCount++;
            return Task.FromResult(new NirCmdProcessResult(true, false, 0));
        }, _ => false);

        var result = await capture.CaptureAsync(_testDirectory);

        Assert.Equal(ShortcutExecutionOutcome.Unavailable, result.Outcome);
        Assert.Equal("Screenshot is unavailable.", result.FailureMessage);
        Assert.Equal(0, launchCount);
        Assert.False(Directory.Exists(_testDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Directory_creation_failure_does_not_launch_nircmd(bool unauthorized)
    {
        var launchCount = 0;
        var capture = new NirCmdScreenshotCapture(
            (_, _) =>
            {
                launchCount++;
                return Task.FromResult(new NirCmdProcessResult(true, false, 0));
            },
            _ => true,
            _ =>
            {
                if (unauthorized) throw new UnauthorizedAccessException("private path");
                throw new IOException("private path");
            });

        var result = await capture.CaptureAsync(_testDirectory);

        Assert.Equal(ShortcutExecutionOutcome.Failed, result.Outcome);
        Assert.Equal("Screenshot could not be saved.", result.FailureMessage);
        Assert.Equal(0, launchCount);
    }

    private NirCmdScreenshotCapture CreateCapture(
        Func<ProcessStartInfo, CancellationToken, Task<NirCmdProcessResult>> runProcess)
    {
        var executablePath = Path.Combine(AppContext.BaseDirectory, "Dependencies", "NirCmd", "nircmdc.exe");
        return new NirCmdScreenshotCapture(
            runProcess,
            path => string.Equals(path, executablePath, StringComparison.OrdinalIgnoreCase) || File.Exists(path));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
        }
        catch
        {
            // Isolated test data is best-effort cleaned after assertions complete.
        }
    }
}
