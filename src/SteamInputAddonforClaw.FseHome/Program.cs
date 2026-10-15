using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SteamInputAddonforClaw.FseHome;

internal static class Program
{
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(60);
    private const int ProbeIntervalMilliseconds = 500;

    [STAThread]
    private static int Main()
    {
        var diagnostics = new FseHomeDiagnostics();
        diagnostics.Start();
        diagnostics.Info("FseHome start");

        HandoffWindow? handoffWindow = null;
        try
        {
            handoffWindow = HandoffWindow.Create();
            diagnostics.Debug($"Handoff window shown HWND={FormatHwnd(handoffWindow.Handle)} Bounds={handoffWindow.Bounds}");

            var launchRequestedAt = Stopwatch.GetTimestamp();
            var launch = SteamLauncher.RequestBigPicture();
            diagnostics.Info($"Steam launch mode={launch.Mode}");
            if (launch.ExecutablePath is not null)
                diagnostics.Debug($"Steam executable={launch.ExecutablePath}");
            if (launch.DirectLaunchFailure is not null)
                diagnostics.Warn($"Direct Steam launch failed; URI fallback used. Reason={launch.DirectLaunchFailure}");

            while (Stopwatch.GetElapsedTime(launchRequestedAt) < ReadinessTimeout)
            {
                if (SteamBigPictureWindowProbe.TryFind(out var candidate) && NativeMethods.IsWindow(candidate.Handle))
                {
                    var elapsedMilliseconds = Stopwatch.GetElapsedTime(launchRequestedAt).TotalMilliseconds;
                    diagnostics.Debug($"BPM candidate found HWND={FormatHwnd(candidate.Handle)} PID={candidate.ProcessId} ElapsedMs={elapsedMilliseconds:F0}");
                    diagnostics.Debug($"Foreground before handoff HWND={FormatHwnd(NativeMethods.GetForegroundWindow())}");

                    handoffWindow.Hide();
                    if (NativeMethods.IsIconic(candidate.Handle))
                        NativeMethods.ShowWindow(candidate.Handle, NativeMethods.ShowWindowMaximized);

                    var foregroundRequestSucceeded = NativeMethods.SetForegroundWindow(candidate.Handle);
                    var foregroundAfterHandoff = NativeMethods.GetForegroundWindow();
                    diagnostics.Debug($"SetForegroundWindow returned={foregroundRequestSucceeded}");
                    diagnostics.Debug($"Foreground after handoff HWND={FormatHwnd(foregroundAfterHandoff)}");

                    handoffWindow.Dispose();
                    handoffWindow = null;
                    if (foregroundAfterHandoff == candidate.Handle)
                        diagnostics.Info("Steam Big Picture handoff completed.");
                    else
                        diagnostics.Warn("Steam Big Picture handoff did not reach the foreground.");
                    return foregroundAfterHandoff == candidate.Handle ? 0 : 1;
                }

                var remaining = ReadinessTimeout - Stopwatch.GetElapsedTime(launchRequestedAt);
                if (remaining <= TimeSpan.Zero)
                    break;

                var waitMilliseconds = (uint)Math.Clamp(
                    (int)Math.Ceiling(Math.Min(ProbeIntervalMilliseconds, remaining.TotalMilliseconds)),
                    1,
                    ProbeIntervalMilliseconds);
                if (!NativeMethods.WaitForMessagesOrTimeout(waitMilliseconds))
                {
                    diagnostics.Warn("Message wait failed before Steam Big Picture became ready.");
                    return 1;
                }
            }

            diagnostics.Warn("Steam Big Picture readiness timed out after 60000 ms");
            return 1;
        }
        catch (Exception exception)
        {
            diagnostics.Error($"Exception type={exception.GetType().FullName} message={exception.Message}");
            return 1;
        }
        finally
        {
            handoffWindow?.Dispose();
            diagnostics.Info("FseHome exit");
        }
    }

    private static string FormatHwnd(IntPtr hwnd) => $"0x{hwnd.ToInt64():X}";
}

internal readonly record struct SteamLaunchResult(string Mode, string? ExecutablePath, string? DirectLaunchFailure);

internal static class SteamLauncher
{
    private const string BigPictureUri = "steam://open/bigpicture";
    private const string SteamRegistryPath = @"Software\Valve\Steam";

