using System.Text;
using SteamInputAddonforClaw.Devices.MSI.LiveUpdate;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiUpdateDriverStoreTests
{
    [Fact]
    public void PnPUtil_arguments_use_only_the_read_only_XML_driver_inventory_command()
    {
        Assert.Equal(new[] { "/enum-drivers", "/format", "xml" },
            MsiUpdateInstalledInventory.GetDriverStoreArguments());
    }

    [Fact]
    public void Class_scoped_arguments_are_built_only_from_supported_driver_classes()
    {
        Assert.Equal(new[] { "/enum-drivers", "/class", "System", "/devices", "/format", "xml" },
            MsiUpdateInstalledInventory.GetDriverStoreArguments("System"));
        Assert.Equal(new[] { "/enum-drivers", "/class", "Extension", "/devices", "/format", "xml" },
            MsiUpdateInstalledInventory.GetDriverStoreArguments("Extension"));
        Assert.Equal(new[] { "/enum-drivers", "/class", "AudioProcessingObject", "/devices", "/format", "xml" },
            MsiUpdateInstalledInventory.GetDriverStoreArguments("AudioProcessingObject"));
        Assert.Throws<ArgumentOutOfRangeException>(() => MsiUpdateInstalledInventory.GetDriverStoreArguments("Other"));
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
            MsiUpdateInstalledInventory.ParseDriverStoreProcessResult(EmptyDriverStoreXml, 0),
            MsiUpdateInstalledInventory.ParseDriverStoreProcessResult(EmptyDriverStoreXml, 0, timedOut: true),
            MsiUpdateInstalledInventory.ParseDriverStoreProcessResult(EmptyDriverStoreXml, 5)
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
    [InlineData("msi.claw.cg3em", "Partial", true)]
    [InlineData("msi.claw.cg3em", "TimedOut", true)]
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
    public void XML_parser_reads_observed_fields_case_insensitively_and_normalizes_original_INF_basename()
    {
        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesXml($"""
            <DriverStore>
              <Driver DriverName="oem677.inf">
                <OriginalName>C:\Windows\System32\DriverStore\FileRepository\ISHHECI.INF</OriginalName>
                <ProviderName>Intel</ProviderName><ClassName>System</ClassName>
                <DriverVersion>12/05/2025 5.8.61.0</DriverVersion>
              </Driver>
              <dRiVeR drivername="oem755.inf">
                <originalname>IntcOED_OemLibPath.inf</originalname>
                <providername>Realtek Semiconductor Corp.</providername><classname>Extension</classname>
                <driverversion>01/23/2026 20.43.274.009</driverversion>
              </dRiVeR>
              {Driver("oem690.inf", "RtkSdcaXuMSI.inf", "Realtek", "Extension", "10.0.378.1")}
              {Driver("oem6.inf", "mep_audio_component.inf", "Microsoft Corporation", "AudioProcessingObject", "3.0.8.0")}
              {Driver("oem900.inf", "unrelated.inf", "Private Publisher", "System", "1.0.0.0")}
            </DriverStore>
            """);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(4, result.Packages.Count);
        Assert.Contains(result.Packages, package => string.Equals(package.OriginalInfName, "ishheci.inf",
                StringComparison.OrdinalIgnoreCase)
            && package.PublishedInfName == "oem677.inf" && package.Version == "5.8.61.0");
        Assert.Contains(result.Packages, package => package.OriginalInfName == "IntcOED_OemLibPath.inf"
            && package.PublishedInfName == "oem755.inf" && package.Version == "20.43.274.009");
        Assert.Contains(result.Packages, package => package.OriginalInfName == "RtkSdcaXuMSI.inf");
        Assert.Contains(result.Packages, package => package.OriginalInfName == "mep_audio_component.inf");
        Assert.DoesNotContain(result.Packages, package => package.Provider == "Private Publisher");
    }

    [Fact]
    public void XML_parser_accepts_a_valid_empty_inventory_but_not_empty_process_output()
    {
        var emptyList = MsiUpdateInstalledInventory.ParseDriverStorePackagesXml(EmptyDriverStoreXml);
        var emptyOutput = MsiUpdateInstalledInventory.ParseDriverStorePackagesXml(string.Empty);

        Assert.Equal("Completed", emptyList.Status);
        Assert.Empty(emptyList.Packages);
        Assert.Equal("EmptyOutput", emptyOutput.Status);
        Assert.Equal("Cg3emDriverStoreEmptyOutput", emptyOutput.Warning);
    }

    [Theory]
    [InlineData("not-xml")]
    [InlineData("<DriverStore><Driver DriverName=\"oem1.inf\"><OriginalName>ishheci.inf</OriginalName></DriverStore>")]
    [InlineData("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///c:/secret'>]><DriverStore>&e;</DriverStore>")]
    [InlineData("<DriverStore><Driver DriverName=\"not-a-published-inf\"><OriginalName>ishheci.inf</OriginalName><ProviderName>Intel</ProviderName><ClassName>System</ClassName><DriverVersion>5.8.61.0</DriverVersion></Driver></DriverStore>")]
    [InlineData("<DriverStore><Driver DriverName=\"oem1.inf\"><OriginalName>ishheci.inf</OriginalName><ProviderName>Intel</ProviderName><ClassName>System</ClassName><DriverVersion>12/05/2025 invalid</DriverVersion></Driver></DriverStore>")]
    public void XML_parser_fails_closed_for_malformed_or_incomplete_allowlisted_rows(string xml)
    {
        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesXml(xml);

        Assert.Equal("InvalidOutput", result.Status);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public void XML_parser_accepts_large_unrelated_driver_lists_without_applying_the_matching_row_limit()
    {
        var unrelated = string.Concat(Enumerable.Range(1, 400).Select(index => $"""
            <Driver DriverName="oem{index}.inf"><OriginalName>unrelated{index}.inf</OriginalName>
              <ProviderName>Other</ProviderName><ClassName>System</ClassName><DriverVersion>1.0.0.0</DriverVersion>
              <Ignored>{new string('x', 200)}</Ignored>
            </Driver>
            """));
        var xml = $"<DriverStore>{unrelated}{Driver("oem900.inf", "ishheci.inf", "Intel", "System", "5.8.61.0")}</DriverStore>";

        Assert.True(xml.Length > 64 * 1024);
        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesXml(xml);

        Assert.Equal("Completed", result.Status);
        Assert.Single(result.Packages);
    }

    [Fact]
    public void XML_parser_rejects_excessive_matching_rows_and_oversized_document()
    {
        var tooManyRows = $"<DriverStore>{string.Concat(Enumerable.Range(1, 129)
            .Select(index => Driver($"oem{index}.inf", "ishheci.inf", "Intel", "System", "5.8.61.0")))}</DriverStore>";
        var tooLarge = $"<DriverStore><Ignored>{new string('x', MsiUpdateInstalledInventory.MaximumDriverStoreOutputCharacters + 1)}</Ignored></DriverStore>";

        Assert.Equal("RowLimitExceeded", MsiUpdateInstalledInventory.ParseDriverStorePackagesXml(tooManyRows).Status);
        Assert.Equal("OutputLimitExceeded", MsiUpdateInstalledInventory.ParseDriverStorePackagesXml(tooLarge).Status);
    }

    [Fact]
    public void Parser_preserves_duplicate_driver_packages_for_conservative_matching()
    {
        var xml = $"<DriverStore>{Driver("oem1.inf", "ishheci.inf", "Intel", "System", "5.8.61.0")}{Driver("oem2.inf", "ishheci.inf", "Intel", "System", "5.8.62.0")}</DriverStore>";

        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesXml(xml);

        Assert.Equal(2, result.Packages.Count);
        Assert.Equal(new[] { "5.8.61.0", "5.8.62.0" }, result.Packages.Select(package => package.Version));
    }

    [Fact]
    public void Conflicting_numeric_versions_request_one_class_lookup_and_equal_versions_do_not()
    {
        var packages = new[]
        {
            Package("oem7.inf", "ishheci.inf", "Intel", "System", "5.8.30.0"),
            Package("oem677.inf", "ishheci.inf", "Intel", "System", "5.8.61.0"),
            Package("oem439.inf", "IntcOED_OemLibPath.inf", "Realtek Semiconductor Corp.", "Extension", "20.42.274.2"),
            Package("oem642.inf", "IntcOED_OemLibPath.inf", "Realtek Semiconductor Corp.", "Extension", "20.43.274.009"),
            Package("oem690.inf", "RtkSdcaXuMSI.inf", "Realtek", "Extension", "10.0.378.1"),
            Package("oem6.inf", "mep_audio_component.inf", "Microsoft Corporation", "AudioProcessingObject", "3.0.8.0")
        };

        Assert.Equal(new[] { "System", "Extension" },
            MsiUpdateCatalogComposer.GetCg3emDriverStoreClassesRequiringStartedBinding(packages));

        var numericallyEqual = new[]
        {
            Package("oem1.inf", "IntcOED_OemLibPath.inf", "Realtek Semiconductor Corp.", "Extension", "20.43.274.009"),
            Package("oem2.inf", "IntcOED_OemLibPath.inf", "Realtek Semiconductor Corp.", "Extension", "20.43.274.9")
        };
        Assert.Empty(MsiUpdateCatalogComposer.GetCg3emDriverStoreClassesRequiringStartedBinding(numericallyEqual));
    }

    [Fact]
    public void Class_XML_parser_aggregates_multiple_device_rows_into_one_started_package()
    {
        var xml = $"""
            <DriverStore>
              <Driver DriverName="oem690.inf">
                <OriginalName>RtkSdcaXuMSI.inf</OriginalName><ProviderName>Realtek</ProviderName><ClassName>Extension</ClassName>
                <DriverVersion>10/15/2026 10.0.378.1</DriverVersion>
                <Devices><Device><Status>Started</Status></Device><Device><Status>Started</Status></Device><Device><Status>Started</Status></Device></Devices>
              </Driver>
              {Driver("oem1.inf", "unrelated.inf", "Intel", "System", "5.8.61.0")}
            </DriverStore>
            """;

        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesXml(xml, includeDeviceStatuses: true);

        Assert.Equal("Completed", result.Status);
        var package = Assert.Single(result.Packages);
        Assert.Equal("oem690.inf", package.PublishedInfName);
        Assert.True(package.HasStartedDevice);
    }

    [Fact]
    public void Started_binding_evidence_requires_exact_published_and_package_identity()
    {
        var basePackage = Package("oem677.inf", "ishheci.inf", "Intel", "System", "5.8.61.0");
        var evidence = MsiUpdateInstalledInventory.ParseDriverStorePackagesXml($"""
            <DriverStore>
              {DriverWithDevice("oem677.inf", "ishheci.inf", "Intel", "System", "5.8.61.0", "Started")}
              {DriverWithDevice("oem678.inf", "ishheci.inf", "Intel", "System", "5.8.62.0", "Disconnected")}
            </DriverStore>
            """, includeDeviceStatuses: true);

        var disconnectedPackage = basePackage with { PublishedInfName = "oem678.inf", Version = "5.8.62.0" };
        var updated = MsiUpdateInstalledInventory.ApplyStartedBindingEvidence(
            [basePackage, disconnectedPackage], evidence.Packages, "System");

        Assert.True(updated[0].HasStartedDevice);
        Assert.False(updated[1].HasStartedDevice);
    }

    [Fact]
    public void Class_XML_parser_leaves_missing_binding_status_unknown()
    {
        var result = MsiUpdateInstalledInventory.ParseDriverStorePackagesXml($"""
            <DriverStore>
              <Driver DriverName="oem677.inf">
                <OriginalName>ishheci.inf</OriginalName><ProviderName>Intel</ProviderName><ClassName>System</ClassName>
                <DriverVersion>5.8.61.0</DriverVersion><Devices><Device /></Devices>
              </Driver>
            </DriverStore>
            """, includeDeviceStatuses: true);

        Assert.Equal("Completed", result.Status);
        Assert.Null(Assert.Single(result.Packages).HasStartedDevice);
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
        Assert.Equal("TimedOut", MsiUpdateInstalledInventory.ParseDriverStoreProcessResult(EmptyDriverStoreXml, 0, timedOut: true).Status);
        Assert.Equal("OutputLimitExceeded", MsiUpdateInstalledInventory.ParseDriverStoreProcessResult(EmptyDriverStoreXml, 0,
            outputLimitExceeded: true).Status);
        Assert.Equal("Unavailable", MsiUpdateInstalledInventory.ParseDriverStoreProcessResult(EmptyDriverStoreXml, 5).Status);
        Assert.Equal("Completed", MsiUpdateInstalledInventory.ParseDriverStoreProcessResult(EmptyDriverStoreXml, 0).Status);
    }

    private const string EmptyDriverStoreXml = "<DriverStore />";

    private static MsiUpdateInstalledDriverPackage Package(
        string published, string original, string provider, string deviceClass, string version) =>
        new(published, original, provider, deviceClass, version);

    private static string Driver(string published, string original, string provider, string deviceClass, string version) => $"""
        <Driver DriverName="{published}">
          <OriginalName>{original}</OriginalName><ProviderName>{provider}</ProviderName>
          <ClassName>{deviceClass}</ClassName><DriverVersion>{version}</DriverVersion>
        </Driver>
        """;

    private static string DriverWithDevice(string published, string original, string provider,
        string deviceClass, string version, string status) => $"""
        <Driver DriverName="{published}">
          <OriginalName>{original}</OriginalName><ProviderName>{provider}</ProviderName>
          <ClassName>{deviceClass}</ClassName><DriverVersion>{version}</DriverVersion>
          <Devices><Device><Status>{status}</Status></Device></Devices>
        </Driver>
        """;
}
