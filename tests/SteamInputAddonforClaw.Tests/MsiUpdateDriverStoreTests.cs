using System.Text.Json;
using SteamInputAddonforClaw.Devices.MSI.LiveUpdate;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiUpdateDriverStoreTests
{
    [Fact]
    public void Powershell_projection_uses_the_Dism_object_property_names_and_never_emits_paths()
    {
        var script = MsiUpdateInstalledInventory.DriverStorePowerShellScript;

        Assert.Contains("Get-WindowsDriver -Online -ErrorAction Stop", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-All", script, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, script.Split("Get-WindowsDriver", StringSplitOptions.None).Length - 1);
        Assert.Contains("$_.Driver", script, StringComparison.Ordinal);
        Assert.Contains("$_.OriginalFileName", script, StringComparison.Ordinal);
        Assert.Contains("$_.ProviderName", script, StringComparison.Ordinal);
        Assert.Contains("$_.ClassName", script, StringComparison.Ordinal);
        Assert.Contains("$_.Version", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$_.PublishedName", script, StringComparison.Ordinal);
        Assert.Contains("[IO.Path]::GetFileName", script, StringComparison.Ordinal);
        Assert.DoesNotContain("FileRepository", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DriverStore_timeout_and_process_cleanup_policies_are_short_and_finite()
    {
        Assert.Equal(8_000, MsiUpdateInstalledInventory.DriverStoreTimeoutMilliseconds);
        Assert.InRange(MsiUpdateInstalledInventory.DriverStoreProcessCleanupGraceMilliseconds, 1, 1_000);
    }

    [Fact]
    public void DriverStore_capture_records_bounded_elapsed_for_success_timeout_and_command_failure()
    {
        var results = new[]
        {
            MsiUpdateInstalledInventory.ParseDriverStoreProcessResult("{\"Packages\":[]}", 0),
            MsiUpdateInstalledInventory.ParseDriverStoreProcessResult("{}", 0, timedOut: true),
            MsiUpdateInstalledInventory.ParseDriverStoreProcessResult("{}", 5)
        };

        Assert.Equal(new[] { "Completed", "TimedOut", "Unavailable" }, results.Select(result => result.Status));
        Assert.All(results, result => Assert.Equal(1_234,
            MsiUpdateInstalledInventory.WithDriverStoreElapsed(result, TimeSpan.FromMilliseconds(1_234)).ElapsedMs));
        Assert.Equal(0, MsiUpdateInstalledInventory.WithDriverStoreElapsed(results[0], TimeSpan.FromMilliseconds(-1)).ElapsedMs);
        Assert.Equal(MsiUpdateInstalledInventory.MaximumDriverStoreElapsedMilliseconds,
            MsiUpdateInstalledInventory.WithDriverStoreElapsed(results[0],
                TimeSpan.FromMilliseconds(MsiUpdateInstalledInventory.MaximumDriverStoreElapsedMilliseconds + 1)).ElapsedMs);
        Assert.Null(MsiUpdateDriverStoreCapture.NotRequested.ElapsedMs);
    }

    [Theory]
    [InlineData("msi.claw.cg3em", "Completed", true)]
    [InlineData("msi.claw.cg3em", "TimedOut", true)]
    [InlineData("msi.claw.cg3em", "Unavailable", true)]
    [InlineData("msi.claw.a2vm.8", "Completed", false)]
    [InlineData("msi.claw.cg3em", "NotRequested", false)]
    public void DriverStore_capture_log_is_limited_to_requested_CG3EM_scans(
        string model, string status, bool expected)
    {
        Assert.Equal(expected, MsiUpdateInstalledInventory.ShouldLogDriverStoreCapture(
            model, new(status, [])));
    }

    [Theory]
    [InlineData("msi.claw.cg3em", true, "Capture")]
    [InlineData("MSI.CLAW.CG3EM", true, "Capture")]
    [InlineData("msi.claw.cg3em", false, "UnsupportedPlatform")]
    [InlineData("msi.claw.a2vm.7", true, "NotRequested")]
    [InlineData("msi.claw.a2vm.8", true, "NotRequested")]
    [InlineData(null, true, "NotRequested")]
    public void DriverStore_capture_is_scoped_to_supported_CG3EM_and_Windows(
        string? model, bool isWindows, string expected)
    {
        Assert.Equal(expected, MsiUpdateInstalledInventory.GetDriverStoreCaptureDisposition(model, isWindows));
    }

    [Fact]
    public void Parser_reads_a_list_case_insensitively_and_normalizes_original_INF_basename()
    {
        const string json = """
            {"packages":[
              {"publishedinfname":"oem677.inf","originalinfname":"C:\\Windows\\System32\\DriverStore\\FileRepository\\ishheci.inf","provider":"Intel","class":"System","version":"5.8.61.0"},
              {"PublishedInfName":"oem755.inf","OriginalInfName":"IntcOED_OemLibPath.inf","Provider":"Realtek Semiconductor Corp.","Class":"Extension","Version":"20.43.274.009"},
              {"PublishedInfName":"oem900.inf","OriginalInfName":"unrelated.inf","Provider":"Private Publisher","Class":"System","Version":"1.0.0.0"}
            ],"overflow":false}
            """;

        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesJson(json);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(2, result.Packages.Count);
        Assert.Contains(result.Packages, package => package.OriginalInfName == "ishheci.inf"
            && package.PublishedInfName == "oem677.inf" && package.Version == "5.8.61.0");
        Assert.DoesNotContain(result.Packages, package => package.Provider == "Private Publisher");
    }

    [Fact]
    public void Parser_accepts_a_single_projected_package_object()
    {
        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesJson("""
            {"PublishedInfName":"oem1.inf","OriginalInfName":"RtkSdcaXuMSI.inf","Provider":"Realtek","Class":"Extension","Version":"10.0.378.1"}
            """);

        var package = Assert.Single(result.Packages);
        Assert.Equal("RtkSdcaXuMSI.inf", package.OriginalInfName);
        Assert.Equal("Completed", result.Status);
    }

    [Fact]
    public void Parser_accepts_an_empty_package_array_as_a_successful_no_match_inventory()
    {
        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesJson("{\"Packages\":[]}");

        Assert.Equal("Completed", result.Status);
        Assert.Empty(result.Packages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parser_reports_empty_output_without_fabricating_an_empty_inventory(string json)
    {
        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesJson(json);

        Assert.Equal("EmptyOutput", result.Status);
        Assert.Equal("Cg3emDriverStoreEmptyOutput", result.Warning);
        Assert.Empty(result.Packages);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"packages\":[{\"OriginalInfName\":null}]}")]
    [InlineData("{\"packages\":[{\"OriginalInfName\":\"ishheci.inf\",\"PublishedInfName\":\"oem1.inf\",\"Provider\":null,\"Class\":\"System\",\"Version\":\"5.8.61.0\"}]}")]
    [InlineData("{\"packages\":[{\"OriginalInfName\":\"ishheci.inf\",\"PublishedInfName\":\"not-a-published-inf\",\"Provider\":\"Intel\",\"Class\":\"System\",\"Version\":\"5.8.61.0\"}]}")]
    [InlineData("{\"packages\":[{\"OriginalInfName\":\"ishheci.inf\",\"PublishedInfName\":\"oem1.inf\",\"Provider\":\"Intel\",\"Class\":\"System\",\"Version\":\"unknown\"}]}")]
    public void Parser_fails_closed_for_malformed_or_incomplete_allowlisted_rows(string json)
    {
        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesJson(json);

        Assert.Equal("InvalidOutput", result.Status);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public void Parser_rejects_json_over_the_output_limit()
    {
        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesJson(new string(' ', 64 * 1024 + 1));

        Assert.Equal("OutputLimitExceeded", result.Status);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public void Parser_rejects_excessive_rows_and_explicit_overflow()
    {
        var row = new
        {
            PublishedInfName = "oem1.inf",
            OriginalInfName = "ishheci.inf",
            Provider = "Intel",
            Class = "System",
            Version = "5.8.61.0"
        };
        var tooMany = JsonSerializer.Serialize(Enumerable.Repeat(row, 129));
        var tooManyResult = MsiUpdateInstalledInventory.ParseDriverStorePackagesJson(tooMany);
        var overflowResult = MsiUpdateInstalledInventory.ParseDriverStorePackagesJson("""
            {"Overflow":true,"Packages":[]}
            """);

        Assert.Equal("RowLimitExceeded", tooManyResult.Status);
        Assert.Equal("RowLimitExceeded", overflowResult.Status);
        Assert.Empty(tooManyResult.Packages);
        Assert.Empty(overflowResult.Packages);
    }

    [Fact]
    public void Parser_preserves_duplicate_driver_packages_for_conservative_matching()
    {
        const string json = """
            {"Packages":[
              {"PublishedInfName":"oem1.inf","OriginalInfName":"ishheci.inf","Provider":"Intel","Class":"System","Version":"5.8.61.0"},
              {"PublishedInfName":"oem2.inf","OriginalInfName":"ishheci.inf","Provider":"Intel","Class":"System","Version":"5.8.62.0"}
            ]}
            """;

        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesJson(json);

        Assert.Equal(2, result.Packages.Count);
        Assert.Equal(new[] { "5.8.61.0", "5.8.62.0" }, result.Packages.Select(package => package.Version));
    }

    [Fact]
    public async Task Inventory_capture_honors_pre_cancelled_manual_request()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MsiUpdateInstalledInventory.CaptureAsync(null, cancellation.Token, "msi.claw.cg3em"));
    }

    [Fact]
    public void Process_result_fails_closed_on_timeout_output_limit_and_nonzero_exit()
    {
        Assert.Equal("TimedOut", MsiUpdateInstalledInventory.ParseDriverStoreProcessResult("{}", 0, timedOut: true).Status);
        Assert.Equal("OutputLimitExceeded", MsiUpdateInstalledInventory.ParseDriverStoreProcessResult("{}", 0,
            outputLimitExceeded: true).Status);
        Assert.Equal("Unavailable", MsiUpdateInstalledInventory.ParseDriverStoreProcessResult("{}", 5).Status);
        Assert.Equal("Completed", MsiUpdateInstalledInventory.ParseDriverStoreProcessResult("{\"Packages\":[]}", 0).Status);
    }
}
