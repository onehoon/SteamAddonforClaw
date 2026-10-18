using System.Net;
using System.Reflection;
using System.Security.AccessControl;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace SteamInputAddonforClaw.MsiLiveUpdateHelper;

internal static class Program
{
    private const int MaxRequestCharacters = 1024;
    private const int MaxResponseBytes = 1024 * 1024;
    private static readonly string[] Categories = ["bios", "firmware", "driver", "utility"];

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var serializer = new JavaScriptSerializer { MaxJsonLength = 12 * 1024 * 1024 };
        var tokenElevation = MsiLiveUpdateProcessDiagnostics.GetTokenElevation();
        var resolverDiagnostics = "NotRun";
        try
        {
            if (!TryReadRequest(args, out var request))
            {
                Console.WriteLine(serializer.Serialize(new { status = "Failed", errorKind = "InvalidRequest", helperTokenElevation = tokenElevation }));
                return 2;
            }

            var assemblyPath = MsiLiveUpdateAssemblyResolver.Resolve(out resolverDiagnostics);
            if (assemblyPath is null)
            {
                Console.WriteLine(serializer.Serialize(new
                {
                    status = "Unavailable",
                    errorKind = "OemDllUnavailable",
                    helperTokenElevation = tokenElevation,
                    resolverDiagnostics
                }));
                return 0;
            }

            Assembly assembly;
            Type apiType;
            MethodInfo requestMethod;
            object api;
            try
            {
                assembly = Assembly.LoadFrom(assemblyPath);
                if (!string.Equals(assembly.GetName().Name, "API_LiveUpdate", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("UnexpectedAssemblyIdentity");

                apiType = assembly.GetType("AWS_Query.ApiRequest", throwOnError: true)!;
                var parametersType = assembly.GetType("AWS_Query.AwsApiGatewayRequestParameters", throwOnError: true)!;
                var constructor = apiType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null, [parametersType], modifiers: null)
                    ?? throw new MissingMethodException("ApiRequest constructor is unavailable.");
                var parameters = Activator.CreateInstance(parametersType, nonPublic: true)
                    ?? throw new MissingMethodException("Default AWS request parameters are unavailable.");
                api = constructor.Invoke([parameters]);
                requestMethod = apiType.GetMethod("GetPostRequest", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null, [typeof(string), typeof(string), typeof(string), typeof(string)], modifiers: null)
                    ?? throw new MissingMethodException("The MSI catalog request method is unavailable.");
                if (requestMethod.ReturnType != typeof(WebRequest))
                    throw new MissingMethodException("The MSI catalog request method has an unexpected return type.");
                MsiLiveUpdateOemRequestReflection.ValidateGetPostRequest(requestMethod);
            }
            catch (Exception exception)
            {
                Console.WriteLine(serializer.Serialize(new
                {
                    status = "Unavailable",
                    errorKind = SafeExceptionKind(exception),
                    helperTokenElevation = tokenElevation,
                    resolverDiagnostics
                }));
                return 0;
            }

            var results = new List<object>();
            foreach (var category in Categories)
            {
                try
                {
                    var requestJson = MsiLiveUpdateOemRequestReflection.CreateRequestJson(
                        serializer.Serialize,
                        request.ProductTitle,
                        request.CountryCode,
                        category,
                        request.OperatingSystem);
                    MsiLiveUpdateOemRequestReflection.SetRequestJsonData(api, requestJson);
                    var webRequest = (WebRequest?)MsiLiveUpdateOemRequestReflection.InvokeGetPostRequest(
                        requestMethod,
                        api,
                        request.ProductTitle,
                        request.CountryCode,
                        category,
                        request.OperatingSystem)
                        ?? throw new InvalidOperationException("RequestCreationFailed");
                    using var response = webRequest.GetResponse();
                    var body = ReadBounded(response.GetResponseStream(), MaxResponseBytes);
                    var statusCode = response is HttpWebResponse http ? (int)http.StatusCode : 200;
                    results.Add(new { category, transportSucceeded = statusCode >= 200 && statusCode < 300, responseJson = body, errorKind = (string?)null });
                }
                catch (Exception exception)
                {
                    results.Add(new { category, transportSucceeded = false, responseJson = (string?)null, errorKind = SafeExceptionKind(exception) });
                }
            }

            Console.WriteLine(serializer.Serialize(new
            {
                status = "Completed",
                categories = results,
                helperTokenElevation = tokenElevation,
                resolverDiagnostics
            }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine(serializer.Serialize(new
            {
                status = "Failed",
                errorKind = SafeExceptionKind(exception),
                helperTokenElevation = tokenElevation,
                resolverDiagnostics
            }));
            return 1;
        }
    }

    private static bool TryReadRequest(string[] args, out CatalogRequest request)
    {
        request = default;
        if (args.Length != 3 || args.Any(static value => string.IsNullOrWhiteSpace(value) || value.Length > MaxRequestCharacters))
            return false;

        var productTitle = args[0].Trim();
        var countryCode = args[1].Trim();
        var operatingSystem = args[2].Trim();
        if (productTitle.Length == 0 || countryCode.Length != 2 || !countryCode.All(char.IsLetter)
            || operatingSystem is not ("win10" or "win11"))
            return false;

        request = new CatalogRequest(productTitle, countryCode.ToLowerInvariant(), operatingSystem);
        return true;
    }

    private static string ReadBounded(Stream? stream, int maximumBytes)
    {
        if (stream is null) throw new InvalidDataException("ResponseBodyUnavailable");
        using (stream)
        using (var memory = new MemoryStream())
        {
            var buffer = new byte[8192];
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                if (memory.Length + read > maximumBytes) throw new InvalidDataException("ResponseTooLarge");
                memory.Write(buffer, 0, read);
            }
            return new UTF8Encoding(false, true).GetString(memory.ToArray());
        }
    }

    private static string SafeExceptionKind(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: not null } invocation)
            exception = invocation.InnerException;
        return exception.GetType().Name;
    }

    private readonly struct CatalogRequest(string productTitle, string countryCode, string operatingSystem)
    {
        public string ProductTitle { get; } = productTitle;
        public string CountryCode { get; } = countryCode;
        public string OperatingSystem { get; } = operatingSystem;
    }
}

