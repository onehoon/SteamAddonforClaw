using System.Diagnostics;
using System.Globalization;
using System.Security;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Shortcuts;

internal readonly record struct NirCmdProcessResult(bool Started, bool TimedOut, int? ExitCode);

/// <summary>Runs one bounded NirCmd full-primary-display JPEG capture.</summary>
internal sealed class NirCmdScreenshotCapture
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(5);
    private readonly Func<ProcessStartInfo, CancellationToken, Task<NirCmdProcessResult>> _runProcess;
    private readonly Func<string, bool> _fileExists;
    private readonly Action<string> _createDirectory;

    internal NirCmdScreenshotCapture(
        Func<ProcessStartInfo, CancellationToken, Task<NirCmdProcessResult>>? runProcess = null,
        Func<string, bool>? fileExists = null,
        Action<string>? createDirectory = null)
    {
        _runProcess = runProcess ?? RunProcessAsync;
        _fileExists = fileExists ?? File.Exists;
        _createDirectory = createDirectory ?? (path => Directory.CreateDirectory(path));
    }

    internal async Task<ShortcutExecutionResult> CaptureAsync(
        string? saveFolder,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var usedDefaultFolder = string.IsNullOrWhiteSpace(saveFolder);
        string? outputPath = null;
        var captureSucceeded = false;
        int? exitCode = null;
        var collisionSuffixUsed = false;
        Exception? failure = null;
        ShortcutExecutionResult result;

        try
        {
            var executablePath = Path.Combine(AppContext.BaseDirectory, "Dependencies", "NirCmd", "nircmdc.exe");
            if (!_fileExists(executablePath))
            {
                result = new(ShortcutExecutionOutcome.Unavailable, "Screenshot is unavailable.");
                return LogResult(result, usedDefaultFolder, collisionSuffixUsed, stopwatch, exitCode);
            }

            var folder = ResolveFolder(usedDefaultFolder ? null : saveFolder);
            if (!Path.IsPathFullyQualified(folder))
                throw new ArgumentException("Screenshot folder must be fully qualified.");

            folder = Path.GetFullPath(folder);
            _createDirectory(folder);

            outputPath = SelectOutputPath(folder, DateTime.Now, _fileExists);
            if (outputPath is null)
            {
                result = new(ShortcutExecutionOutcome.Failed, "Screenshot could not be saved.");
                return LogResult(result, usedDefaultFolder, collisionSuffixUsed, stopwatch, exitCode);
            }

            collisionSuffixUsed = Path.GetFileNameWithoutExtension(outputPath).Length > 15;

            var startInfo = CreateStartInfo(executablePath, outputPath);
            var processResult = await _runProcess(startInfo, cancellationToken).ConfigureAwait(false);
            exitCode = processResult.ExitCode;

            if (!processResult.Started || processResult.TimedOut || processResult.ExitCode != 0)
            {
                result = new(ShortcutExecutionOutcome.Failed, "Screenshot could not be saved.");
                return LogResult(result, usedDefaultFolder, collisionSuffixUsed, stopwatch, exitCode);
            }

            var outputExists = File.Exists(outputPath);
            var outputHasContent = outputExists && new FileInfo(outputPath).Length > 0;
            captureSucceeded = outputHasContent;
            result = outputHasContent
                ? new(ShortcutExecutionOutcome.Succeeded)
                : new(ShortcutExecutionOutcome.Failed, "Screenshot could not be saved.");
            return LogResult(result, usedDefaultFolder, collisionSuffixUsed, stopwatch, exitCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                          or NotSupportedException or SecurityException or InvalidOperationException
                                          or System.ComponentModel.Win32Exception)
        {
            failure = exception;
            result = new(ShortcutExecutionOutcome.Failed, "Screenshot could not be saved.");
            return LogResult(result, usedDefaultFolder, collisionSuffixUsed, stopwatch, exitCode, failure);
        }
        catch (Exception exception)
        {
            failure = exception;
            result = new(ShortcutExecutionOutcome.Failed, "Screenshot could not be saved.");
            return LogResult(result, usedDefaultFolder, collisionSuffixUsed, stopwatch, exitCode, failure);
        }
        finally
        {
            if (!captureSucceeded && outputPath is not null)
                TryDeletePartialOutput(outputPath);
        }
    }

    internal static string ResolveFolder(string? saveFolder)
    {
        if (!string.IsNullOrWhiteSpace(saveFolder)) return saveFolder;

        var pictures = Environment.GetFolderPath(
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(pictures)) return string.Empty;
        return Path.Combine(pictures, "Screenshots");
    }

    internal static string? SelectOutputPath(string folder, DateTime localNow, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(fileExists);

        var timestamp = localNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var basePath = Path.Combine(folder, timestamp + ".jpg");
        if (!fileExists(basePath)) return basePath;

        for (var suffix = 1; suffix <= 99; suffix++)
        {
            var candidate = Path.Combine(folder, $"{timestamp}-{suffix:00}.jpg");
            if (!fileExists(candidate)) return candidate;
        }

        return null;
    }

    internal static ProcessStartInfo CreateStartInfo(string executablePath, string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty
        };
        startInfo.ArgumentList.Add("savescreenshot");
        startInfo.ArgumentList.Add(outputPath);
        return startInfo;
    }

    private static async Task<NirCmdProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo);
        if (process is null) return new(false, false, null);

        return await WaitForOwnedProcessAsync(
            token => process.WaitForExitAsync(token),
            () => process.ExitCode,
            () => TryKill(process),
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<NirCmdProcessResult> WaitForOwnedProcessAsync(
        Func<CancellationToken, Task> waitForExit,
        Func<int> readExitCode,
        Action killOwnedProcess,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(waitForExit);
        ArgumentNullException.ThrowIfNull(readExitCode);
        ArgumentNullException.ThrowIfNull(killOwnedProcess);

        void TryStopOwnedProcess()
        {
            try { killOwnedProcess(); }
            catch { /* Best-effort stop; preserve the original cancellation or process result. */ }
        }

        try
        {
            await waitForExit(cancellationToken)
                .WaitAsync(ProcessTimeout, cancellationToken)
                .ConfigureAwait(false);
            return new(true, false, readExitCode());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryStopOwnedProcess();
            throw;
        }
        catch (TimeoutException)
        {
            TryStopOwnedProcess();
            return new(true, true, null);
        }
        catch
        {
            TryStopOwnedProcess();
            return new(true, false, null);
        }
    }

    private static ShortcutExecutionResult LogResult(
        ShortcutExecutionResult result,
        bool usedDefaultFolder,
        bool collisionSuffixUsed,
        Stopwatch stopwatch,
        int? exitCode,
        Exception? exception = null)
    {
        var fields = new (string Key, object? Value)[]
        {
            ("ActionType", ShortcutActionTypeIds.ScreenshotFullscreen),
            ("Outcome", result.Outcome),
            ("ExceptionType", exception?.GetType().Name ?? "None"),
            ("UsedDefaultFolder", usedDefaultFolder),
            ("CollisionSuffixUsed", collisionSuffixUsed),
            ("ElapsedMs", stopwatch.ElapsedMilliseconds),
            ("ExitCode", exitCode)
        };

        if (result.Outcome == ShortcutExecutionOutcome.Succeeded)
            AppLog.Info("Shortcuts", "Screenshot capture completed.", fields);
        else
            AppLog.Warn("Shortcuts", "Screenshot capture did not complete.", null, fields);
        return result;
    }

    private static void TryDeletePartialOutput(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Cleanup is best effort; the failure result remains authoritative.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill();
        }
        catch
        {
            // The capture result remains a failure even if the exact child could not be stopped.
        }
    }
}