    internal static SteamLaunchResult RequestBigPicture()
    {
        var executablePath = ResolveCurrentUserSteamExecutable();
        string? directLaunchFailure = null;
        if (executablePath is not null)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = false,
                    ArgumentList = { BigPictureUri },
                });
                if (process is not null)
                    return new("DirectExe", executablePath, null);

                directLaunchFailure = "Process.Start returned no process.";
            }
            catch (Exception exception)
            {
                directLaunchFailure = $"{exception.GetType().Name}: {exception.Message}";
            }
        }

        using (Process.Start(new ProcessStartInfo
        {
            FileName = BigPictureUri,
            UseShellExecute = true,
        }))
        {
        }

        return new("UriFallback", null, directLaunchFailure);
    }

    internal static string? ResolveFromRegistryValues(
        string? steamExe,
        string? steamPath,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var executable = ExistingAbsoluteFile(steamExe, fileExists);
        if (executable is not null)
            return executable;

        if (string.IsNullOrWhiteSpace(steamPath))
            return null;

        try
        {
            return ExistingAbsoluteFile(Path.Combine(steamPath.Trim().Trim('"'), "steam.exe"), fileExists);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ResolveCurrentUserSteamExecutable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SteamRegistryPath);
            return ResolveFromRegistryValues(
                key?.GetValue("SteamExe") as string,
                key?.GetValue("SteamPath") as string);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ExistingAbsoluteFile(string? candidate, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        try
        {
            var normalized = candidate.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(normalized))
                return null;

            var fullPath = Path.GetFullPath(normalized);
            return fileExists(fullPath) ? fullPath : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal sealed class FseHomeDiagnostics
{
    private const string PersistentDataDirectoryName = "SteamInputAddonforClaw-Data";
    private readonly string _localApplicationData;
    private readonly string _path;
    private FseHomeLogLevel _minimumLevel;
    private bool _available;

    internal FseHomeDiagnostics(string? localApplicationData = null)
    {
        _localApplicationData = localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _path = ResolveLogPath(_localApplicationData);
    }

    internal static string ResolveLogPath(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        return Path.Combine(localApplicationData, PersistentDataDirectoryName, "logs", "fse-home-last.log");
    }

    internal static string ResolveSettingsPath(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        return Path.Combine(localApplicationData, PersistentDataDirectoryName, "settings.json");
    }

    internal void Start()
    {
        _minimumLevel = ReadLogLevel(ResolveSettingsPath(_localApplicationData));
        if (_minimumLevel == FseHomeLogLevel.Off)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _available = true;
        }
        catch (Exception)
        {
            _available = false;
        }
    }

    internal void Debug(string message) => Write(FseHomeLogLevel.Debug, "DEBUG", message);

    internal void Info(string message) => Write(FseHomeLogLevel.Info, "INFO", message);

    internal void Warn(string message) => Write(FseHomeLogLevel.Info, "WARN", message);

    internal void Error(string message) => Write(FseHomeLogLevel.Info, "ERROR", message);

    private void Write(FseHomeLogLevel level, string severity, string message)
    {
        if (!_available || (level == FseHomeLogLevel.Debug && _minimumLevel != FseHomeLogLevel.Debug))
            return;

        try
        {
            File.AppendAllText(
                _path,
                $"{DateTimeOffset.Now:O} [{severity}] {message}{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception)
        {
            _available = false;
        }
    }

    private static FseHomeLogLevel ReadLogLevel(string settingsPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (!document.RootElement.TryGetProperty("LogLevel", out var value) || value.ValueKind != JsonValueKind.String)
                return FseHomeLogLevel.Off;

            return value.GetString()?.ToLowerInvariant() switch
            {
                "info" => FseHomeLogLevel.Info,
                "debug" => FseHomeLogLevel.Debug,
                _ => FseHomeLogLevel.Off,
            };
        }
        catch (FileNotFoundException) { return FseHomeLogLevel.Info; }
        catch (DirectoryNotFoundException) { return FseHomeLogLevel.Info; }
        catch { return FseHomeLogLevel.Off; }
    }

    private enum FseHomeLogLevel { Off, Info, Debug }
}
