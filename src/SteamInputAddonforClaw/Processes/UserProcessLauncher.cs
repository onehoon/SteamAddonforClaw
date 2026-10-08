using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.WindowsGaming;

namespace SteamInputAddonforClaw.Processes;

/// <summary>Starts user-requested processes without changing the Runtime's controller-authority token.</summary>
internal sealed class UserProcessLauncher
{
    private const uint TokenAssignPrimary = 0x0001;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const int MediumIntegrityRid = 0x2000;
    private const int HighIntegrityRid = 0x3000;
    private const int MaxCommandLineChars = 32_766;

    private readonly Func<ProcessStartInfo, bool, bool>? _launchOverride;

    internal static UserProcessLauncher Shared { get; } = new();

    internal UserProcessLauncher()
    {
    }

    internal UserProcessLauncher(Func<ProcessStartInfo, bool, bool> launchOverride)
        => _launchOverride = launchOverride ?? throw new ArgumentNullException(nameof(launchOverride));

    /// <summary>Launches a literal executable. The caller retains action-specific validation and argument construction.</summary>
    internal bool Launch(ProcessStartInfo startInfo, bool runAsAdministrator = false)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (string.IsNullOrWhiteSpace(startInfo.FileName)
            || startInfo.UseShellExecute
            || !string.Equals(Path.GetExtension(startInfo.FileName), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("User process launch requires a literal .exe with shell execution disabled.");

        if (_launchOverride is not null)
            return _launchOverride(startInfo, runAsAdministrator);

        if (runAsAdministrator)
            return StartWithCurrentHighToken(startInfo);

        return StartWithMediumUserToken(startInfo);
    }

    /// <summary>Dispatches a supported URI through Explorer running as the interactive user.</summary>
    internal bool LaunchUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || string.IsNullOrWhiteSpace(parsed.Host)
            || !(parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || uri.Equals("steam://open/bigpicture", StringComparison.OrdinalIgnoreCase)
                || uri.Equals("steam://open/main", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Only HTTP, HTTPS, and Steam URIs can use shell activation.");

        return LaunchShellTarget(uri);
    }

    /// <summary>Activates the product's fixed Xbox gaming-home package for the interactive user.</summary>
    internal bool LaunchXboxApp()
        => LaunchShellTarget($"shell:AppsFolder\\{XboxGamingHomeAppIdentity.Aumid}");

    private bool LaunchShellTarget(string target)
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
            throw new InvalidOperationException("The Windows directory is unavailable.");

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(windowsDirectory, "explorer.exe"),
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(target);
        return Launch(startInfo);
    }

    private static bool StartWithCurrentHighToken(ProcessStartInfo startInfo)
    {
        using var currentToken = OpenCurrentProcessToken(TokenQuery);
        if (GetTokenElevationType(currentToken) != TokenElevationType.Full
            || GetTokenIntegrityRid(currentToken) < HighIntegrityRid)
            throw new InvalidOperationException("Administrator launch requires the existing High Runtime token.");

        using var process = Process.Start(startInfo);
        return process is not null;
    }

    private static bool StartWithMediumUserToken(ProcessStartInfo startInfo)
    {
        using var currentToken = OpenCurrentProcessToken(TokenQuery | TokenDuplicate);
        var currentIntegrity = GetTokenIntegrityRid(currentToken);
        if (currentIntegrity == MediumIntegrityRid)
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }

        if (currentIntegrity < HighIntegrityRid || GetTokenElevationType(currentToken) != TokenElevationType.Full)
            throw new InvalidOperationException("A verified Medium user token is unavailable.");

        using var linkedToken = GetLinkedToken(currentToken);
        ValidateMediumLinkedToken(currentToken, linkedToken);
        using var primaryToken = DuplicateAsPrimary(linkedToken);

        var commandLine = new StringBuilder(BuildCommandLine(startInfo));
        var startupInfo = new StartupInfo
        {
            Size = Marshal.SizeOf<StartupInfo>(),
            Desktop = "winsta0\\default"
        };
        var creationFlags = CreateUnicodeEnvironment
            | (startInfo.CreateNoWindow ? CreateNoWindow : 0);

        if (!NativeMethods.CreateEnvironmentBlock(out var environment, primaryToken, false))
            throw LastWin32Exception("The Medium user environment could not be created.");

        try
        {
            if (!NativeMethods.CreateProcessWithTokenW(
                    primaryToken,
                    0,
                    Path.GetFullPath(startInfo.FileName),
                    commandLine,
                    creationFlags,
                    environment,
                    string.IsNullOrWhiteSpace(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory,
                    ref startupInfo,
                    out var processInformation))
                throw LastWin32Exception("The process could not be started with the Medium user token.");

            try { return true; }
            finally
            {
                NativeMethods.CloseHandle(processInformation.ThreadHandle);
                NativeMethods.CloseHandle(processInformation.ProcessHandle);
            }
        }
        finally
        {
            NativeMethods.DestroyEnvironmentBlock(environment);
        }
    }

    private static void ValidateMediumLinkedToken(SafeTokenHandle currentToken, SafeTokenHandle linkedToken)
    {
        if (GetTokenElevationType(linkedToken) != TokenElevationType.Limited
            || GetTokenIntegrityRid(linkedToken) != MediumIntegrityRid
            || GetTokenSessionId(currentToken) != GetTokenSessionId(linkedToken)
            || !HasSameUser(currentToken, linkedToken))
            throw new InvalidOperationException("The linked token is not the same user's interactive Medium token.");
    }

    private static SafeTokenHandle OpenCurrentProcessToken(uint desiredAccess)
    {
        using var currentProcess = Process.GetCurrentProcess();
        if (!NativeMethods.OpenProcessToken(currentProcess.Handle, desiredAccess, out var token))
            throw LastWin32Exception("The Runtime process token could not be opened.");
        return token;
    }

    private static SafeTokenHandle GetLinkedToken(SafeTokenHandle token)
    {
        using var information = GetTokenInformation(token, TokenInformationClass.LinkedToken);
        var linkedHandle = Marshal.PtrToStructure<TokenLinkedToken>(information.DangerousGetHandle()).Token;
        if (linkedHandle == IntPtr.Zero)
            throw new InvalidOperationException("The Runtime has no UAC-linked Medium token.");
        return new SafeTokenHandle(linkedHandle);
    }

    private static SafeTokenHandle DuplicateAsPrimary(SafeTokenHandle token)
    {
        var desiredAccess = TokenQuery | TokenDuplicate | TokenAssignPrimary;
        if (!NativeMethods.DuplicateTokenEx(token, desiredAccess, IntPtr.Zero, SecurityImpersonation,
                TokenPrimary, out var primaryToken))
            throw LastWin32Exception("The Medium user token could not be prepared for process creation.");
        return primaryToken;
    }

    private static TokenElevationType GetTokenElevationType(SafeTokenHandle token)
    {
        using var information = GetTokenInformation(token, TokenInformationClass.ElevationType);
        return (TokenElevationType)Marshal.ReadInt32(information.DangerousGetHandle());
    }

    private static int GetTokenSessionId(SafeTokenHandle token)
    {
        using var information = GetTokenInformation(token, TokenInformationClass.SessionId);
        return Marshal.ReadInt32(information.DangerousGetHandle());
    }

    private static int GetTokenIntegrityRid(SafeTokenHandle token)
    {
        using var information = GetTokenInformation(token, TokenInformationClass.IntegrityLevel);
        var label = Marshal.PtrToStructure<TokenMandatoryLabel>(information.DangerousGetHandle());
        var count = Marshal.ReadByte(NativeMethods.GetSidSubAuthorityCount(label.Label.Sid));
        if (count == 0)
            throw new InvalidOperationException("The token integrity SID is invalid.");
        var rid = NativeMethods.GetSidSubAuthority(label.Label.Sid, (uint)(count - 1));
        if (rid == IntPtr.Zero)
            throw new InvalidOperationException("The token integrity SID is invalid.");
        return Marshal.ReadInt32(rid);
    }

    private static bool HasSameUser(SafeTokenHandle firstToken, SafeTokenHandle secondToken)
    {
        using var firstUser = GetTokenInformation(firstToken, TokenInformationClass.User);
        using var secondUser = GetTokenInformation(secondToken, TokenInformationClass.User);
        var first = Marshal.PtrToStructure<TokenUser>(firstUser.DangerousGetHandle()).User.Sid;
        var second = Marshal.PtrToStructure<TokenUser>(secondUser.DangerousGetHandle()).User.Sid;
        return NativeMethods.EqualSid(first, second);
    }

    private static SafeLocalBuffer GetTokenInformation(SafeTokenHandle token, TokenInformationClass informationClass)
    {
        NativeMethods.GetTokenInformation(token, informationClass, IntPtr.Zero, 0, out var requiredLength);
        var error = Marshal.GetLastWin32Error();
        if (requiredLength == 0)
            throw new Win32Exception(error, "Token information size could not be queried.");

        var buffer = new SafeLocalBuffer(Marshal.AllocHGlobal(checked((int)requiredLength)));
        if (!NativeMethods.GetTokenInformation(token, informationClass, buffer.DangerousGetHandle(), requiredLength, out _))
        {
            buffer.Dispose();
            throw LastWin32Exception("Token information could not be read.");
        }
        return buffer;
    }

    internal static string BuildCommandLine(ProcessStartInfo startInfo)
    {
        if (startInfo.ArgumentList.Count != 0 && !string.IsNullOrEmpty(startInfo.Arguments))
            throw new InvalidOperationException("Process arguments cannot use both argument representations.");

        var commandLine = new StringBuilder(QuoteArgument(startInfo.FileName));
        if (startInfo.ArgumentList.Count > 0)
        {
            foreach (var argument in startInfo.ArgumentList)
            {
                commandLine.Append(' ').Append(QuoteArgument(argument));
            }
        }
        else if (!string.IsNullOrEmpty(startInfo.Arguments))
        {
            commandLine.Append(' ').Append(startInfo.Arguments);
        }

        if (commandLine.Length > MaxCommandLineChars)
            throw new InvalidOperationException("The process command line exceeds the Windows limit.");

        return commandLine.ToString();
    }

    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', (backslashes * 2) + 1).Append('"');
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }

