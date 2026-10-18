using System.Net;
using System.Reflection;
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
        try
        {
            if (!TryReadRequest(args, out var request))
            {
                Console.WriteLine(serializer.Serialize(new { status = "Failed", errorKind = "InvalidRequest" }));
                return 2;
            }

            var assemblyPath = MsiLiveUpdateAssemblyResolver.Resolve();
            if (assemblyPath is null)
            {
                Console.WriteLine(serializer.Serialize(new { status = "Unavailable", errorKind = "OemDllUnavailable" }));
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
            }
            catch (Exception exception)
            {
                Console.WriteLine(serializer.Serialize(new { status = "Unavailable", errorKind = SafeExceptionKind(exception) }));
                return 0;
            }

            var results = new List<object>();
            foreach (var category in Categories)
            {
                try
                {
                    var webRequest = (WebRequest?)requestMethod.Invoke(api,
                        [request.ProductTitle, request.CountryCode, category, request.OperatingSystem])
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

            Console.WriteLine(serializer.Serialize(new { status = "Completed", categories = results }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine(serializer.Serialize(new { status = "Failed", errorKind = SafeExceptionKind(exception) }));
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

    internal static string? Resolve()
    {
        foreach (var root in EnumerateRegisteredRoots())
        {
            var host = Path.Combine(root, LiveUpdateHostName);
            var api = Path.Combine(root, ApiAssemblyName);
            if (File.Exists(host) && File.Exists(api)) return api;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateRegisteredRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
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
                        AddRoot(roots, Path.GetDirectoryName(executable));
                }
            }

            using var uninstall = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is not null)
                AddRegisteredProductRoots(uninstall, roots);

            using var msi = localMachine.OpenSubKey(@"SOFTWARE\MSI\MSI Center M");
            AddMsiRegistryRoots(msi, roots);
        }

        using (var currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
        using (var uninstall = currentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
        {
            if (uninstall is not null) AddRegisteredProductRoots(uninstall, roots);
        }

        foreach (var root in roots) yield return root;
    }

    private static void AddRegisteredProductRoots(RegistryKey uninstall, HashSet<string> roots)
    {
        foreach (var subKeyName in uninstall.GetSubKeyNames())
        {
            using var product = uninstall.OpenSubKey(subKeyName);
            var displayName = product?.GetValue("DisplayName") as string;
            if (!string.Equals(displayName?.Trim(), "MSI Center M", StringComparison.OrdinalIgnoreCase)) continue;
            AddRoot(roots, product?.GetValue("InstallLocation") as string);
        }
    }

    private static void AddMsiRegistryRoots(RegistryKey? msi, HashSet<string> roots)
    {
        if (msi is null) return;
        foreach (var valueName in new[] { "InstallLocation", "InstallPath", "InstallDir", "Path" })
            AddRoot(roots, msi.GetValue(valueName) as string);
        using var liveUpdate = msi.OpenSubKey("LiveUpdate");
        if (liveUpdate is not null)
            foreach (var valueName in new[] { "InstallLocation", "InstallPath", "InstallDir", "Path" })
                AddRoot(roots, liveUpdate.GetValue(valueName) as string);
    }

    private static void AddRoot(HashSet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var normalized = Environment.ExpandEnvironmentVariables(path!.Trim().Trim('"'));
        if (!Path.IsPathRooted(normalized) || normalized.StartsWith(@"\\", StringComparison.Ordinal)) return;
        try
        {
            var fullPath = Path.GetFullPath(normalized);
            if (Directory.Exists(fullPath)) roots.Add(fullPath);
        }
        catch { }
    }

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
