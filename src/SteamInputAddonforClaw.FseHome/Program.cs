using System.Diagnostics;
using System.Text;
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
        diagnostics.Write("FseHome start");

        HandoffWindow? handoffWindow = null;
        try
        {
            handoffWindow = HandoffWindow.Create();
            diagnostics.Write($"Handoff window shown HWND={FormatHwnd(handoffWindow.Handle)} Bounds={handoffWindow.Bounds}");

            var launchRequestedAt = Stopwatch.GetTimestamp();
            var launch = SteamLauncher.RequestBigPicture();
            diagnostics.Write($"Steam launch mode={launch.Mode}");
            if (launch.ExecutablePath is not null)
                diagnostics.Write($"Steam executable={launch.ExecutablePath}");
            if (launch.DirectLaunchFailure is not null)
                diagnostics.Write($"Direct Steam launch failed; URI fallback used. Reason={launch.DirectLaunchFailure}");

            while (Stopwatch.GetElapsedTime(launchRequestedAt) < ReadinessTimeout)
            {
                if (SteamBigPictureWindowProbe.TryFind(out var candidate) && NativeMethods.IsWindow(candidate.Handle))
                {
                    var elapsedMilliseconds = Stopwatch.GetElapsedTime(launchRequestedAt).TotalMilliseconds;
                    diagnostics.Write($"BPM candidate found HWND={FormatHwnd(candidate.Handle)} PID={candidate.ProcessId} ElapsedMs={elapsedMilliseconds:F0}");
                    diagnostics.Write($"Foreground before handoff HWND={FormatHwnd(NativeMethods.GetForegroundWindow())}");

                    handoffWindow.Hide();
                    if (NativeMethods.IsIconic(candidate.Handle))
                        NativeMethods.ShowWindow(candidate.Handle, NativeMethods.ShowWindowMaximized);

                    var foregroundRequestSucceeded = NativeMethods.SetForegroundWindow(candidate.Handle);
                    var foregroundAfterHandoff = NativeMethods.GetForegroundWindow();
                    diagnostics.Write($"SetForegroundWindow returned={foregroundRequestSucceeded}");
                    diagnostics.Write($"Foreground after handoff HWND={FormatHwnd(foregroundAfterHandoff)}");

                    handoffWindow.Dispose();
                    handoffWindow = null;
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
                    return 1;
            }

            diagnostics.Write("Steam Big Picture readiness timed out after 60000 ms");
            return 1;
        }
        catch (Exception exception)
        {
            diagnostics.Write($"Exception type={exception.GetType().FullName} message={exception.Message}");
            return 1;
        }
        finally
        {
            handoffWindow?.Dispose();
            diagnostics.Write("FseHome exit");
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
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamInputAddonforClaw",
        "logs",
        "fse-home-last.log");
    private bool _available = true;

    internal void Start()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception)
        {
            _available = false;
        }
    }

    internal void Write(string message)
    {
        if (!_available)
            return;

        try
        {
            File.AppendAllText(
                _path,
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception)
        {
            _available = false;
        }
    }
}