        result.Append('\\', backslashes * 2).Append('"');
        return result.ToString();
    }

    private static Win32Exception LastWin32Exception(string message)
        => new(Marshal.GetLastWin32Error(), message);

    private enum TokenInformationClass
    {
        User = 1,
        SessionId = 12,
        ElevationType = 18,
        LinkedToken = 19,
        IntegrityLevel = 25
    }

    private enum TokenElevationType
    {
        Default = 1,
        Full = 2,
        Limited = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        internal IntPtr Sid;
        internal uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenUser
    {
        internal SidAndAttributes User;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        internal SidAndAttributes Label;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenLinkedToken
    {
        internal IntPtr Token;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        internal int Size;
        internal string? Reserved;
        internal string? Desktop;
        internal string? Title;
        internal int X;
        internal int Y;
        internal int XSize;
        internal int YSize;
        internal int XCountChars;
        internal int YCountChars;
        internal int FillAttribute;
        internal int Flags;
        internal short ShowWindow;
        internal short Reserved2Size;
        internal IntPtr Reserved2;
        internal IntPtr StandardInput;
        internal IntPtr StandardOutput;
        internal IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        internal IntPtr ProcessHandle;
        internal IntPtr ThreadHandle;
        internal int ProcessId;
        internal int ThreadId;
    }

    private sealed class SafeTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeTokenHandle() : base(ownsHandle: true) { }
        internal SafeTokenHandle(IntPtr handle) : base(ownsHandle: true) => SetHandle(handle);
        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    private sealed class SafeLocalBuffer : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeLocalBuffer(IntPtr buffer) : base(ownsHandle: true) => SetHandle(buffer);
        protected override bool ReleaseHandle()
        {
            Marshal.FreeHGlobal(handle);
            return true;
        }
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeTokenHandle tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(SafeTokenHandle tokenHandle, TokenInformationClass informationClass,
            IntPtr information, uint informationLength, out uint returnLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DuplicateTokenEx(SafeTokenHandle existingToken, uint desiredAccess, IntPtr tokenAttributes,
            int impersonationLevel, int tokenType, out SafeTokenHandle newToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessWithTokenW(SafeTokenHandle token, uint logonFlags, string applicationName,
            StringBuilder commandLine, uint creationFlags, IntPtr environment, string? currentDirectory,
            ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [DllImport("userenv.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyEnvironmentBlock(IntPtr environment);

        [DllImport("advapi32.dll")]
        internal static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

        [DllImport("advapi32.dll")]
        internal static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthorityIndex);

        [DllImport("advapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EqualSid(IntPtr firstSid, IntPtr secondSid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