internal static class MsiLiveUpdateAssemblyResolver
{
    private const string LiveUpdateHostName = "MSI_Center_M_Server_LiveUpdate.exe";
    private const string ApiAssemblyName = "API_LiveUpdate.dll";
    private const int MaximumLoggedCandidates = 8;

    internal static string? Resolve(out string diagnostics)
    {
        (string Source, string Path)[] candidates;
        try
        {
            candidates = EnumerateRegisteredRoots().ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            diagnostics = "registry=AccessDenied";
            throw;
        }
        catch (SecurityException)
        {
            diagnostics = "registry=AccessDenied";
            throw;
        }
        catch
        {
            diagnostics = "registry=InspectionFailed";
            throw;
        }

        var inspected = new List<string>(Math.Min(candidates.Length, MaximumLoggedCandidates));
        string? resolvedAssembly = null;
        foreach (var candidate in candidates)
        {
            var result = InspectCandidate(candidate.Path, out var assemblyPath, out var scope, out var relativeRoot);
            var detail = $"{candidate.Source}:{scope}:{relativeRoot}:{result}";
            if (inspected.Count < MaximumLoggedCandidates)
            {
                inspected.Add(detail);
            }

            if (result != "Accepted") continue;
            if (inspected.Count == MaximumLoggedCandidates && !inspected.Contains(detail))
                inspected[MaximumLoggedCandidates - 1] = detail;
            resolvedAssembly = assemblyPath;
            break;
        }

        var omitted = Math.Max(0, candidates.Length - inspected.Count);
        diagnostics = $"registrations={candidates.Length};" +
            (inspected.Count == 0 ? "candidates=none" : "candidates=" + string.Join("|", inspected)) +
            (omitted == 0 ? string.Empty : $";notDetailed={omitted}");
        diagnostics = diagnostics.Length <= 1024 ? diagnostics : diagnostics.Substring(0, 1024);
        return resolvedAssembly;
    }

    private static IEnumerable<(string Source, string Path)> EnumerateRegisteredRoots()
    {
        var roots = new List<(string Source, string Path)>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            var viewName = view == RegistryView.Registry64 ? "64" : "32";
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var services = localMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is not null)
            {
                foreach (var serviceName in services.GetSubKeyNames())
                {
                    using var service = services.OpenSubKey(serviceName);
                    var imagePath = service?.GetValue("ImagePath") as string;
                    if (TryGetExecutablePath(imagePath, out var executable)
                        && string.Equals(Path.GetFileName(executable), LiveUpdateHostName, StringComparison.OrdinalIgnoreCase))
                        AddRoot(roots, Path.GetDirectoryName(executable), "Service" + viewName);
                }
            }

