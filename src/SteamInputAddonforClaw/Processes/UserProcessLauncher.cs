using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.WindowsGaming;

namespace SteamInputAddonforClaw.Processes;

/// <summary>Starts user-requested processes without changing the Runtime's controller-authority token.</summary>
internal sealed class UserProcessLauncher
{
    private const uint TokenQuery = 0x0008;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;
    private const int MediumIntegrityRid = 0x2000;
    private const int HighIntegrityRid = 0x3000;
    private const int MaxCommandLineChars = 32_766;
    private const int MaxCreateProcessWithTokenCommandLineChars = 1_024;

    internal enum MediumProcessCreationApi
    {
        CreateProcessAsUserW,
        CreateProcessWithTokenW
    }

    internal enum MediumLaunchRoute
    {
        DirectProcessCreation,
        DesktopShellDispatch
    }

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
        return LaunchCore(startInfo, runAsAdministrator, MediumProcessCreationApi.CreateProcessAsUserW);
    }

    internal bool LaunchViaDesktopShell(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ValidateExecutableStartInfo(startInfo);
        using var currentToken = OpenCurrentProcessToken(TokenQuery, "OpenProcessToken.Current");
        return StartThroughDesktopShell(startInfo, currentToken);
    }

    /// <summary>Dispatches a supported HTTP(S) or Steam URI for the interactive user.</summary>
    internal bool LaunchUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || string.IsNullOrWhiteSpace(parsed.Host)
            || !(parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || uri.Equals("steam://open/bigpicture", StringComparison.OrdinalIgnoreCase)
                || uri.Equals("steam://open/main", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Only HTTP, HTTPS, and Steam URIs can use shell activation.");

        var isWebUrl = parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        return isWebUrl
            ? LaunchWebUrl(parsed.AbsoluteUri)
            : LaunchShellTarget(uri);
    }

    /// <summary>Activates the product's fixed Xbox gaming-home package for the interactive user.</summary>
    internal bool LaunchXboxApp()
        => LaunchShellTarget($"shell:AppsFolder\\{XboxGamingHomeAppIdentity.Aumid}");

    private bool LaunchShellTarget(string target)
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
            throw new InvalidOperationException("The Windows directory is unavailable.");

        var startInfo = new ProcessStartInfo(Path.Combine(windowsDirectory, "explorer.exe"))
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(target);

        return LaunchCore(startInfo, runAsAdministrator: false, MediumProcessCreationApi.CreateProcessWithTokenW);
    }

    private bool LaunchCore(
        ProcessStartInfo startInfo,
        bool runAsAdministrator,
        MediumProcessCreationApi mediumProcessCreationApi)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ValidateExecutableStartInfo(startInfo);

        if (_launchOverride is not null)
            return _launchOverride(startInfo, runAsAdministrator);

        if (runAsAdministrator)
            return StartWithCurrentHighToken(startInfo);

        return StartWithMediumUserToken(startInfo, mediumProcessCreationApi);
    }

    private static void ValidateExecutableStartInfo(ProcessStartInfo startInfo)
    {
        if (string.IsNullOrWhiteSpace(startInfo.FileName)
            || startInfo.UseShellExecute
            || !string.Equals(Path.GetExtension(startInfo.FileName), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("User process launch requires a literal .exe with shell execution disabled.");
    }

    private bool LaunchWebUrl(string url)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "rundll32.exe"))
        {
            Arguments = $"url.dll,FileProtocolHandler \"{url}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };
        return Launch(startInfo);
    }

    private static bool StartWithCurrentHighToken(ProcessStartInfo startInfo)
    {
        using var currentToken = OpenCurrentProcessToken(TokenQuery, "OpenProcessToken.CurrentHigh");
        var elevationType = GetTokenElevationType(currentToken);
        var integrity = GetTokenIntegrityRid(currentToken);
        var valid = elevationType == TokenElevationType.Full && integrity >= HighIntegrityRid;
        AppLog.Debug("UserProcessLauncher", "Current High token validation completed.",
            ("CurrentIntegrity", GetIntegrityCategory(integrity)),
            ("CurrentElevation", GetElevationCategory(elevationType)),
            ("ValidationSucceeded", valid), ("RequestedPrivilegeMode", "High"));
        if (!valid)
            throw new InvalidOperationException("Administrator launch requires the existing High Runtime token.");

        using var process = Process.Start(startInfo);
        return process is not null;
    }

    private static bool StartWithMediumUserToken(
        ProcessStartInfo startInfo,
        MediumProcessCreationApi processCreationApi)
    {
        using var currentToken = OpenCurrentProcessToken(TokenQuery, "OpenProcessToken.Current");
        var currentIntegrity = GetTokenIntegrityRid(currentToken);
        if (currentIntegrity == MediumIntegrityRid)
        {
            AppLog.Debug("UserProcessLauncher", "Current process already has a Medium token.",
                ("CurrentIntegrity", "Medium"), ("LinkedTokenChecked", false),
                ("RequestedPrivilegeMode", "Medium"));
            using var process = Process.Start(startInfo);
            return process is not null;
        }

        var currentElevationType = GetTokenElevationType(currentToken);
        AppLog.Debug("UserProcessLauncher", "Current token inspected for Medium process creation.",
            ("CurrentIntegrity", GetIntegrityCategory(currentIntegrity)),
            ("CurrentElevation", GetElevationCategory(currentElevationType)),
            ("RequestedPrivilegeMode", "Medium"));
        if (currentIntegrity < HighIntegrityRid || currentElevationType != TokenElevationType.Full)
            throw new InvalidOperationException("A verified Medium user token is unavailable.");

        using var linkedToken = GetLinkedToken(currentToken);
        var linkedTokenType = ValidateMediumLinkedToken(currentToken, linkedToken, currentIntegrity, currentElevationType);
        var launchRoute = GetMediumLaunchRoute((int)linkedTokenType);
        if (launchRoute == MediumLaunchRoute.DesktopShellDispatch)
            return StartThroughDesktopShell(startInfo, currentToken);

        var processToken = linkedToken;

        var commandLineText = BuildCommandLine(startInfo);
        if (processCreationApi == MediumProcessCreationApi.CreateProcessWithTokenW
            && !IsWithinCreateProcessWithTokenCommandLineLimit(commandLineText))
            throw new InvalidOperationException("The shell activation command exceeds the selected Windows API limit.");

        var commandLine = new StringBuilder(commandLineText);
        var startupInfo = new StartupInfo
        {
            Size = Marshal.SizeOf<StartupInfo>(),
            Desktop = "winsta0\\default"
        };
        var creationFlags = CreateUnicodeEnvironment
            | (startInfo.CreateNoWindow ? CreateNoWindow : 0);

        if (!NativeMethods.CreateEnvironmentBlock(out var environment, processToken, false))
            throw LastWin32Exception("CreateEnvironmentBlock", "The Medium user environment could not be created.");

        try
        {
            AppLog.Debug("UserProcessLauncher", "Creating a same-session Medium user process.",
                ("ProcessCreationApi", processCreationApi), ("CommandLineLength", commandLine.Length),
                ("CurrentDirectoryPresent", !string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)),
                ("RequestedPrivilegeMode", "Medium"));
            ProcessInformation processInformation;
            bool created;
            if (processCreationApi == MediumProcessCreationApi.CreateProcessWithTokenW)
            {
                created = NativeMethods.CreateProcessWithTokenW(
                    processToken,
                    0,
                    Path.GetFullPath(startInfo.FileName),
                    commandLine,
                    creationFlags,
                    environment,
                    string.IsNullOrWhiteSpace(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory,
                    ref startupInfo,
                    out processInformation);
            }
            else
            {
                created = NativeMethods.CreateProcessAsUserW(
                    processToken,
                    Path.GetFullPath(startInfo.FileName),
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    creationFlags,
                    environment,
                    string.IsNullOrWhiteSpace(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory,
                    ref startupInfo,
                    out processInformation);
            }
            if (!created)
            {
                var stage = processCreationApi.ToString();
                throw LastWin32Exception(stage, "The process could not be started with the Medium user token.");
            }

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

    private static TokenType ValidateMediumLinkedToken(
        SafeTokenHandle currentToken,
        SafeTokenHandle linkedToken,
        int currentIntegrity,
        TokenElevationType currentElevationType)
    {
        var linkedElevationType = GetTokenElevationType(linkedToken);
        var linkedIntegrity = GetTokenIntegrityRid(linkedToken);
        var linkedTokenType = GetTokenType(linkedToken);
        var linkedImpersonationLevel = linkedTokenType == TokenType.Impersonation
            ? GetTokenImpersonationLevel(linkedToken)
            : (SecurityImpersonationLevel?)null;
        var sameSession = GetTokenSessionId(currentToken) == GetTokenSessionId(linkedToken);
        var sameUser = HasSameUser(currentToken, linkedToken);
        var identityValid = linkedElevationType == TokenElevationType.Limited
            && linkedIntegrity == MediumIntegrityRid
            && sameSession
            && sameUser;
        var supportedTokenType = linkedTokenType is TokenType.Primary or TokenType.Impersonation;
        AppLog.Debug("UserProcessLauncher", "Linked Medium token validation completed.",
            ("CurrentIntegrity", GetIntegrityCategory(currentIntegrity)),
            ("CurrentElevation", GetElevationCategory(currentElevationType)),
            ("LinkedIntegrity", GetIntegrityCategory(linkedIntegrity)),
            ("LinkedElevation", GetElevationCategory(linkedElevationType)),
            ("LinkedTokenType", GetTokenTypeCategory(linkedTokenType)),
            ("LinkedImpersonationLevel", linkedImpersonationLevel is { } diagnosticLevel
                ? GetImpersonationLevelCategory(diagnosticLevel)
                : linkedTokenType == TokenType.Primary ? "NotApplicable" : "NotQueried"),
            ("SameUser", sameUser), ("SameSession", sameSession),
            ("ValidationSucceeded", identityValid && supportedTokenType));
        if (!identityValid)
            throw new InvalidOperationException("The linked token is not the same user's interactive Medium token.");
        if (!supportedTokenType)
        {
            throw new InvalidOperationException(
                $"The linked Medium token type is unsupported (TokenType={GetTokenTypeCategory(linkedTokenType)}).");
        }

        return linkedTokenType;
    }

    internal static MediumLaunchRoute GetMediumLaunchRoute(int tokenType) => tokenType switch
    {
        (int)TokenType.Primary => MediumLaunchRoute.DirectProcessCreation,
        (int)TokenType.Impersonation => MediumLaunchRoute.DesktopShellDispatch,
        _ => throw new InvalidOperationException("The linked Medium token type is unsupported.")
    };

    private static bool StartThroughDesktopShell(ProcessStartInfo startInfo, SafeTokenHandle currentToken)
    {
        const int csidlDesktop = 0;
        const int swcDesktop = 8;
        const int swfoNeedDispatch = 1;
        const int svgIoBackground = 0;
        const int swShowNormal = 1;
        const int swHide = 0;
        var shellWindowsClsid = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        var serviceGuid = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");

        object? shellWindowsObject = null;
        object? desktopWindowObject = null;
        object? shellBrowserObject = null;
        object? shellViewObject = null;
        object? folderViewObject = null;
        object? shellDispatchObject = null;
        var stage = "CreateShellWindows";
        try
        {
            var shellWindowsType = Type.GetTypeFromCLSID(shellWindowsClsid, throwOnError: true)
                ?? throw new InvalidOperationException("The Windows ShellWindows class is unavailable.");
            shellWindowsObject = Activator.CreateInstance(shellWindowsType)
                ?? throw new InvalidOperationException("The Windows ShellWindows object could not be created.");
            var shellWindows = (IShellWindows)shellWindowsObject;

            stage = "FindDesktopWindow";
            object location = csidlDesktop;
            object locationRoot = new();
            desktopWindowObject = shellWindows.FindWindowSW(
                ref location, ref locationRoot, swcDesktop, out var desktopHwnd, swfoNeedDispatch);
            if (desktopHwnd == 0 || desktopWindowObject is not IComServiceProvider serviceProvider)
                throw new InvalidOperationException("The current user's desktop Shell dispatch is unavailable.");

            stage = "ResolveDesktopShellBrowser";
            var shellBrowserIid = typeof(IShellBrowser).GUID;
            shellBrowserObject = serviceProvider.QueryService(ref serviceGuid, ref shellBrowserIid);
            if (shellBrowserObject is not IShellBrowser shellBrowser)
                throw new InvalidOperationException("The desktop Shell browser interface is unavailable.");

            stage = "ResolveDesktopFolderView";
            shellViewObject = shellBrowser.QueryActiveShellView();
            if (shellViewObject is not IShellView shellView)
                throw new InvalidOperationException("The desktop Shell view is unavailable.");
            var folderViewIid = new Guid("00020400-0000-0000-C000-000000000046");
            folderViewObject = shellView.GetItemObject(svgIoBackground, ref folderViewIid);
            if (folderViewObject is not IShellFolderViewDual folderView)
                throw new InvalidOperationException("The desktop folder view dispatch is unavailable.");

            stage = "ResolveDesktopShellApplication";
            shellDispatchObject = folderView.Application;
            if (shellDispatchObject is not IShellDispatch2 shellDispatch)
                throw new InvalidOperationException("The desktop IShellDispatch2 interface is unavailable.");

            stage = "ValidateDesktopShellToken";
            var desktopThreadId = NativeMethods.GetWindowThreadProcessId(desktopHwnd, out var desktopProcessId);
            if (desktopThreadId == 0 || desktopProcessId == 0)
                throw new InvalidOperationException("The desktop Shell process could not be identified.");

            using (var desktopProcess = Process.GetProcessById(checked((int)desktopProcessId)))
            using (var desktopToken = OpenProcessToken(desktopProcess.Handle, "OpenProcessToken.DesktopShell"))
            {
                var sameUser = HasSameUser(currentToken, desktopToken);
                var sameSession = GetTokenSessionId(currentToken) == GetTokenSessionId(desktopToken);
                var desktopIntegrity = GetTokenIntegrityRid(desktopToken);
                var mediumDesktop = desktopIntegrity == MediumIntegrityRid;
                var desktopElevation = GetTokenElevationType(desktopToken);
                var validDesktop = sameUser && sameSession && mediumDesktop;
                AppLog.Debug("UserProcessLauncher", "Desktop Shell token validation completed.",
                    ("DesktopIntegrity", GetIntegrityCategory(desktopIntegrity)),
                    ("DesktopElevation", GetElevationCategory(desktopElevation)),
                    ("SameUser", sameUser), ("SameSession", sameSession),
                    ("ValidationSucceeded", validDesktop));
                if (!validDesktop)
                    throw new InvalidOperationException("The desktop Shell is not the same user's interactive Medium process.");
            }

            _ = BuildCommandLine(startInfo);
            var arguments = BuildShellArguments(startInfo);
            var workingDirectory = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
                ? string.Empty
                : startInfo.WorkingDirectory;
            var showCommand = startInfo.CreateNoWindow || startInfo.WindowStyle == ProcessWindowStyle.Hidden
                ? swHide
                : swShowNormal;

            stage = "DesktopShellExecute";
            shellDispatch.ShellExecute(Path.GetFullPath(startInfo.FileName), arguments,
                workingDirectory, "open", showCommand);
            AppLog.Debug("UserProcessLauncher", "Medium launch was accepted by the desktop Shell.",
                ("Stage", stage), ("DispatchAccepted", true), ("TargetVisibility", "Unverified"),
                ("RequestedPrivilegeMode", "Medium"));
            return true;
        }
        catch (COMException exception)
        {
            AppLog.Warn("UserProcessLauncher", "Desktop Shell launch dispatch failed.", exception,
                ("Stage", stage), ("HResult", $"0x{exception.HResult:X8}"),
                ("DispatchAccepted", false));
            throw new COMException($"Desktop Shell launch failed during {stage}.", exception.ErrorCode);
        }
        finally
        {
            ReleaseComObject(shellDispatchObject);
            ReleaseComObject(folderViewObject);
            ReleaseComObject(shellViewObject);
            ReleaseComObject(shellBrowserObject);
            ReleaseComObject(desktopWindowObject);
            ReleaseComObject(shellWindowsObject);
        }
    }

    internal static string BuildShellArguments(ProcessStartInfo startInfo)
    {
        if (startInfo.ArgumentList.Count != 0 && !string.IsNullOrEmpty(startInfo.Arguments))
            throw new InvalidOperationException("Process arguments cannot use both argument representations.");

        return startInfo.ArgumentList.Count == 0
            ? startInfo.Arguments
            : string.Join(' ', startInfo.ArgumentList.Select(QuoteArgument));
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }

    private static SafeTokenHandle OpenProcessToken(IntPtr processHandle, string stage)
    {
        if (!NativeMethods.OpenProcessToken(processHandle, TokenQuery, out var token))
            throw LastWin32Exception(stage, "The desktop Shell process token could not be opened.");
        return token;
    }

    private static string GetTokenTypeCategory(TokenType tokenType) => tokenType switch
    {
        TokenType.Primary => "Primary",
        TokenType.Impersonation => "Impersonation",
        _ => "Unknown"
    };

    private static string GetImpersonationLevelCategory(SecurityImpersonationLevel level) => level switch
    {
        SecurityImpersonationLevel.Anonymous => "Anonymous",
        SecurityImpersonationLevel.Identification => "Identification",
        SecurityImpersonationLevel.Impersonation => "Impersonation",
        SecurityImpersonationLevel.Delegation => "Delegation",
        _ => "Unknown"
    };

    private static string GetIntegrityCategory(int integrityRid) => integrityRid switch
    {
        >= HighIntegrityRid => "HighOrAbove",
        >= MediumIntegrityRid => "Medium",
        > 0 => "Low",
        _ => "Unknown"
    };

    private static string GetElevationCategory(TokenElevationType elevationType) => elevationType switch
    {
        TokenElevationType.Full => "Full",
        TokenElevationType.Limited => "Limited",
        TokenElevationType.Default => "Default",
        _ => "Unknown"
    };

    private static SafeTokenHandle OpenCurrentProcessToken(uint desiredAccess, string stage)
    {
        using var currentProcess = Process.GetCurrentProcess();
        if (!NativeMethods.OpenProcessToken(currentProcess.Handle, desiredAccess, out var token))
            throw LastWin32Exception(stage, "The Runtime process token could not be opened.");
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

    private static TokenType GetTokenType(SafeTokenHandle token)
    {
        using var information = GetTokenInformation(token, TokenInformationClass.Type);
        return (TokenType)Marshal.ReadInt32(information.DangerousGetHandle());
    }

    private static SecurityImpersonationLevel GetTokenImpersonationLevel(SafeTokenHandle token)
    {
        using var information = GetTokenInformation(token, TokenInformationClass.ImpersonationLevel);
        return (SecurityImpersonationLevel)Marshal.ReadInt32(information.DangerousGetHandle());
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
            throw new UserProcessLaunchException($"GetTokenInformation.{informationClass}.Size", error,
                "Token information size could not be queried.");

        var buffer = new SafeLocalBuffer(Marshal.AllocHGlobal(checked((int)requiredLength)));
        if (!NativeMethods.GetTokenInformation(token, informationClass, buffer.DangerousGetHandle(), requiredLength, out _))
        {
            buffer.Dispose();
            throw LastWin32Exception($"GetTokenInformation.{informationClass}", "Token information could not be read.");
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

    internal static UserProcessLaunchException CaptureNativeFailure(
        string stage,
        Func<int> getLastError,
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(getLastError);
        var errorCode = getLastError();
        return new UserProcessLaunchException(stage, errorCode, message ?? $"{stage} failed.");
    }

    internal static bool IsWithinCreateProcessWithTokenCommandLineLimit(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        return commandLine.Length < MaxCreateProcessWithTokenCommandLineChars;
    }

    private static UserProcessLaunchException LastWin32Exception(string stage, string message)
        => CaptureNativeFailure(stage, Marshal.GetLastWin32Error, message);

    private enum TokenInformationClass
    {
        User = 1,
        Type = 8,
        ImpersonationLevel = 9,
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

    private enum TokenType
    {
        Primary = 1,
        Impersonation = 2
    }

    private enum SecurityImpersonationLevel
    {
        Anonymous = 0,
        Identification = 1,
        Impersonation = 2,
        Delegation = 3
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

    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellWindows
    {
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(
            [MarshalAs(UnmanagedType.Struct)] ref object location,
            [MarshalAs(UnmanagedType.Struct)] ref object locationRoot,
            int shellWindowClass,
            out int windowHandle,
            int options);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        object QueryService(ref Guid serviceGuid, ref Guid interfaceGuid);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void VTableGap01();
        void VTableGap02();
        void VTableGap03();
        void VTableGap04();
        void VTableGap05();
        void VTableGap06();
        void VTableGap07();
        void VTableGap08();
        void VTableGap09();
        void VTableGap10();
        void VTableGap11();
        void VTableGap12();
        IShellView QueryActiveShellView();
    }

    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void VTableGap01();
        void VTableGap02();
        void VTableGap03();
        void VTableGap04();
        void VTableGap05();
        void VTableGap06();
        void VTableGap07();
        void VTableGap08();
        void VTableGap09();
        void VTableGap10();
        void VTableGap11();
        void VTableGap12();

        [return: MarshalAs(UnmanagedType.Interface)]
        object GetItemObject(uint item, ref Guid interfaceGuid);
    }

    [ComImport]
    [Guid("E7A1AF80-4D96-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellFolderViewDual
    {
        object Application { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
    }

    [ComImport]
    [Guid("A4C6892C-3BA9-11D2-9DEA-00C04FB16162")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellDispatch2
    {
        void ShellExecute(
            [MarshalAs(UnmanagedType.BStr)] string file,
            [MarshalAs(UnmanagedType.Struct)] object arguments,
            [MarshalAs(UnmanagedType.Struct)] object directory,
            [MarshalAs(UnmanagedType.Struct)] object operation,
            [MarshalAs(UnmanagedType.Struct)] object showCommand);
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

        [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessAsUserW(SafeTokenHandle token, string applicationName,
            StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment,
            string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [DllImport("advapi32.dll", EntryPoint = "CreateProcessWithTokenW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessWithTokenW(SafeTokenHandle token, uint logonFlags,
            string applicationName, StringBuilder commandLine, uint creationFlags, IntPtr environment,
            string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

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

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
    }
}

internal sealed class UserProcessLaunchException(string stage, int nativeErrorCode, string message)
    : Win32Exception(nativeErrorCode, message)
{
    internal string Stage { get; } = stage;
}
