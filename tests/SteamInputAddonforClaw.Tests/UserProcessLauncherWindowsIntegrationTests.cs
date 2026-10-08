using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Contracts.Shortcuts;
using SteamInputAddonforClaw.Processes;
using SteamInputAddonforClaw.Shortcuts;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UserProcessLauncherWindowsIntegrationTests
{
    [Fact]
    public async Task Http_url_shortcut_preserves_query_when_opened_by_the_default_browser()
    {
        Assert.True(OperatingSystem.IsWindows());

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var requestTask = ReceiveHttpRequestLineAsync(listener, timeout.Token);
        var url = $"http://127.0.0.1:{endpoint.Port}/shortcut?q=claw&equals=a=b&next=two";

        try
        {
            var launcher = new UserProcessLauncher((startInfo, runAsAdministrator) =>
            {
                Assert.False(runAsAdministrator);
                using var process = Process.Start(startInfo);
                return process is not null;
            });

            Assert.True(launcher.LaunchUri(url));
            var requestLine = await requestTask.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal($"GET /shortcut?q=claw&equals=a=b&next=two HTTP/1.1", requestLine);
        }
        finally
        {
            timeout.Cancel();
            listener.Stop();
            try
            {
                await requestTask;
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }
        }
    }

    [Fact]
    public async Task Shortcut_medium_launches_preserve_long_commands_and_child_integrity()
    {
        Assert.True(OperatingSystem.IsWindows());

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var powerShellPath = Path.Combine(windowsDirectory, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.True(File.Exists(powerShellPath), $"Windows PowerShell was not found: {powerShellPath}");

        var testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.UserProcessLauncher.Integration.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        try
        {
            using var currentIdentity = WindowsIdentity.GetCurrent();
            var currentUserSid = currentIdentity.User?.Value;
            Assert.False(string.IsNullOrWhiteSpace(currentUserSid));
            var currentIntegritySid = GetCurrentIntegritySid();
            var currentIntegrityRid = ParseIntegrityRid(currentIntegritySid);
            var currentElevationType = GetCurrentElevationType();

            var powershellActionOutput = Path.Combine(testDirectory, "powershell-action.txt");
            var executableActionOutput = Path.Combine(testDirectory, "executable-action.txt");
            var administratorActionOutput = Path.Combine(testDirectory, "administrator-action.txt");
            var powershellScript = CreateIdentityScript(powershellActionOutput);
            var executableScript = CreateIdentityScript(executableActionOutput);
            var administratorScript = CreateIdentityScript(administratorActionOutput);
            var encodedPowerShellScript = Encode(powershellScript);
            var encodedExecutableScript = Encode(executableScript);
            var encodedAdministratorScript = Encode(administratorScript);

            var powershellStartInfo = CreatePowerShellActionStartInfo(powerShellPath, encodedPowerShellScript);
            var executableStartInfo = CreateExecutableActionStartInfo(powerShellPath, encodedExecutableScript);
            Assert.True(UserProcessLauncher.BuildCommandLine(powershellStartInfo).Length > 1_024);
            Assert.True(UserProcessLauncher.BuildCommandLine(executableStartInfo).Length > 1_024);

            var powershellTile = CreateTile(
                "Medium PowerShell",
                ShortcutActionTypeIds.PowerShell,
                new { script = powershellScript, runAsAdministrator = false });
            var executableTile = CreateTile(
                "Medium EXE",
                ShortcutActionTypeIds.Executable,
                new
                {
                    path = powerShellPath,
                    arguments = $"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encodedExecutableScript}",
                    runAsAdministrator = false
                });
            var administratorTile = CreateTile(
                "Administrator PowerShell",
                ShortcutActionTypeIds.PowerShell,
                new { script = administratorScript, runAsAdministrator = true });

            var shortcutPath = Path.Combine(testDirectory, "shortcuts.json");
            var store = new ShortcutStore(shortcutPath);
            store.Save(new ShortcutDocument
            {
                Dashboard = new ShortcutDashboardDefinition([powershellTile, executableTile, administratorTile])
            });
            var runtime = new ShortcutRuntime(store, userProcessLauncher: UserProcessLauncher.Shared);

            var powershellResult = await runtime.ExecuteAsync(powershellTile.TileId);
            var executableResult = await runtime.ExecuteAsync(executableTile.TileId);
            // Medium hosts can launch directly; elevated Full hosts must use the validated linked token.
            var hasSupportedMediumToken = currentIntegrityRid == 0x2000 || currentElevationType == TokenElevationType.Full;
            if (hasSupportedMediumToken)
            {
                Assert.True(powershellResult.Outcome == ShortcutExecutionOutcome.Succeeded,
                    $"Medium PowerShell launch failed. Caller integrity={currentIntegritySid}, elevation={currentElevationType}, result={powershellResult.Outcome}.");
                Assert.True(executableResult.Outcome == ShortcutExecutionOutcome.Succeeded,
                    $"Medium EXE launch failed. Caller integrity={currentIntegritySid}, elevation={currentElevationType}, result={executableResult.Outcome}.");
                AssertChildIdentity(await WaitForIdentityAsync(powershellActionOutput), currentUserSid!, "S-1-16-8192");
                AssertChildIdentity(await WaitForIdentityAsync(executableActionOutput), currentUserSid!, "S-1-16-8192");
            }
            else
            {
                Assert.Equal(ShortcutExecutionOutcome.Failed, powershellResult.Outcome);
                Assert.Equal(ShortcutExecutionOutcome.Failed, executableResult.Outcome);
                Assert.False(File.Exists(powershellActionOutput));
                Assert.False(File.Exists(executableActionOutput));
            }

            var administratorResult = await runtime.ExecuteAsync(administratorTile.TileId);
            if (currentIntegrityRid >= 0x3000 && currentElevationType == TokenElevationType.Full)
            {
                Assert.Equal(ShortcutExecutionOutcome.Succeeded, administratorResult.Outcome);
                AssertChildIdentity(await WaitForIdentityAsync(administratorActionOutput), currentUserSid!, currentIntegritySid);
            }
            else
            {
                Assert.Equal(ShortcutExecutionOutcome.Failed, administratorResult.Outcome);
                Assert.False(File.Exists(administratorActionOutput));
            }
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static ShortcutTileDefinition CreateTile(string title, string typeId, object parameters) =>
        new(Guid.NewGuid(), title, false,
            new ShortcutActionSpec(typeId, 1, JsonSerializer.SerializeToElement(parameters)));

    private static ProcessStartInfo CreatePowerShellActionStartInfo(string executablePath, string encodedScript)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedScript);
        return startInfo;
    }

    private static ProcessStartInfo CreateExecutableActionStartInfo(string executablePath, string encodedScript) => new()
    {
        FileName = executablePath,
        Arguments = $"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encodedScript}",
        UseShellExecute = false
    };

    private static string CreateIdentityScript(string outputPath)
    {
        var escapedPath = outputPath.Replace("'", "''", StringComparison.Ordinal);
        return "$identity = [Security.Principal.WindowsIdentity]::GetCurrent(); "
            + "$groupLine = & (Join-Path $env:windir 'System32\\whoami.exe') /groups /fo csv "
            + "| Select-String -Pattern 'S-1-16-\\d+' | Select-Object -First 1; "
            + "$integrity = [regex]::Match($groupLine.Line, 'S-1-16-\\d+').Value; "
            + $"[IO.File]::WriteAllText('{escapedPath}', \"$($identity.User.Value)|$integrity\")"
            + "\n# " + new string('x', 1_400);
    }

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static async Task<string> ReceiveHttpRequestLineAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectionTimeout.CancelAfter(TimeSpan.FromSeconds(1));
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            string? requestLine;
            try
            {
                requestLine = await reader.ReadLineAsync(connectionTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(requestLine))
                continue;

            var response = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
            await stream.WriteAsync(response, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return requestLine;
        }
    }

    private static string GetCurrentIntegritySid()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "whoami.exe"),
            Arguments = "/groups /fo csv",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("whoami.exe could not be started to inspect the current token.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        var match = System.Text.RegularExpressions.Regex.Match(output, "S-1-16-\\d+");
        return match.Success
            ? match.Value
            : throw new InvalidOperationException("The current process integrity SID was not present in whoami output.");
    }

    private static int ParseIntegrityRid(string integritySid) =>
        int.Parse(integritySid[(integritySid.LastIndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture);

    private static TokenElevationType GetCurrentElevationType()
    {
        using var process = Process.GetCurrentProcess();
        if (!NativeMethods.OpenProcessToken(process.Handle, 0x0008, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The current process token could not be opened.");
        using (token)
        {
            var information = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                if (!NativeMethods.GetTokenInformation(token, TokenInformationClass.ElevationType,
                        information, sizeof(int), out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The current token elevation type could not be read.");
                return (TokenElevationType)Marshal.ReadInt32(information);
            }
            finally
            {
                Marshal.FreeHGlobal(information);
            }
        }
    }

    private static void AssertChildIdentity(string identityText, string expectedUserSid, string expectedIntegritySid)
    {
        var parts = identityText.Split('|', 2);
        Assert.Equal(2, parts.Length);
        Assert.Equal(expectedUserSid, parts[0]);
        Assert.Equal(expectedIntegritySid, parts[1]);
    }

    private static async Task<string> WaitForIdentityAsync(string outputPath)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (File.Exists(outputPath))
            {
                var content = await File.ReadAllTextAsync(outputPath);
                if (content.Contains("|S-1-16-", StringComparison.Ordinal))
                    return content;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"The launched child did not write its token identity in time: {outputPath}");
        return string.Empty;
    }

    private enum TokenInformationClass
    {
        ElevationType = 18
    }

    private enum TokenElevationType
    {
        Default = 1,
        Full = 2,
        Limited = 3
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(SafeAccessTokenHandle tokenHandle, TokenInformationClass informationClass,
            IntPtr information, uint informationLength, out uint returnLength);
    }
}
