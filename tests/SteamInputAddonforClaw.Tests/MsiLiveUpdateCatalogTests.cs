using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.MSI.LiveUpdate;
using SteamInputAddonforClaw.MsiLiveUpdateHelper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiLiveUpdateCatalogTests
{
    private static readonly MsiLiveUpdateCatalogRequest Request = new("Claw 8 AI+ A2VM", "kr", "win11");
    private static readonly DateTimeOffset CheckedAt = new(2026, 10, 18, 12, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(" Claw 8 AI+ A2VM/MT- ", "Claw 8 AI+ A2VM")]
    [InlineData("Claw 8 AI+ A2VM\\SKU", "Claw 8 AI+ A2VM")]
    [InlineData("  Claw 8 AI+ A2VM  ", "Claw 8 AI+ A2VM")]
    public void Product_title_uses_exact_WMI_value_with_suffix_removed(string input, string expected)
    {
        Assert.Equal(expected, MsiLiveUpdateCatalogRequestIdentity.NormalizeProductTitle(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/MT-")]
    [InlineData("\\SKU")]
    public void Missing_product_title_fails_closed(string? input)
    {
        Assert.False(MsiLiveUpdateCatalogRequestIdentity.TryCreate(input, "ko-KR", 26100, out var request, out var error));
        Assert.Null(request);
        Assert.Equal("ProductTitleUnavailable", error);
    }

    [Fact]
    public void Request_uses_current_culture_country_and_Windows_OS_token()
    {
        Assert.True(MsiLiveUpdateCatalogRequestIdentity.TryCreate("Claw 8 AI+ A2VM", "ko-KR", 26100, out var win11, out _));
        Assert.Equal(new("Claw 8 AI+ A2VM", "kr", "win11"), win11);
        Assert.True(MsiLiveUpdateCatalogRequestIdentity.TryCreate("Claw 7 AI+ A2VM", "en-US", 19045, out var win10, out _));
        Assert.Equal(new("Claw 7 AI+ A2VM", "us", "win10"), win10);
    }

    [Fact]
    public void Country_without_region_fails_closed()
    {
        Assert.False(MsiLiveUpdateCatalogRequestIdentity.TryCreate("Claw 8 AI+ A2VM", "", 26100, out _, out var error));
        Assert.Equal("CountryCodeUnavailable", error);
    }

    [Theory]
    [InlineData(FrontendHardwareStatus.Supported, "msi.claw", "msi.claw.a2vm.7", true)]
    [InlineData(FrontendHardwareStatus.Supported, "msi.claw", "msi.claw.a2vm.8", true)]
    [InlineData(FrontendHardwareStatus.Supported, "msi.claw", "msi.claw.cg3em", true)]
    [InlineData(FrontendHardwareStatus.Supported, "msi.claw", "unknown", false)]
    [InlineData(FrontendHardwareStatus.Indeterminate, "msi.claw", "msi.claw.a2vm.8", false)]
    [InlineData(FrontendHardwareStatus.Supported, "other", "msi.claw.a2vm.8", false)]
    public void Device_policy_only_allows_resolved_supported_models(FrontendHardwareStatus status, string family, string model, bool expected)
    {
        Assert.Equal(expected, MsiLiveUpdateCatalogDevicePolicy.IsSupported(status, family, model));
    }

    [Fact]
    public void Parser_preserves_raw_catalog_rows_and_example_category_counts()
    {
        var bios = JsonSerializer.Serialize(new object[]
        {
            new { name = "Intel ME FW Update Tool", version = "ME20011.1815(LNL)", release_date = "2026-08-07" },
            new { title = "BIOS", displayVersion = "E1T52IMS.114", releaseDate = "2026-09-17", description = "Published BIOS record" }
        });
        var firmware = JsonSerializer.Serialize(new[] { new { name = "Controller Firmware", version = "2.30" } });
        var drivers = JsonSerializer.Serialize(Enumerable.Range(1, 21).Select(index => new { name = $"Driver {index}", version = "1.0" }));
        var utilities = JsonSerializer.Serialize(Enumerable.Range(1, 7).Select(index => new { name = $"Utility {index}", displayVersion = "2.0" }));
        var result = Parse(HelperOutput(("bios", bios), ("firmware", firmware), ("driver", drivers), ("utility", utilities)));

        Assert.Equal(MsiLiveUpdateCatalogState.Completed, result.State);
        Assert.Equal(new[] { 2, 1, 21, 7 }, result.Categories.Select(category => category.Entries.Count));
        Assert.Equal("Intel ME FW Update Tool", result.Categories[0].Entries[0].Name);
        Assert.Equal("BIOS", result.Categories[0].Entries[1].Name);
        Assert.Equal("ME20011.1815(LNL)", result.Categories[0].Entries[0].Version);
        Assert.Equal("E1T52IMS.114", result.Categories[0].Entries[1].Version);
    }

    [Fact]
    public void Parser_accepts_SoftwareRecord_wrapper_and_missing_optional_fields()
    {
        var wrapper = "{\"SoftwareRecord\":[{\"name\":\"Record\",\"extra\":true}]}";
        var result = Parse(HelperOutput(("bios", wrapper), ("firmware", "[]"), ("driver", "[]"), ("utility", "[]")));

        Assert.Equal(MsiLiveUpdateCatalogState.Completed, result.State);
        Assert.Equal(MsiLiveUpdateCatalogCategoryState.Success, result.Categories[0].State);
        Assert.Null(result.Categories[0].Entries[0].Version);
        Assert.Null(result.Categories[0].Entries[0].ReleaseDate);
        Assert.Null(result.Categories[0].Entries[0].Description);
        Assert.Equal(MsiLiveUpdateCatalogCategoryState.Empty, result.Categories[1].State);
    }

    [Fact]
    public void Parser_preserves_duplicate_catalog_names()
    {
        var result = Parse(HelperOutput(
            ("bios", "[]"),
            ("firmware", "[]"),
            ("driver", "[{\"name\":\"Same name\"},{\"name\":\"Same name\"}]"),
            ("utility", "[]")));

        Assert.Equal(2, result.Categories[2].Entries.Count);
        Assert.Equal(new[] { "Same name", "Same name" }, result.Categories[2].Entries.Select(entry => entry.Name));
    }

    [Theory]
    [InlineData("false", "FalseSentinel")]
    [InlineData("{", "MalformedCatalogResponse")]
    [InlineData("null", "UnexpectedResponseShape")]
    [InlineData("true", "UnexpectedResponseShape")]
    [InlineData("{}", "UnexpectedResponseShape")]
    [InlineData("[null]", "InvalidCatalogEntry")]
    public void Parser_classifies_false_and_malformed_shapes_as_failures(string json, string expectedError)
    {
        var result = Parse(HelperOutput(("bios", json), ("firmware", "[]"), ("driver", "[]"), ("utility", "[]")));

        Assert.Equal(MsiLiveUpdateCatalogState.PartialFailure, result.State);
        Assert.Equal(MsiLiveUpdateCatalogCategoryState.Failed, result.Categories[0].State);
        Assert.Equal(expectedError, result.Categories[0].ErrorKind);
        Assert.Equal(MsiLiveUpdateCatalogCategoryState.Empty, result.Categories[1].State);
    }

    [Fact]
    public void Category_failure_preserves_successful_siblings_and_is_partial()
    {
        var result = Parse(HelperOutput(("bios", "[]"), ("firmware", "false"), ("driver", "[{\"name\":\"Driver\"}]"), ("utility", "[]")));

        Assert.Equal(MsiLiveUpdateCatalogState.PartialFailure, result.State);
        Assert.Equal(MsiLiveUpdateCatalogCategoryState.Empty, result.Categories[0].State);
        Assert.Equal(MsiLiveUpdateCatalogCategoryState.Failed, result.Categories[1].State);
        Assert.Single(result.Categories[2].Entries);
        Assert.Equal(MsiLiveUpdateCatalogCategoryState.Empty, result.Categories[3].State);
    }

    [Fact]
    public void Missing_category_is_failure_not_empty()
    {
        var output = "{\"status\":\"Completed\",\"categories\":[{\"category\":\"bios\",\"transportSucceeded\":true,\"responseJson\":\"[]\"}]}";
        var result = Parse(output);

        Assert.Equal(MsiLiveUpdateCatalogState.PartialFailure, result.State);
        Assert.Equal(MsiLiveUpdateCatalogCategoryState.Empty, result.Categories[0].State);
        Assert.All(result.Categories.Skip(1), category => Assert.Equal(MsiLiveUpdateCatalogCategoryState.Failed, category.State));
    }

    [Fact]
    public void Helper_unavailable_is_distinct_from_empty_catalog()
    {
        var result = Parse("{\"status\":\"Unavailable\",\"errorKind\":\"OemDllUnavailable\"}");

        Assert.Equal(MsiLiveUpdateCatalogState.Unavailable, result.State);
        Assert.Null(result.CheckedAtUtc);
        Assert.Empty(result.Categories);
    }

    [Fact]
    public void Invalid_helper_json_fails_without_throwing()
    {
        var result = Parse("not-json");

        Assert.Equal(MsiLiveUpdateCatalogState.Failed, result.State);
        Assert.Equal("MalformedHelperResponse", result.ErrorKind);
    }

    [Fact]
    public async Task Query_does_not_run_when_product_title_is_missing()
    {
        var invocationCount = 0;
        var client = new MsiLiveUpdateCatalogClient(
            productNameSource: () => null,
            cultureNameSource: () => "ko-KR",
            windowsBuildSource: () => 26100,
            query: (_, _) => { invocationCount++; return Task.FromResult(Parse("{}")); });

        var result = await client.QueryAsync(CancellationToken.None);

        Assert.Equal(MsiLiveUpdateCatalogState.Unavailable, result.State);
        Assert.Equal("ProductTitleUnavailable", result.ErrorKind);
        Assert.Equal(0, invocationCount);
    }

    [Fact]
    public async Task Query_uses_the_WMI_marketing_title_and_normalized_request_identity()
    {
        MsiLiveUpdateCatalogRequest? observed = null;
        var client = new MsiLiveUpdateCatalogClient(
            productNameSource: () => "Claw 8 AI+ A2VM/MT-1T52",
            cultureNameSource: () => "ko-KR",
            windowsBuildSource: () => 26100,
            query: (request, _) =>
            {
                observed = request;
                return Task.FromResult(new MsiLiveUpdateCatalogQueryResult(
                    MsiLiveUpdateCatalogState.Completed, request.ProductTitle, request.CountryCode,
                    request.OperatingSystem, [], CheckedAt));
            });

        var result = await client.QueryAsync(CancellationToken.None);

        Assert.Equal(new("Claw 8 AI+ A2VM", "kr", "win11"), observed);
        Assert.Equal("Claw 8 AI+ A2VM", result.ProductTitle);
    }

    [Fact]
    public async Task Query_failures_return_only_a_bounded_failure_kind_without_exception_message()
    {
        var client = new MsiLiveUpdateCatalogClient(
            productNameSource: () => "Claw 8 AI+ A2VM",
            cultureNameSource: () => "ko-KR",
            windowsBuildSource: () => 26100,
            query: (_, _) => Task.FromException<MsiLiveUpdateCatalogQueryResult>(
                new TimeoutException("Authorization Bearer secret must not escape")));

        var result = await client.QueryAsync(CancellationToken.None);

        Assert.Equal(MsiLiveUpdateCatalogState.Failed, result.State);
        Assert.Equal("TimeoutException", result.ErrorKind);
        Assert.DoesNotContain("secret", result.ErrorKind, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_helper_is_unavailable_and_never_falls_back_to_a_web_request()
    {
        var client = new MsiLiveUpdateCatalogClient(
            productNameSource: () => "Claw 8 AI+ A2VM",
            cultureNameSource: () => "ko-KR",
            windowsBuildSource: () => 26100,
            helperPathSource: () => Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"));

        var result = await client.QueryAsync(CancellationToken.None);

        Assert.Equal(MsiLiveUpdateCatalogState.Unavailable, result.State);
        Assert.Equal("HelperUnavailable", result.ErrorKind);
        Assert.Empty(result.Categories);
    }

    [Fact]
    public async Task Helper_launch_failure_is_a_bounded_failed_result_without_path_details()
    {
        var client = new MsiLiveUpdateCatalogClient(
            productNameSource: () => "Claw 8 AI+ A2VM",
            cultureNameSource: () => "ko-KR",
            windowsBuildSource: () => 26100,
            helperPathSource: () => typeof(MsiLiveUpdateCatalogTests).Assembly.Location);

        var result = await client.QueryAsync(CancellationToken.None);

        Assert.Equal(MsiLiveUpdateCatalogState.Failed, result.State);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorKind));
        Assert.DoesNotContain(Path.GetDirectoryName(typeof(MsiLiveUpdateCatalogTests).Assembly.Location)!, result.ErrorKind, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nonzero_helper_exit_is_failed_and_does_not_return_process_output()
    {
        var result = MsiLiveUpdateCatalogClient.FailedForHelperExit(17, Request);

        Assert.Equal(MsiLiveUpdateCatalogState.Failed, result.State);
        Assert.Equal("HelperProcessFailed", result.ErrorKind);
        Assert.Empty(result.Categories);
    }

    [Fact]
    public async Task Helper_timeout_kills_the_child_and_returns_a_bounded_failure()
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.True(File.Exists(powershell));
        var startInfo = new ProcessStartInfo
        {
            FileName = powershell,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Start-Sleep -Seconds 30");

        var result = await MsiLiveUpdateCatalogClient.RunHelperProcessAsync(
            startInfo, Request, TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.Equal(MsiLiveUpdateCatalogState.Failed, result.State);
        Assert.Equal("HelperTimedOut", result.ErrorKind);
        Assert.Empty(result.Categories);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_to_the_helper_query_without_becoming_empty_data()
    {
        using var cancellation = new CancellationTokenSource();
        var queryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new MsiLiveUpdateCatalogClient(
            productNameSource: () => "Claw 8 AI+ A2VM",
            cultureNameSource: () => "ko-KR",
            windowsBuildSource: () => 26100,
            query: async (_, token) =>
            {
                queryStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new(MsiLiveUpdateCatalogState.Completed, null, null, null, [], CheckedAt);
            });

        var operation = client.QueryAsync(cancellation.Token);
        await queryStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Theory]
    [InlineData((int)MsiLiveUpdateCatalogState.Completed, (int)FrontendMsiLiveUpdateCatalogState.Completed)]
    [InlineData((int)MsiLiveUpdateCatalogState.PartialFailure, (int)FrontendMsiLiveUpdateCatalogState.PartialFailure)]
    [InlineData((int)MsiLiveUpdateCatalogState.Unavailable, (int)FrontendMsiLiveUpdateCatalogState.Unavailable)]
    [InlineData((int)MsiLiveUpdateCatalogState.Failed, (int)FrontendMsiLiveUpdateCatalogState.Failed)]
    public void Runtime_contract_mapper_preserves_distinct_catalog_states(int sourceValue, int expectedValue)
    {
        var mapped = MsiLiveUpdateCatalogContractMapper.Map(
            new((MsiLiveUpdateCatalogState)sourceValue, Request.ProductTitle, Request.CountryCode, Request.OperatingSystem, [], CheckedAt));

        Assert.Equal(expectedValue, (int)mapped.State);
        Assert.Equal(CheckedAt, mapped.CheckedAtUtc);
    }

    [Fact]
    public void Oem_request_body_uses_the_verified_json_fields_and_values()
    {
        var body = MsiLiveUpdateOemRequestReflection.CreateRequestJson(
            value => JsonSerializer.Serialize(value),
            "Claw 8 AI+ A2VM",
            "kr",
            "driver",
            "win11");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal("Claw 8 AI+ A2VM", root.GetProperty("product_title").GetString());
        Assert.Equal("kr", root.GetProperty("country_code").GetString());
        Assert.Equal("driver", root.GetProperty("type").GetString());
        Assert.Equal("win11", root.GetProperty("os").GetString());
    }

    [Fact]
    public void Oem_reflection_invocation_maps_the_verified_header_arguments_in_order()
    {
        var api = new FakeOemApi();
        var method = typeof(FakeOemApi).GetMethod(nameof(FakeOemApi.GetPostRequest), BindingFlags.Instance | BindingFlags.Public)!;

        var result = MsiLiveUpdateOemRequestReflection.InvokeGetPostRequest(
            method,
            api,
            "Claw 8 AI+ A2VM",
            "kr",
            "utility",
            "win11");

        Assert.Equal("request-created", result);
        Assert.Equal(["Claw 8 AI+ A2VM", "kr", "utility", "win11"], api.Arguments!);
    }

    [Fact]
    public void Oem_request_body_is_set_on_the_signing_configuration_before_transport_invocation()
    {
        var api = new FakeOemApi();

        MsiLiveUpdateOemRequestReflection.SetRequestJsonData(api, "{\"type\":\"bios\"}");

        Assert.Equal("{\"type\":\"bios\"}", api.AwsApiGatewayRequest.JsonData);
    }

    [Fact]
    public void Elevated_helper_uses_tested_oem_request_contract_and_rejects_user_registry_candidates()
    {
        var source = ReadHelperSource();

        Assert.Contains("MsiLiveUpdateOemRequestReflection.CreateRequestJson", source, StringComparison.Ordinal);
        Assert.Contains("MsiLiveUpdateOemRequestReflection.SetRequestJsonData(api, requestJson)", source, StringComparison.Ordinal);
        Assert.Contains("MsiLiveUpdateOemRequestReflection.InvokeGetPostRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("requestMethod.Invoke(api", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RegistryHive.CurrentUser", source, StringComparison.Ordinal);
        Assert.Contains("Directory.Exists(fullPath) && IsTrustedInstallationRoot(fullPath)", source, StringComparison.Ordinal);
        Assert.Contains("FileAttributes.ReparsePoint", source, StringComparison.Ordinal);
        Assert.Contains("GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)", source, StringComparison.Ordinal);
        Assert.Contains("IsProtectedRegularFile(host)", source, StringComparison.Ordinal);
        Assert.Contains("IsProtectedRegularFile(api)", source, StringComparison.Ordinal);
        Assert.Contains("new FileInfo(path).GetAccessControl", source, StringComparison.Ordinal);
    }

    private static MsiLiveUpdateCatalogQueryResult Parse(string output) =>
        MsiLiveUpdateCatalogResultParser.ParseHelperOutput(output, Request, CheckedAt);

    private static string HelperOutput(params (string Category, string Body)[] responses)
    {
        var categories = responses.Select(response => new
        {
            category = response.Category,
            transportSucceeded = true,
            responseJson = response.Body,
            errorKind = (string?)null
        });
        return JsonSerializer.Serialize(new { status = "Completed", categories });
    }

    private static string ReadHelperSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "SteamInputAddonforClaw.MsiLiveUpdateHelper", "Program.cs"));
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class FakeOemApi
    {
        public FakeOemRequestConfiguration AwsApiGatewayRequest { get; } = new();
        public string[]? Arguments { get; private set; }

        public string GetPostRequest(string product_title, string country_code, string type, string os)
        {
            Arguments = [product_title, country_code, type, os];
            return "request-created";
        }
    }

    private sealed class FakeOemRequestConfiguration
    {
        public string JsonData { get; set; } = string.Empty;
    }
}