            using var uninstall = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is not null)
                AddRegisteredProductRoots(uninstall, roots, viewName);

            using var msi = localMachine.OpenSubKey(@"SOFTWARE\MSI\MSI Center M");
            AddMsiRegistryRoots(msi, roots, viewName);
        }

        foreach (var root in roots) yield return root;
    }

    private static void AddRegisteredProductRoots(RegistryKey uninstall, List<(string Source, string Path)> roots, string viewName)
    {
        foreach (var subKeyName in uninstall.GetSubKeyNames())
        {
            using var product = uninstall.OpenSubKey(subKeyName);
            var displayName = product?.GetValue("DisplayName") as string;
            if (!MsiLiveUpdateProductRegistration.IsSupportedDisplayName(displayName)) continue;
            AddRoot(roots, product?.GetValue("InstallLocation") as string, "Uninstall" + viewName);
        }
    }

    private static void AddMsiRegistryRoots(RegistryKey? msi, List<(string Source, string Path)> roots, string viewName)
    {
        if (msi is null) return;
        foreach (var valueName in new[] { "InstallLocation", "InstallPath", "InstallDir", "Path" })
            AddRoot(roots, msi.GetValue(valueName) as string, "MsiRegistry" + viewName);
        using var liveUpdate = msi.OpenSubKey("LiveUpdate");
        if (liveUpdate is not null)
            foreach (var valueName in new[] { "InstallLocation", "InstallPath", "InstallDir", "Path" })
                AddRoot(roots, liveUpdate.GetValue(valueName) as string, "MsiRegistry" + viewName);
    }

    private static void AddRoot(List<(string Source, string Path)> roots, string? path, string source)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        roots.Add((source, Environment.ExpandEnvironmentVariables(path!.Trim().Trim('"'))));
    }

    private static string InspectCandidate(string candidatePath, out string? assemblyPath, out string scope, out string relativeRoot)
    {
        assemblyPath = null;
        scope = "Unknown";
        relativeRoot = "-";
        string fullPath;
        try
        {
            if (!Path.IsPathRooted(candidatePath) || candidatePath.StartsWith(@"\\", StringComparison.Ordinal))
                return "InvalidPath";
            fullPath = NormalizePath(candidatePath);
        }
        catch
        {
            return "InvalidPath";
        }

        var trustedRoots = new[]
        {
            (Scope: "ProgramFiles", Path: Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
            (Scope: "ProgramFilesX86", Path: Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
        };

        string? trustedRoot = null;
        foreach (var trustedRootCandidate in trustedRoots)
        {
            if (string.IsNullOrWhiteSpace(trustedRootCandidate.Path)) continue;
            try
            {
                var normalizedTrustedRoot = NormalizePath(trustedRootCandidate.Path);
                if (!IsPathWithin(fullPath, normalizedTrustedRoot)) continue;
                scope = trustedRootCandidate.Scope;
                trustedRoot = normalizedTrustedRoot;
                relativeRoot = SanitizeRelativePath(fullPath.Substring(trustedRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                break;
            }
            catch
            {
                return "PathInspectionFailed";
            }
        }

        if (trustedRoot is null) return "OutsideProgramFiles";

        var directoryPresence = CheckDirectoryPresence(fullPath);
        if (directoryPresence != "Present") return directoryPresence;

        var rootSafety = CheckDirectoryAncestorsProtected(fullPath, trustedRoot);
        if (rootSafety != "Protected") return rootSafety;

        var host = Path.Combine(fullPath, LiveUpdateHostName);
        var api = Path.Combine(fullPath, ApiAssemblyName);
        var hostSafety = CheckProtectedRegularFile(host);
        if (hostSafety != "Protected") return "Host" + hostSafety;
        var apiSafety = CheckProtectedRegularFile(api);
        if (apiSafety != "Protected") return "Api" + apiSafety;

        assemblyPath = api;
        return "Accepted";
    }

    private static string CheckDirectoryPresence(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0 ? "Present" : "NotDirectory";
        }
        catch (FileNotFoundException) { return "DirectoryMissing"; }
        catch (DirectoryNotFoundException) { return "DirectoryMissing"; }
        catch (UnauthorizedAccessException) { return "DirectoryAccessDenied"; }
        catch (SecurityException) { return "DirectoryAccessDenied"; }
        catch { return "DirectoryInspectionFailed"; }
    }

    private static string CheckDirectoryAncestorsProtected(string path, string trustedRoot)
    {
        HashSet<string> unprivilegedSids;
        try { unprivilegedSids = MsiLiveUpdatePathSecurity.GetUnelevatedUserSids(); }
        catch (UnauthorizedAccessException) { return "TokenAccessDenied"; }
        catch (SecurityException) { return "TokenAccessDenied"; }
        catch { return "TokenInspectionFailed"; }

        try
        {
            DirectoryInfo? current = new DirectoryInfo(path);
            while (current is not null)
            {
                var safety = CheckDirectory(current, unprivilegedSids);
                if (safety != "Protected") return safety;
                if (string.Equals(NormalizePath(current.FullName), trustedRoot, StringComparison.OrdinalIgnoreCase))
                    return "Protected";
                current = current.Parent;
            }
            return "DirectoryChainIncomplete";
        }
        catch (UnauthorizedAccessException) { return "DirectoryAccessDenied"; }
        catch (SecurityException) { return "DirectoryAccessDenied"; }
        catch { return "DirectoryInspectionFailed"; }
    }

    private static string CheckDirectory(DirectoryInfo directory, HashSet<string> unprivilegedSids)
    {
        try
        {
            var attributes = File.GetAttributes(directory.FullName);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                return (attributes & FileAttributes.ReparsePoint) != 0 ? "DirectoryReparsePoint" : "NotDirectory";

            var security = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            return MsiLiveUpdatePathSecurity.IsFileSystemObjectProtected(security, unprivilegedSids)
                ? "Protected"
                : "DirectoryAclWritable";
        }
        catch (UnauthorizedAccessException) { return "DirectoryAccessDenied"; }
        catch (SecurityException) { return "DirectoryAccessDenied"; }
        catch { return "DirectoryInspectionFailed"; }
    }

    private static string CheckProtectedRegularFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return "ReparsePoint";
            if ((attributes & FileAttributes.Directory) != 0) return "NotRegularFile";

            var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            return MsiLiveUpdatePathSecurity.IsFileSystemObjectProtected(
                security,
                MsiLiveUpdatePathSecurity.GetUnelevatedUserSids())
                ? "Protected"
                : "AclWritable";
        }
        catch (FileNotFoundException) { return "Missing"; }
        catch (DirectoryNotFoundException) { return "Missing"; }
        catch (UnauthorizedAccessException) { return "AccessDenied"; }
        catch (SecurityException) { return "AccessDenied"; }
        catch { return "InspectionFailed"; }
    }

    private static string SanitizeRelativePath(string path) =>
        new string(path.Select(character => char.IsLetterOrDigit(character) || character is ' ' or '.' or '_' or '-' or '\\' or '/'
            ? character : '_').Take(96).ToArray());

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var pathRoot = Path.GetPathRoot(fullPath) ?? string.Empty;
        return fullPath.Length > pathRoot.Length
            ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : fullPath;
    }

    private static bool IsPathWithin(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetExecutablePath(string? imagePath, out string executable)
    {
        executable = string.Empty;
        if (string.IsNullOrWhiteSpace(imagePath)) return false;
        var value = Environment.ExpandEnvironmentVariables(imagePath!.Trim());
        if (value.Length > 0 && value[0] == '"')
        {
            var endQuote = value.IndexOf('"', 1);
            if (endQuote <= 1) return false;
            executable = value.Substring(1, endQuote - 1);
        }
        else
        {
            var exeEnd = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeEnd < 0) return false;
            executable = value.Substring(0, exeEnd + 4).Trim();
        }
        return Path.IsPathRooted(executable) && !executable.StartsWith(@"\\", StringComparison.Ordinal);
    }
}

internal static class MsiLiveUpdateProcessDiagnostics
{
    internal static string GetTokenElevation()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
                ? "Administrator"
                : "NotElevated";
        }
        catch
        {
            return "Unknown";
        }
    }
}
