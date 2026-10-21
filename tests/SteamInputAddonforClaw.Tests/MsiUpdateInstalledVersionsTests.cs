using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.MSI.LiveUpdate;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiUpdateInstalledVersionsTests
{
    private const string GpuDeviceId = @"PCI\VEN_8086&DEV_1234&SUBSYS_56788086&REV_01";
    private const string GpuCompatibleId = @"PCI\VEN_8086&DEV_1234";

    [Fact]
    public void Intel_archive_parser_reads_only_catalog_json_and_preserves_typed_component_data()
    {
        var archive = CreateArchive(
            [
                Package("930514", "Intel Arc Graphics", "32.0.101.9150 WHQL Certified", false,
                    Component("Graphics", "32.0.101.9150", ["VEN_8086&DEV_1234"], ["Windows 10, Windows 11"])),
                Package("926939", "Intel Wi-Fi", "24.70.0", false,
                    Component("Wireless", "24.70.0.3", ["VEN_8086&DEV_5678"], [])),
                Package("927576", "Intel Bluetooth", "24.70.0", false,
                    Component("Bluetooth", "24.70.0.4", ["VID_8087&PID_0033"], [])),
                Package("927127", "Intel NPU Driver - Windows*", "32.0.100.5540", false,
                    Component("OtherDrivers", null, ["VEN_8086&DEV_643E"], []))
            ],
            meta: "{\"Created\":\"2026-10-16T11:00:03.6528776Z\"}",
            includeUntrustedBinary: true);

        var result = IntelDsaCatalogClient.ParseArchive(archive);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 11, 0, 3, TimeSpan.Zero).AddTicks(6528776), result.CatalogCreatedUtc);
        Assert.Equal(4, result.Packages.Count);
        Assert.Equal("930514", result.Packages[0].Id);
        Assert.False(result.Packages[0].IsBeta);
        Assert.Equal("https://download.example.test/driver.exe", result.Packages[0].PackageUrl);
        Assert.Equal("32.0.101.9150", result.Packages[0].Components[0].Version);
        Assert.Equal("24.70.0.3", result.Packages[1].Components[0].Version);
        Assert.Equal("24.70.0.4", result.Packages[2].Components[0].Version);
        Assert.Null(result.Packages[3].Components[0].Version);
        Assert.Equal("32.0.100.5540", result.Packages[3].Version);
    }

    [Fact]
    public void Intel_archive_parser_preserves_large_detection_value_arrays_for_matching()
    {
        var detectionValues = Enumerable.Range(0, 645)
            .Select(index => $"VEN_8086&DEV_{index:X4}")
            .Append("VEN_8086&DEV_64A0")
            .ToArray();
        var archive = CreateArchive(
        [
            Package("930514", "Intel Arc Graphics", "32.0.101.9150", false,
                Component("Graphics", "32.0.101.9150", detectionValues, []))
        ]);

        var package = Assert.Single(IntelDsaCatalogClient.ParseArchive(archive).Packages);
        var deviceId = @"PCI\VEN_8086&DEV_64A0&SUBSYS_146C1462&REV_04";
        var device = Device(deviceId, [deviceId], "DISPLAY", "Intel Arc Graphics", "32.0.100.1", 0x8086, 0x64A0);
        var match = Assert.Single(IntelDsaHardwareMatcher.Resolve(new(true, [package]), Inventory(device), "win11"));

        Assert.Equal(646, package.Components[0].DetectionValues.Count);
        Assert.Equal("930514", match.PackageId);
        Assert.Equal(IntelDsaComponentFamily.Graphics, match.Family);
    }

    [Theory]
    [InlineData("not a zip", "InvalidArchive")]
    [InlineData("", "EmptyArchive")]
    public void Intel_archive_parser_fails_closed_for_invalid_or_empty_archive(string text, string expectedError)
    {
        var result = IntelDsaCatalogClient.ParseArchive(Encoding.UTF8.GetBytes(text));

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.ErrorKind);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public void Intel_archive_parser_requires_the_exact_catalog_entry_and_valid_json()
    {
        var missing = IntelDsaCatalogClient.ParseArchive(CreateArchive([], includeCatalog: false));
        var invalid = IntelDsaCatalogClient.ParseArchive(CreateArchiveBytes("{"));

        Assert.Equal("CatalogEntryMissing", missing.ErrorKind);
        Assert.Equal("InvalidJson", invalid.ErrorKind);
    }

    [Fact]
    public void Intel_archive_parser_rejects_oversized_json_entries()
    {
        var archive = CreateArchiveBytes("[\"" + new string('x', 8 * 1024 * 1024) + "\"]");

        var result = IntelDsaCatalogClient.ParseArchive(archive);

        Assert.Equal("InvalidArchive", result.ErrorKind);
    }

    [Fact]
    public async Task Intel_query_reports_http_failure_without_hiding_it_as_an_empty_catalog()
    {
        using var http = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
        var client = new IntelDsaCatalogClient(http);

        var result = await client.QueryAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Http403", result.ErrorKind);
    }

    [Fact]
    public async Task Intel_query_downloads_and_parses_one_catalog_archive()
    {
        Uri? requestedUri = null;
        string? userAgent = null;
        var archive = CreateArchive([Package("930514", "Intel Arc", "32.0.101.9150", false,
            Component("Graphics", "32.0.101.9150", ["VEN_8086&DEV_1234"], []))]);
        using var http = new HttpClient(new StubHandler((request, _) =>
        {
            requestedUri = request.RequestUri;
            userAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        }));

        var result = await new IntelDsaCatalogClient(http).QueryAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(IntelDsaCatalogClient.CatalogUri, requestedUri);
        Assert.Contains("SteamInputAddonforClaw", userAgent, StringComparison.Ordinal);
        Assert.Single(result.Packages);
    }

    [Fact]
    public async Task Intel_query_enforces_its_request_deadline()
    {
        using var http = new HttpClient(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var client = new IntelDsaCatalogClient(http, TimeSpan.FromMilliseconds(40));

        var result = await client.QueryAsync(CancellationToken.None);

        Assert.Equal("TimedOut", result.ErrorKind);
    }

    [Fact]
    public async Task Intel_query_propagates_navigation_cancellation()
    {
        using var http = new HttpClient(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cancellation = new CancellationTokenSource();
        var client = new IntelDsaCatalogClient(http, TimeSpan.FromSeconds(10));
        var operation = client.QueryAsync(cancellation.Token);
        cancellation.CancelAfter(30);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task Intel_query_rejects_an_oversized_response_from_headers()
    {
        using var http = new HttpClient(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[4 * 1024 * 1024 + 1])
            })));

        var result = await new IntelDsaCatalogClient(http).QueryAsync(CancellationToken.None);

        Assert.Equal("ArchiveTooLarge", result.ErrorKind);
    }

    [Fact]
    public void Exact_component_and_hardware_matches_resolve_Intel_precedence_and_version_types()
    {
        var inventory = Inventory(
            Device(GpuDeviceId, [GpuDeviceId, GpuCompatibleId], "DISPLAY", "Intel Arc", "32.0.100.1", 0x8086, 0x1234),
            Device(@"PCI\VEN_8086&DEV_5678&SUBSYS_10108086", [@"PCI\VEN_8086&DEV_5678&SUBSYS_10108086"], "NET", "Intel Wi-Fi", "24.60.0.2", 0x8086, 0x5678),
            Device(@"USB\VID_8087&PID_0033", [@"USB\VID_8087&PID_0033"], "Bluetooth", "Intel Bluetooth", "24.60.0.3", 0x8087, 0x0033),
            Device(@"PCI\VEN_8086&DEV_643E", [@"PCI\VEN_8086&DEV_643E"], "System", "Intel NPU", "32.0.90.1", 0x8086, 0x643E));
        var packages = new[]
        {
            IntelPackage("930514", "Intel Arc Graphics", false, IntelComponent("Graphics", "32.0.101.9150", "VEN_8086&DEV_1234")),
            IntelPackage("926939", "Intel Wi-Fi", false, IntelComponent("Wireless", "24.70.0.3", "VEN_8086&DEV_5678")),
            IntelPackage("927576", "Intel Bluetooth", false, IntelComponent("Bluetooth", "24.70.0.4", "VID_8087&PID_0033")),
            IntelPackage("927127", "Intel NPU Driver", false, IntelComponent("OtherDrivers", null, "VEN_8086&DEV_643E"), version: "32.0.100.5540"),
            IntelPackage("999001", "Intel Arc Beta", true, IntelComponent("Graphics", "99.0.0.0", "VEN_8086&DEV_1234")),
            IntelPackage("999002", "Unrelated Intel Other Driver", false, IntelComponent("OtherDrivers", "99.0.0.0", "VEN_8086&DEV_9999"))
        };

        var result = IntelDsaHardwareMatcher.Resolve(new(true, packages), inventory, "win11");

        Assert.Equal(4, result.Count);
        Assert.Contains(result, item => item.Family == IntelDsaComponentFamily.Graphics && item.LatestVersion == "32.0.101.9150" && item.VersionKind == FrontendMsiLiveUpdateVersionKind.Driver);
        Assert.Contains(result, item => item.Family == IntelDsaComponentFamily.Wireless && item.LatestVersion == "24.70.0.3");
        Assert.Contains(result, item => item.Family == IntelDsaComponentFamily.Bluetooth && item.LatestVersion == "24.70.0.4");
        Assert.Contains(result, item => item.Family == IntelDsaComponentFamily.Npu && item.LatestVersion == "32.0.100.5540" && item.VersionKind == FrontendMsiLiveUpdateVersionKind.Package);
    }

    [Fact]
    public void Intel_matcher_rejects_wrong_device_os_and_beta_candidates()
    {
        var device = Device(GpuDeviceId, [GpuDeviceId], "DISPLAY", "Intel Arc", "32.0.100.1", 0x8086, 0x1234);
        var inventory = Inventory(device);
        var packages = new[]
        {
            IntelPackage("1", "Intel Arc Win10", false, IntelComponent("Graphics", "1.0.0.0", "VEN_8086&DEV_1234"), operatingSystems: ["Windows 10"]),
            IntelPackage("2", "Intel Arc Beta", true, IntelComponent("Graphics", "2.0.0.0", "VEN_8086&DEV_1234")),
            IntelPackage("3", "Wrong GPU", false, IntelComponent("Graphics", "3.0.0.0", "VEN_8086&DEV_9999"))
        };

        Assert.Empty(IntelDsaHardwareMatcher.Resolve(new(true, packages), inventory, "win11"));
    }

    [Fact]
    public void Intel_matcher_accepts_both_versions_in_a_mixed_OS_override_label()
    {
        var archive = CreateArchive(
        [
            Package("1", "Intel Arc", "32.0.1.0", false,
                Component("Graphics", "32.0.1.0", ["VEN_8086&DEV_1234"], []),
                operatingSystemOverrides: new Dictionary<string, object> { ["Windows 10, Windows 11"] = new { } })
        ]);
        var package = Assert.Single(IntelDsaCatalogClient.ParseArchive(archive).Packages);
        var device = Device(GpuDeviceId, [GpuDeviceId], "DISPLAY", "Intel Arc", "32.0.0.0", 0x8086, 0x1234);

        Assert.Single(IntelDsaHardwareMatcher.Resolve(new(true, [package]), Inventory(device), "win11"));
    }

    [Fact]
    public void Intel_matcher_reads_hyphenated_OS_override_labels_from_the_catalog_archive()
    {
        var archive = CreateArchive(
        [
            Package("win10", "Intel Wi-Fi", "99.0.0.0", false,
                Component("Wireless", "99.0.0.0", ["VEN_8086&DEV_5678"], [],
                    new Dictionary<string, object> { ["windows-10-22h2-64"] = new { } })),
            Package("win11", "Intel Wi-Fi", "24.70.0.3", false,
                Component("Wireless", "24.70.0.3", ["VEN_8086&DEV_5678"], [],
                    new Dictionary<string, object> { ["windows-11-24h2-64"] = new { } }))
        ]);
        var packages = IntelDsaCatalogClient.ParseArchive(archive).Packages;
        var deviceId = @"PCI\VEN_8086&DEV_5678&SUBSYS_146C1462&REV_04";
        var device = Device(deviceId, [deviceId], "NET", "Intel Wi-Fi", "24.60.0.1", 0x8086, 0x5678);

        var match = Assert.Single(IntelDsaHardwareMatcher.Resolve(new(true, packages), Inventory(device), "win11"));

        Assert.Equal("win11", match.PackageId);
        Assert.Equal("24.70.0.3", match.LatestVersion);
    }

    [Fact]
    public void Intel_matcher_keeps_segment_wildcards_local_and_prefers_more_specific_identity()
    {
        var device = Device(GpuDeviceId, [GpuDeviceId, GpuCompatibleId], "DISPLAY", "Intel Arc", "32.0.0.0", 0x8086, 0x1234);
        var packages = new[]
        {
            IntelPackage("weak", "Intel Arc", false, IntelComponent("Graphics", "32.0.101.9999", "VEN_8086&DEV_1234")),
            IntelPackage("specific", "Intel Arc", false, IntelComponent("Graphics", "32.0.101.9000", "VEN_8086&DEV_1234&SUBSYS_*8086"))
        };

        var result = Assert.Single(IntelDsaHardwareMatcher.Resolve(new(true, packages), Inventory(device), "win11"));

        Assert.Equal("specific", result.PackageId);
        Assert.Equal("32.0.101.9000", result.LatestVersion);
        Assert.False(IntelDsaHardwareMatcher.TryMatchDetectionValue(
            "VEN_8086&DEV_1234&SUBSYS_*8086",
            [@"PCI\VEN_8086&DEV_1234&REV_8086"], out _));
    }

    [Fact]
    public void MSI_catalog_parser_retains_validated_optional_identifiers_and_legacy_rows()
    {
        var output = HelperOutput(
            ("bios", "[]"), ("firmware", "[]"),
            ("driver", "[{\"name\":\"Intel Wi-Fi\",\"DeviceID\":\"PCI\\\\VEN_8086&DEV_5678\",\"GUID\":\"{00112233-4455-6677-8899-AABBCCDDEEFF}\",\"upgradeGuid\":\"00112233-4455-6677-8899-aabbccddeeff\",\"packageVersion\":\"24.70.0\",\"driverVersion\":\"24.70.0.3\",\"infName\":\"netwtw.inf\"},{\"name\":\"Legacy\",\"version\":\"1.0\"},{\"name\":\"Unsafe ID\",\"deviceID\":\"not-an-id\",\"GUID\":\"invalid\"}]"),
            ("utility", "[]"));
        var result = MsiLiveUpdateCatalogResultParser.ParseHelperOutput(output,
            new("Claw 8 AI+ A2VM", "kr", "win11"), DateTimeOffset.UtcNow);
        var rows = result.Categories[2].Entries;

        Assert.Equal(@"PCI\VEN_8086&DEV_5678", rows[0].DeviceId);
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", rows[0].Guid);
        Assert.Equal("24.70.0.3", rows[0].DriverVersion);
        Assert.Equal("netwtw.inf", rows[0].InfName);
        Assert.Null(rows[1].DeviceId);
        Assert.Null(rows[1].DriverVersion);
        Assert.Null(rows[2].DeviceId);
        Assert.Null(rows[2].Guid);

        var mapped = MsiLiveUpdateCatalogContractMapper.Map(result).Categories[2].Entries[0];
        Assert.Equal("24.70.0.3", mapped.Version);
        Assert.Equal(FrontendMsiLiveUpdateVersionKind.Driver, mapped.LatestVersionKind);
    }

    [Fact]
    public void Combined_snapshot_prefers_exact_Intel_record_keeps_distinct_MSI_rows_and_labels_fallback()
    {
        var device = Device(GpuDeviceId, [GpuDeviceId], "DISPLAY", "Intel Arc", "32.0.100.1", 0x8086, 0x1234);
        var inventory = Inventory(device);
        var driverCategory = new MsiLiveUpdateCatalogCategoryResult(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success,
            [
                new("Intel Arc Display Driver", "32.0.100.4", null, null, DeviceId: GpuDeviceId),
                new("Intel IPF Driver", "1.2.3", null, null)
            ]);
        var msi = MsiResult(driverCategory);
        var intel = new IntelDsaCatalogResult(true,
        [IntelPackage("930514", "Intel Arc Graphics", false, IntelComponent("Graphics", "32.0.101.9150", "VEN_8086&DEV_1234"))]);

        var combined = MsiUpdateCatalogComposer.Compose(msi, intel, inventory);
        var entries = Assert.Single(combined.Categories,
            category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries;

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, item => item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa
            && item.Version == "32.0.101.9150" && item.InstalledVersion == "32.0.100.1");
        Assert.Contains(entries, item => item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa
            && item.ComparisonStatus == FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable);
        Assert.Contains(entries, item => item.Name == "Intel IPF Driver" && item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.Msi);
        Assert.Contains(entries, item => item.Name == "Intel IPF Driver"
            && item.ComparisonStatus == FrontendMsiLiveUpdateComparisonStatus.Unknown);

        var fallback = MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"), inventory)
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries;
        Assert.Contains(fallback, item => item.Name == "Intel Arc Display Driver"
            && item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.Msi);
        Assert.Contains(fallback, item => item.Name == "Intel Arc Display Driver"
            && item.ComparisonStatus == FrontendMsiLiveUpdateComparisonStatus.Unknown);
        Assert.Equal(FrontendMsiLiveUpdateSourceState.Unavailable, MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory).IntelDsaSource!.State);
    }

    [Fact]
    public void Missing_local_inventory_is_partial_and_never_fabricates_an_installed_version()
    {
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Intel Display Driver", "32.0.101.9150", null, null, DeviceId: GpuDeviceId)]));
        var combined = MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"),
            new([], [], [], null, ["WmiDriverReadFailed"]));
        var entry = combined.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries[0];

        Assert.Equal(FrontendMsiLiveUpdateCatalogState.PartialFailure, combined.State);
        Assert.Null(entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateVersionSource.Msi, entry.LatestVersionSource);
    }

    [Fact]
    public void MSI_base_HardwareId_resolves_only_the_matching_installed_driver_version()
    {
        var matchingId = @"PCI\VEN_8086&DEV_64A0&SUBSYS_146C1462&REV_04";
        var unrelatedId = @"PCI\VEN_8086&DEV_1234&SUBSYS_146C1462&REV_04";
        var matchingDevice = Device(matchingId, [matchingId], "DISPLAY", "Intel Arc Graphics", "32.0.100.1", 0x8086, 0x64A0);
        var unrelatedDevice = Device(unrelatedId, [unrelatedId], "DISPLAY", "Other Intel Graphics", "99.0.0.1", 0x8086, 0x1234);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Intel Arc Graphics Driver", "32.0.101.9150", null, null, DeviceId: @"PCI\VEN_8086&DEV_64A0")]));

        var driver = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"),
                Inventory(matchingDevice, unrelatedDevice)).Categories,
            category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver);
        var entry = Assert.Single(driver.Entries);

        Assert.Equal("32.0.100.1", entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Exact, entry.InstalledVersionConfidence);
        Assert.Equal(FrontendMsiLiveUpdateVersionSource.Msi, entry.LatestVersionSource);
    }

    [Fact]
    public void A2VM_matches_a_later_valid_alternate_catalog_HardwareId()
    {
        const string primaryId = @"PCI\VEN_8086&DEV_1111";
        const string alternateId = @"PCI\VEN_8086&DEV_2222";
        var device = Device(alternateId + @"\LOCAL_INSTANCE", [alternateId], "SYSTEM",
            "Intel Device", "10.1.2.3", 0x8086, 0x2222);
        var catalogEntry = new MsiLiveUpdateCatalogEntry("Alternate Hardware Driver", "10.1.2.4", null, null,
            DeviceId: primaryId, CatalogHardwareIds: [primaryId, alternateId]);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success, [catalogEntry]));

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), Inventory(device),
            hardwareModelId: "msi.claw.a2vm.8")
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Equal("10.1.2.3", entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Exact, entry.InstalledVersionConfidence);
    }

    [Fact]
    public void A2VM_does_not_expand_vendor_only_catalog_identity_to_all_vendor_devices()
    {
        const string vendorOnlyId = @"PCI\VEN_8086";
        var device = Device(@"PCI\VEN_8086&DEV_2222\LOCAL_INSTANCE", [vendorOnlyId],
            "SYSTEM", "Intel Device", "10.1.2.3", 0x8086, 0x2222);
        var catalogEntry = new MsiLiveUpdateCatalogEntry("Unmapped Generic Driver", "10.1.2.4", null, null,
            DeviceId: vendorOnlyId, CatalogHardwareIds: [vendorOnlyId]);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success, [catalogEntry]));

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), Inventory(device),
            hardwareModelId: "msi.claw.a2vm.7")
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Null(entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Fact]
    public void Successful_Intel_query_without_a_device_match_does_not_hide_failed_MSI_driver_category()
    {
        var device = Device(GpuDeviceId, [GpuDeviceId], "DISPLAY", "Intel Arc", "32.0.100.1", 0x8086, 0x1234);
        var failedDriverCategory = new MsiLiveUpdateCatalogCategoryResult(
            MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Failed, [], "Http503");
        var msi = MsiResult(failedDriverCategory);
        var intel = new IntelDsaCatalogResult(true,
            [IntelPackage("wrong-device", "Intel Arc Graphics", false,
                IntelComponent("Graphics", "32.0.101.9150", "VEN_8086&DEV_9999"))]);

        var combined = MsiUpdateCatalogComposer.Compose(msi, intel, Inventory(device));
        var driver = Assert.Single(combined.Categories,
            category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver);

        Assert.Equal(FrontendMsiLiveUpdateCatalogCategoryState.Failed, driver.State);
        Assert.Equal("Http503", driver.ErrorKind);
        Assert.Empty(driver.Entries);
        Assert.Equal(FrontendMsiLiveUpdateSourceState.PartialFailure, combined.MsiSource!.State);
        Assert.Equal(FrontendMsiLiveUpdateCatalogState.PartialFailure, combined.State);
    }

    [Fact]
    public void Matched_Intel_driver_remains_visible_when_MSI_query_is_unavailable()
    {
        var deviceId = @"PCI\VEN_8086&DEV_64A0&SUBSYS_146C1462&REV_04";
        var device = Device(deviceId, [deviceId], "DISPLAY", "Intel Arc Graphics", "32.0.100.1", 0x8086, 0x64A0);
        var msi = new MsiLiveUpdateCatalogQueryResult(MsiLiveUpdateCatalogState.Unavailable,
            null, null, "win11", [], DateTimeOffset.UtcNow, "HelperUnavailable");
        var intel = new IntelDsaCatalogResult(true,
        [IntelPackage("930514", "Intel Arc Graphics", false,
            IntelComponent("Graphics", "32.0.101.9150", "VEN_8086&DEV_64A0"))]);

        var combined = MsiUpdateCatalogComposer.Compose(msi, intel, Inventory(device));
        var driver = Assert.Single(combined.Categories,
            category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver);

        Assert.Equal(FrontendMsiLiveUpdateCatalogCategoryState.Success, driver.State);
        Assert.Equal("MsiCategoryMissing", driver.ErrorKind);
        Assert.Equal("32.0.101.9150", Assert.Single(driver.Entries).Version);
        Assert.Equal(FrontendMsiLiveUpdateVersionSource.IntelDsa, driver.Entries[0].LatestVersionSource);
        Assert.Equal(FrontendMsiLiveUpdateSourceState.Unavailable, combined.MsiSource!.State);
        Assert.Equal(FrontendMsiLiveUpdateCatalogState.PartialFailure, combined.State);
    }

    [Fact]
    public void Intel_DSA_row_replaces_MSI_base_HardwareId_duplicate()
    {
        var deviceId = @"PCI\VEN_8086&DEV_64A0&SUBSYS_146C1462&REV_04";
        var device = Device(deviceId, [deviceId], "DISPLAY", "Intel Arc Graphics", "32.0.100.1", 0x8086, 0x64A0);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Intel Arc Graphics Driver", "32.0.101.8000", null, null, DeviceId: @"PCI\VEN_8086&DEV_64A0")]));
        var intel = new IntelDsaCatalogResult(true,
        [IntelPackage("930514", "Intel Arc Graphics", false,
            IntelComponent("Graphics", "32.0.101.9150", "VEN_8086&DEV_64A0"))]);
        var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();

        var driver = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, intel, Inventory(device), onDiagnostic: diagnostics.Add).Categories,
            category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver);

        var entry = Assert.Single(driver.Entries);
        Assert.Equal(FrontendMsiLiveUpdateVersionSource.IntelDsa, entry.LatestVersionSource);
        Assert.Equal("32.0.101.9150", entry.Version);
        Assert.Contains(diagnostics, item => item.Outcome == "SuppressedByExactIdentity"
            && item.CandidateCount == 1 && item.VersionCount == 1);
    }

    [Theory]
    [InlineData("Intel Graphics Driver", "Graphics", "PCI", "VEN_8086&DEV_1234", "DISPLAY", "Intel Arc Graphics", 0x8086, 0x1234)]
    [InlineData("Intel Wireless LAN Driver", "Wireless", "PCI", "VEN_8086&DEV_5678", "NET", "Intel Wi-Fi 7", 0x8086, 0x5678)]
    [InlineData("Intel Bluetooth Driver", "Bluetooth", "USB", "VID_8087&PID_0033", "Bluetooth", "Intel Wireless Bluetooth", 0x8087, 0x0033)]
    [InlineData("Intel NPU Driver", "OtherDrivers", "PCI", "VEN_8086&DEV_643E", "System", "Intel AI Boost NPU", 0x8086, 0x643E)]
    public void Intel_DSA_unique_family_replaces_known_MSI_row_without_device_id(
        string msiName, string category, string bus, string detectionValue, string deviceClass,
        string deviceName, ushort vendorId, ushort productId)
    {
        var deviceId = $"{bus}\\{detectionValue}";
        var device = Device(deviceId, [deviceId], deviceClass, deviceName, "1.2.3.4", vendorId, productId);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new(msiName, "2.0.0.0", null, null)]));
        var intelVersion = category == "OtherDrivers" ? null : "3.0.0.0";
        var packageName = category == "OtherDrivers" ? "Intel NPU Driver" : $"Intel {category}";
        var package = IntelPackage("family-match", packageName, false,
            IntelComponent(category, intelVersion, detectionValue), version: "3.0.0.0");
        var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();

        var driver = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            new(true, [package]), Inventory(device), onDiagnostic: diagnostics.Add).Categories,
            item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver);

        var entry = Assert.Single(driver.Entries);
        Assert.Equal(FrontendMsiLiveUpdateVersionSource.IntelDsa, entry.LatestVersionSource);
        Assert.Equal("3.0.0.0", entry.Version);
        Assert.Contains(diagnostics, item => item.Outcome == "SuppressedByUniqueDsaFamily"
            && item.CandidateCount == 1 && item.Name == msiName);
    }

    [Fact]
    public void Intel_DSA_keeps_known_MSI_family_when_DSA_has_no_match_or_query_fails()
    {
        var deviceId = @"PCI\VEN_8086&DEV_1234";
        var device = Device(deviceId, [deviceId], "DISPLAY", "Intel Arc Graphics", "1.0.0.0", 0x8086, 0x1234);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Intel Graphics Driver", "2.0.0.0", null, null)]));
        var wrongDeviceDsa = new IntelDsaCatalogResult(true,
            [IntelPackage("wrong-device", "Intel Arc Graphics", false,
                IntelComponent("Graphics", "3.0.0.0", "VEN_8086&DEV_9999"))]);

        foreach (var intel in new[] { wrongDeviceDsa, IntelDsaCatalogResult.Failed("Http403") })
        {
            var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();
            var driver = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, intel, Inventory(device), onDiagnostic: diagnostics.Add).Categories,
                item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver);
            var entry = Assert.Single(driver.Entries);

            Assert.Equal("Intel Graphics Driver", entry.Name);
            Assert.Equal(FrontendMsiLiveUpdateVersionSource.Msi, entry.LatestVersionSource);
            Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
            Assert.Contains(diagnostics, item => item.Outcome == "RetainedNoDsa");
        }
    }

    [Fact]
    public void Intel_DSA_does_not_hide_a_known_MSI_family_with_conflicting_subsystem_identity()
    {
        var deviceId = @"PCI\VEN_8086&DEV_1234&SUBSYS_11112222";
        var device = Device(deviceId, [deviceId], "DISPLAY", "Intel Arc Graphics", "1.0.0.0", 0x8086, 0x1234);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Intel Graphics Driver", "2.0.0.0", null, null,
                DeviceId: @"PCI\VEN_8086&DEV_1234&SUBSYS_33334444")]));
        var intel = new IntelDsaCatalogResult(true,
            [IntelPackage("graphics", "Intel Arc Graphics", false,
                IntelComponent("Graphics", "3.0.0.0", "VEN_8086&DEV_1234"))]);

        var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();
        var driver = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, intel, Inventory(device), onDiagnostic: diagnostics.Add).Categories,
            item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver);

        Assert.Equal(2, driver.Entries.Count);
        Assert.Contains(driver.Entries, item => item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.Msi);
        Assert.Contains(driver.Entries, item => item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa);
        Assert.Contains(diagnostics, item => item.Outcome == "RetainedConflictingIdentity");
    }

    [Fact]
    public void Intel_DSA_does_not_use_family_fallback_when_multiple_present_devices_match()
    {
        var firstId = @"PCI\VEN_8086&DEV_1234";
        var secondId = @"PCI\VEN_8086&DEV_5678";
        var first = Device(firstId, [firstId], "DISPLAY", "Intel Arc A", "1.0.0.0", 0x8086, 0x1234);
        var second = Device(secondId, [secondId], "DISPLAY", "Intel Arc B", "1.0.0.1", 0x8086, 0x5678);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Intel Graphics Driver", "2.0.0.0", null, null)]));
        var intel = new IntelDsaCatalogResult(true,
            [IntelPackage("graphics", "Intel Arc Graphics", false,
                IntelComponent("Graphics", "3.0.0.0", "VEN_8086&DEV_1234", "VEN_8086&DEV_5678"))]);

        var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();
        var driver = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, intel, Inventory(first, second), onDiagnostic: diagnostics.Add).Categories,
            item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver);

        Assert.Equal(3, driver.Entries.Count);
        Assert.Contains(driver.Entries, item => item.Name == "Intel Graphics Driver"
            && item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.Msi);
        Assert.Equal(2, driver.Entries.Count(item => item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa));
        Assert.Contains(diagnostics, item => item.Outcome == "RetainedAmbiguousFamily" && item.CandidateCount == 2);
    }

    [Fact]
    public void Intel_DSA_family_fallback_preserves_other_Intel_and_non_Intel_driver_rows()
    {
        var deviceId = @"PCI\VEN_8086&DEV_1234";
        var device = Device(deviceId, [deviceId], "DISPLAY", "Intel Arc Graphics", "1.0.0.0", 0x8086, 0x1234);
        var retainedNames = new[]
        {
            "Intel Chipset Driver", "Intel IPF Driver", "Intel Management Engine Driver",
            "Intel Smart Sound Technology", "Realtek Audio Driver"
        };
        var rows = retainedNames.Select(name => new MsiLiveUpdateCatalogEntry(name, "2.0.0.0", null, null)).ToList();
        rows.Add(new("Intel Graphics Driver", "2.0.0.0", null, null));
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success, rows));
        var intel = new IntelDsaCatalogResult(true,
            [IntelPackage("graphics", "Intel Arc Graphics", false,
                IntelComponent("Graphics", "3.0.0.0", "VEN_8086&DEV_1234"))]);

        var entries = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, intel, Inventory(device)).Categories,
            item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries;

        Assert.Equal(6, entries.Count);
        Assert.All(retainedNames, name => Assert.Contains(entries, item => item.Name == name));
        Assert.DoesNotContain(entries, item => item.Name == "Intel Graphics Driver");
    }

    [Fact]
    public void MSI_driver_family_fallback_requires_unique_provider_class_and_distinctive_product_evidence()
    {
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Intel Graphics Driver", "2.0.0.0", null, null)]));
        var unrelatedId = @"PCI\VEN_8086&DEV_1234";
        var insufficientDevices = new[]
        {
            Device(unrelatedId, [unrelatedId], "DISPLAY", "Intel Arc Graphics", "1.0.0.0", 0x8086, 0x1234)
                with { DriverProvider = "Microsoft" },
            Device(unrelatedId, [unrelatedId], "NET", "Intel Arc Graphics", "1.0.0.0", 0x8086, 0x1234),
            Device(unrelatedId, [unrelatedId], "DISPLAY", "Intel Graphics", "1.0.0.0", 0x8086, 0x1234)
        };

        foreach (var device in insufficientDevices)
        {
            var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"), Inventory(device))
                .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

            Assert.Null(entry.InstalledVersion);
            Assert.Null(entry.InstalledVersionConfidence);
        }
    }

    [Fact]
    public void MSI_driver_family_fallback_reports_one_supported_candidate_as_heuristic()
    {
        var deviceId = @"PCI\VEN_8086&DEV_1234";
        var device = Device(deviceId, [deviceId], "DISPLAY", "Intel(R) Arc(TM) Graphics", "32.0.100.1", 0x8086, 0x1234);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Intel Graphics Driver", "32.0.101.9150", null, null)]));
        var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"),
                Inventory(device), onDiagnostic: diagnostics.Add)
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Equal("32.0.100.1", entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, entry.InstalledVersionConfidence);
        Assert.Contains(diagnostics, item => item.Category == MsiLiveUpdateCatalogCategory.Driver
            && item.Name == "Intel Graphics Driver" && item.VersionSource == "MSI"
            && item.Outcome == "HeuristicFamily" && item.Confidence == FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic
            && item.CandidateCount == 1 && item.VersionCount == 1);
    }

    [Fact]
    public void Realtek_audio_driver_matches_only_the_unique_observed_A2VM_codec_device()
    {
        var codecId = @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0256&SUBSYS_1462146C&REV_1000\5&2F390E51&0&0001";
        var codec = Device(codecId,
            [@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0256&SUBSYS_1462146C&REV_1000",
             @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0256&SUBSYS_1462146C",
             @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0256"],
            "MEDIA", "Realtek(R) Audio", "6.0.9738.1", 0x10EC, 0x0256)
            with { DriverProvider = "Realtek Semiconductor Corp.", InfName = null };
        var intelDevice = Device(GpuDeviceId, [GpuDeviceId], "DISPLAY", "Intel Arc Graphics", "32.0.100.1", 0x8086, 0x1234);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
        [
            new("Realtek Audio Driver", "6.0.9738.1", null, null),
            new("Intel Arc Graphics Driver", "32.0.100.4", null, null, DeviceId: GpuDeviceId)
        ]));
        var intel = new IntelDsaCatalogResult(true,
            [IntelPackage("930514", "Intel Arc Graphics", false,
                IntelComponent("Graphics", "32.0.101.9150", "VEN_8086&DEV_1234"))]);
        var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();

        var entries = MsiUpdateCatalogComposer.Compose(msi, intel, Inventory(codec, intelDevice),
                onDiagnostic: diagnostics.Add, hardwareModelId: "msi.claw.a2vm.8")
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries;
        var entry = Assert.Single(entries, item => item.Name == "Realtek Audio Driver");

        Assert.Equal("6.0.9738.1", entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, entry.InstalledVersionConfidence);
        Assert.DoesNotContain(entries, item => item.Name == "Intel Arc Graphics Driver");
        Assert.Contains(entries, item => item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa
            && item.Version == "32.0.101.9150");
        Assert.Contains(diagnostics, item => item.Name == "Realtek Audio Driver"
            && item.Outcome == "HeuristicFamily" && item.Confidence == FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic
            && item.CandidateCount == 1 && item.VersionCount == 1);
    }

    [Fact]
    public void Realtek_audio_driver_family_fallback_rejects_provider_class_and_candidate_ambiguity()
    {
        var codecId = @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0256&SUBSYS_1462146C&REV_1000\5&2F390E51&0&0001";
        var hardwareIds = new[]
        {
            @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0256&SUBSYS_1462146C&REV_1000",
            @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0256"
        };
        var realtekCodec = Device(codecId, hardwareIds, "MEDIA", "Realtek(R) Audio", "6.0.9738.1", 0x10EC, 0x0256)
            with { DriverProvider = "Realtek Semiconductor Corp.", InfName = null };
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Realtek Audio Driver", "6.0.9738.1", null, null)]));
        var collisions = new[]
        {
            realtekCodec with { DriverProvider = "Intel Corporation" },
            realtekCodec with { WmiDeviceClass = "SYSTEM", ClassName = "SYSTEM" },
            realtekCodec with { WmiDeviceName = "Realtek Audio Effects Component", FriendlyName = "Realtek Audio Effects Component" },
            realtekCodec with { HardwareIds = [@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0257"] }
        };

        foreach (var collision in collisions)
        {
            var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"),
                    Inventory(collision), hardwareModelId: "msi.claw.a2vm.8")
                .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);
            Assert.Null(entry.InstalledVersion);
            Assert.Null(entry.InstalledVersionConfidence);
        }

        var secondCodecId = @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0256&SUBSYS_1462146C&REV_1000\SECOND";
        var secondCodec = realtekCodec with { DeviceId = secondCodecId };
        var ambiguous = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"),
                Inventory(realtekCodec, secondCodec), hardwareModelId: "msi.claw.a2vm.8")
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);
        Assert.Null(ambiguous.InstalledVersion);
        Assert.Null(ambiguous.InstalledVersionConfidence);
    }

    [Fact]
    public void MSI_driver_diagnostics_distinguish_missing_identity_no_present_match_and_missing_version()
    {
        var matchingId = @"PCI\VEN_8086&DEV_1234";
        var missingVersionDevice = Device(matchingId, [matchingId], "DISPLAY", "Intel Arc Graphics", "1.0.0.0", 0x8086, 0x1234)
            with { DriverVersion = null };
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [
                new("Unknown OEM Package", "2.0.0.0", null, null),
                new("Intel Graphics Driver", "2.0.0.0", null, null, DeviceId: @"PCI\VEN_8086&DEV_9999"),
                new("Intel Graphics Driver", "2.0.0.0", null, null, DeviceId: matchingId)
            ]));
        var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();

        var snapshot = MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"),
            Inventory(missingVersionDevice), onDiagnostic: diagnostics.Add);
        var driverDiagnostics = diagnostics.Where(item => item.Category == MsiLiveUpdateCatalogCategory.Driver
            && item.VersionSource == "MSI").ToArray();

        Assert.Equal(3, driverDiagnostics.Length);
        Assert.Contains(driverDiagnostics, item => item.Name == "Unknown OEM Package" && item.Outcome == "NoUsableIdentity");
        Assert.Contains(driverDiagnostics, item => item.Name == "Intel Graphics Driver" && item.Outcome == "NoMatchingPresentDevice");
        Assert.Contains(driverDiagnostics, item => item.Name == "Intel Graphics Driver" && item.Outcome == "MissingDriverVersion"
            && item.CandidateCount == 1);
        Assert.Empty(snapshot.LocalInventoryWarnings!);
        Assert.DoesNotContain(diagnostics, item => item.Name?.Contains('\\') == true);
    }

    [Fact]
    public void MSI_driver_multiple_family_versions_remain_unknown_with_ambiguous_diagnostic()
    {
        var firstId = @"PCI\VEN_8086&DEV_1234";
        var secondId = @"PCI\VEN_8086&DEV_5678";
        var first = Device(firstId, [firstId], "DISPLAY", "Intel Arc A", "1.0.0.0", 0x8086, 0x1234);
        var second = Device(secondId, [secondId], "DISPLAY", "Intel Arc B", "2.0.0.0", 0x8086, 0x5678);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Intel Graphics Driver", "3.0.0.0", null, null)]));
        var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"),
                Inventory(first, second), onDiagnostic: diagnostics.Add)
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Null(entry.InstalledVersion);
        Assert.Null(entry.InstalledVersionConfidence);
        Assert.Contains(diagnostics, item => item.Outcome == "AmbiguousMatches"
            && item.CandidateCount == 2 && item.VersionCount == 2);
    }

    [Fact]
    public void Local_BIOS_PnP_registry_and_controller_revisions_are_projected_only_with_matching_identity()
    {
        var device = Device(GpuDeviceId, [GpuDeviceId], "DISPLAY", "Intel Arc", "32.0.100.1", 0x8086, 0x1234);
        var productCode = "00112233-4455-6677-8899-aabbccddeeff";
        var msi = new MsiLiveUpdateCatalogQueryResult(MsiLiveUpdateCatalogState.Completed,
            "Claw 8 AI+ A2VM", "kr", "win11",
            [
                new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("BIOS", "E1T52IMS.114", null, null), new("Intel ME FW Update Tool", "ME20011.1815", null, null)]),
                new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("Controller Firmware", "2.40", null, null)]),
                new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("Intel Arc Display Driver", "32.0.101.9150", null, null, DeviceId: GpuDeviceId)]),
                new(MsiLiveUpdateCatalogCategory.Utility, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("MSI Center M", "3.0.2608.3101", null, null, Guid: productCode)])
            ], DateTimeOffset.UtcNow);
        var inventory = new MsiUpdateInstalledInventory(
            ["E1T52IMS.113"], [device],
            [new("uninstall-key", productCode, null, "MSI Center M", "3.0.2501.0", "MSI")],
            new("0x0230", FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic), []);

        var snapshot = MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"), inventory);
        var bios = snapshot.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Bios).Entries;
        var firmware = Assert.Single(snapshot.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Firmware).Entries);
        var driver = Assert.Single(snapshot.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);
        var utility = Assert.Single(snapshot.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Equal("E1T52IMS.113", bios[0].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Exact, bios[0].InstalledVersionConfidence);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable, bios[0].ComparisonStatus);
        Assert.Null(bios[1].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, bios[1].ComparisonStatus);
        Assert.Equal("0x0230", firmware.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, firmware.InstalledVersionConfidence);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, firmware.ComparisonStatus);
        Assert.Equal("32.0.100.1", driver.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, driver.ComparisonStatus);
        Assert.Equal("3.0.2501.0", utility.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable, utility.ComparisonStatus);
    }

    [Theory]
    [InlineData("4.19", "4.19", FrontendMsiLiveUpdateInstalledVersionConfidence.Exact, FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("4.18", "4.19", FrontendMsiLiveUpdateInstalledVersionConfidence.Exact, FrontendMsiLiveUpdateComparisonStatus.Unknown)]
    [InlineData("0x0419", "0x0419", FrontendMsiLiveUpdateInstalledVersionConfidence.Exact, FrontendMsiLiveUpdateComparisonStatus.Unknown)]
    [InlineData("4.x", "4.x", FrontendMsiLiveUpdateInstalledVersionConfidence.Exact, FrontendMsiLiveUpdateComparisonStatus.Unknown)]
    [InlineData("4.19", "4.19", FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, FrontendMsiLiveUpdateComparisonStatus.Unknown)]
    public void Controller_firmware_verdict_requires_exact_owned_capture_and_equal_dotted_version(
        string installed, string offered,
        FrontendMsiLiveUpdateInstalledVersionConfidence confidence,
        FrontendMsiLiveUpdateComparisonStatus expected)
    {
        var catalogEntry = new MsiLiveUpdateCatalogEntry("Controller Firmware", offered, null, null,
            DeviceId: @"USB\VID_0DB0&PID_1901");
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Firmware,
            MsiLiveUpdateCatalogCategoryState.Success, [catalogEntry]));
        var inventory = new MsiUpdateInstalledInventory([], [], [], new(installed, confidence), []);

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
                IntelDsaCatalogResult.Failed("Http403"), inventory)
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Firmware).Entries);

        Assert.Equal(FrontendMsiLiveUpdateVersionKind.Catalog, entry.LatestVersionKind);
        Assert.Equal(expected, entry.ComparisonStatus);
    }

    [Fact]
    public void Controller_firmware_verdict_stays_unknown_without_a_capture_or_controller_identity()
    {
        var firmware = new MsiLiveUpdateCatalogEntry("Controller Firmware", "4.19", null, null,
            DeviceId: @"USB\VID_0DB0&PID_1902");
        var unrelated = new MsiLiveUpdateCatalogEntry("Wireless Firmware", "4.19", null, null);
        var msi = new MsiLiveUpdateCatalogQueryResult(MsiLiveUpdateCatalogState.Completed,
            "Claw 8 AI+ CG3EM", "kr", "win11",
            [
                new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Success, [firmware, unrelated]),
                new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Utility, MsiLiveUpdateCatalogCategoryState.Empty, [])
            ], DateTimeOffset.UtcNow);
        var inventory = new MsiUpdateInstalledInventory([], [], [], null, []);

        var entries = MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"), inventory)
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Firmware).Entries;

        Assert.All(entries, entry => Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus));

        var capturedInventory = inventory with
        {
            ControllerFirmware = new("4.19", FrontendMsiLiveUpdateInstalledVersionConfidence.Exact)
        };
        var wrongIdentity = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Firmware,
            MsiLiveUpdateCatalogCategoryState.Success,
            [new("Wireless Firmware", "4.19", null, null, DeviceId: @"USB\VID_1234&PID_5678"),
                new("Controller Firmware", "4.19", null, null, DeviceId: @"USB\VID_0DB0&PID_1903")]));
        var wrongIdentityEntries = MsiUpdateCatalogComposer.Compose(
                wrongIdentity, IntelDsaCatalogResult.Failed("Http403"), capturedInventory)
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Firmware).Entries;
        Assert.All(wrongIdentityEntries, entry => Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus));
    }

    [Theory]
    [InlineData("Goodix FingerPrinter Driver", "USB\\VID_27C6&PID_6090", "3.12144.0.160", "3.12144.0.100", "3.12144.0.100", FrontendMsiLiveUpdateVersionKind.Package, FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Goodix FingerPrinter Driver", "USB\\VID_27C6&PID_6090", "3.12144.0.100", "3.12144.0.100", "3.12144.0.100", FrontendMsiLiveUpdateVersionKind.Package, FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Intel Game Assist Driver", "PCI\\VEN_8086&DEV_E45D", "2.0.1801.30", "1.9.602.30", null, FrontendMsiLiveUpdateVersionKind.Catalog, FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Intel Game Assist Driver", "PCI\\VEN_8086&DEV_E45D", "1.9.602.30", "1.9.602.30", null, FrontendMsiLiveUpdateVersionKind.Catalog, FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Intel Game Assist Driver", "PCI\\VEN_8086&DEV_E45D", "1.8.602.30", "1.9.602.30", null, FrontendMsiLiveUpdateVersionKind.Catalog, FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable)]
    [InlineData("Intel Dynamic Tuning Driver", "ACPI\\INTC10D5", "9.1.10009.1745", "9.1.10009.1745", null, FrontendMsiLiveUpdateVersionKind.Catalog, FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Intel Trusted Execution Technology Driver", "ACPI\\VEN_INTC&DEV_1025", "1.21.18.0", "1.21.18.0", "1.21.18.0", FrontendMsiLiveUpdateVersionKind.Package, FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Intel PPM Provision Driver", "ACPI\\INTC1025", "1.0.0.255", "1.0.0.255", null, FrontendMsiLiveUpdateVersionKind.Catalog, FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Goodix FingerPrinter Driver", "USB\\VID_27C6&PID_6090", "3.12144.0.100", "3.12144.0.160", "3.12144.0.160", FrontendMsiLiveUpdateVersionKind.Package, FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable)]
    public void A2VM_legacy_single_device_MSI_driver_families_compare_only_exact_precise_matches(
        string name, string hardwareId, string installed, string offered, string? packageVersion,
        FrontendMsiLiveUpdateVersionKind expectedKind,
        FrontendMsiLiveUpdateComparisonStatus expectedStatus)
    {
        var device = DriverDevice(hardwareId + @"\INSTANCE_1", hardwareId,
            name.StartsWith("Goodix", StringComparison.Ordinal) ? "Biometric" : "SoftwareComponent",
            name.StartsWith("Goodix", StringComparison.Ordinal) ? "Goodix" : "Intel",
            installed);
        var catalogEntry = new MsiLiveUpdateCatalogEntry(name, offered, null, null,
            DeviceId: hardwareId, PackageVersion: packageVersion);

        var entry = ComposeDriverEntry(catalogEntry, device);

        Assert.Equal(installed, entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Exact, entry.InstalledVersionConfidence);
        Assert.Equal(expectedKind, entry.LatestVersionKind);
        Assert.Equal(expectedStatus, entry.ComparisonStatus);
    }

    [Fact]
    public void A2VM_legacy_single_device_MSI_driver_families_reject_conflicting_catalog_fields_and_vague_ids()
    {
        var preciseId = @"USB\VID_27C6&PID_6090";
        var device = DriverDevice(preciseId + @"\INSTANCE_1", preciseId, "Biometric", "Goodix", "3.12144.0.160");
        var conflict = ComposeDriverEntry(new MsiLiveUpdateCatalogEntry(
            "Goodix FingerPrinter Driver", "3.12144.0.100", null, null,
            DeviceId: preciseId, PackageVersion: "3.12144.0.101"), device);
        var vagueId = @"USB\VID_27C6";
        var vagueDevice = DriverDevice(vagueId + @"\INSTANCE_1", vagueId, "Biometric", "Goodix", "3.12144.0.160");
        var vague = ComposeDriverEntry(new MsiLiveUpdateCatalogEntry(
            "Goodix FingerPrinter Driver", "3.12144.0.100", null, null,
            DeviceId: vagueId, PackageVersion: "3.12144.0.100"), vagueDevice);
        var unsupported = ComposeDriverEntry(new MsiLiveUpdateCatalogEntry(
            "Unrelated OEM Driver", "3.12144.0.100", null, null,
            DeviceId: preciseId, PackageVersion: "3.12144.0.100"), device);
        var wrongHardwareId = ComposeDriverEntry(new MsiLiveUpdateCatalogEntry(
            "Goodix FingerPrinter Driver", "3.12144.0.100", null, null,
            DeviceId: @"USB\VID_27C6&PID_6091", PackageVersion: "3.12144.0.100"), device);
        var dynamicConflict = ComposeDriverEntry(new MsiLiveUpdateCatalogEntry(
            "Intel Dynamic Tuning Driver", "9.1.10009.1745", null, null,
            DeviceId: @"ACPI\INTC10D5", PackageVersion: "9.1.10009.1746"),
            DriverDevice(@"ACPI\INTC10D5\INSTANCE_1", @"ACPI\INTC10D5", "System", "Intel", "9.1.10009.1745"));
        var ppmMalformed = ComposeDriverEntry(new MsiLiveUpdateCatalogEntry(
            "Intel PPM Provision Driver", "1.0.0.x", null, null,
            DeviceId: @"ACPI\INTC1025"),
            DriverDevice(@"ACPI\INTC1025\INSTANCE_1", @"ACPI\INTC1025", "SoftwareComponent", "Intel", "1.0.0.x"));

        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, conflict.ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, vague.ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, unsupported.ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, wrongHardwareId.ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, dynamicConflict.ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, ppmMalformed.ComparisonStatus);
    }

    [Fact]
    public void A2VM_legacy_single_device_MSI_driver_families_reject_ambiguous_present_devices()
    {
        var hardwareId = @"USB\VID_27C6&PID_6090";
        var catalogEntry = new MsiLiveUpdateCatalogEntry(
            "Goodix FingerPrinter Driver", "3.12144.0.100", null, null,
            DeviceId: hardwareId, PackageVersion: "3.12144.0.100");
        var first = DriverDevice(hardwareId + @"\INSTANCE_1", hardwareId, "Biometric", "Goodix", "3.12144.0.160");
        var second = DriverDevice(hardwareId + @"\INSTANCE_2", hardwareId, "Biometric", "Goodix", "3.12144.0.160");

        var entry = ComposeDriverEntry(catalogEntry, first, second);

        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, entry.InstalledVersionConfidence);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Theory]
    [InlineData("DTS Driver", "DTS", "SoftwareComponent", "SWC\\VEN_DTSI&DEV_0001", "1.16.4.0", 2)]
    [InlineData("IntelliGo APO Driver", "intelliGo", "AudioProcessingObject", "SWC\\VEN_IGO&DEV_0002", "100.1.4.2709", 4)]
    public void A2VM_legacy_audio_driver_families_allow_only_same_version_family_equality(
        string name, string provider, string deviceClass, string hardwareId, string version, int candidateCount)
    {
        var devices = Enumerable.Range(1, candidateCount)
            .Select(index => DriverDevice($"{hardwareId}\\COMPONENT_{index}", hardwareId,
                index % 2 == 0 ? "SoftwareComponent" : deviceClass, provider, version))
            .ToArray();
        var catalogEntry = new MsiLiveUpdateCatalogEntry(name, version, null, null,
            DeviceId: hardwareId, PackageVersion: name == "IntelliGo APO Driver" ? version : null);

        var entry = ComposeDriverEntry(catalogEntry, devices);

        Assert.Equal(version, entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, entry.InstalledVersionConfidence);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, entry.ComparisonStatus);
    }

    [Fact]
    public void A2VM_legacy_audio_driver_families_keep_mixed_unrelated_broad_and_unequal_matches_unknown()
    {
        const string dtsId = @"SWC\VEN_DTSI&DEV_0001";
        var dtsOne = DriverDevice(dtsId + @"\COMPONENT_1", dtsId, "SoftwareComponent", "DTS", "1.16.4.0");
        var dtsTwo = DriverDevice(dtsId + @"\COMPONENT_2", dtsId, "AudioProcessingObject", "DTS", "1.16.4.1");
        var mixed = ComposeDriverEntry(new("DTS Driver", "1.16.4.0", null, null, DeviceId: dtsId), dtsOne, dtsTwo);

        var unrelated = DriverDevice(dtsId + @"\COMPONENT_2", dtsId, "AudioProcessingObject", "Other Audio", "1.16.4.0");
        var wrongFamily = ComposeDriverEntry(new("DTS Driver", "1.16.4.0", null, null, DeviceId: dtsId), dtsOne, unrelated);

        const string broadId = @"SWC\VEN_DTSI";
        var broadOne = DriverDevice(broadId + @"\COMPONENT_1", broadId, "SoftwareComponent", "DTS", "1.16.4.0");
        var broadTwo = DriverDevice(broadId + @"\COMPONENT_2", broadId, "AudioProcessingObject", "DTS", "1.16.4.0");
        var broad = ComposeDriverEntry(new("DTS Driver", "1.16.4.0", null, null, DeviceId: broadId), broadOne, broadTwo);

        var unequal = ComposeDriverEntry(new("DTS Driver", "1.16.4.1", null, null, DeviceId: dtsId),
            dtsOne, dtsOne with { DeviceId = dtsId + @"\COMPONENT_2" });

        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, mixed.ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, wrongFamily.ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, broad.ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, unequal.ComparisonStatus);
    }

    [Fact]
    public void UWP_and_store_catalog_rows_are_excluded_before_local_version_matching()
    {
        var msi = new MsiLiveUpdateCatalogQueryResult(MsiLiveUpdateCatalogState.Completed,
            "Claw 8 AI+ A2VM", "kr", "win11",
            [
                new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("Bios Store", "1.0", null, null, DiagnosticFields: StoreRowFields(".appx"))]),
                new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("Firmware Store", "1.0", null, null, DiagnosticFields: StoreRowFields(null, "StoreLink"))]),
                new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("Realtek Audio Control", "UWP", null, null)]),
                new(MsiLiveUpdateCatalogCategory.Utility, MsiLiveUpdateCatalogCategoryState.Success,
                [
                    new("Intel Graphics Command Center", "1.0", null, null,
                        DiagnosticFields: StoreRowFields(null, "StoreLink")),
                    new("Realtek Audio Control", "UWP", null, null),
                    new("DTS Audio Processing", "1.0", null, null,
                        DiagnosticFields: StoreRowFields(".msix")),
                    new("Intel Graphics Software", "1.0", null, null,
                        DiagnosticFields: StoreRowFields(null, "StoreLink")),
                    new("MSI App Player Installer CN", "1.0", null, null),
                    new("MSI App Player Installer", "1.0", null, null),
                    new("MSI Center M", "3.0.2608.3101", null, null,
                        Guid: "00112233-4455-6677-8899-aabbccddeeff")
                ])
            ], DateTimeOffset.UtcNow);
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", "00112233-4455-6677-8899-aabbccddeeff", null,
                "MSI Center M", "3.0.2501.0", "MSI")], null, []);
        var diagnostics = new List<MsiUpdateCatalogRowDiagnostic>();
        var snapshot = MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"), inventory,
            onRowDiagnostic: diagnostics.Add);

        foreach (var category in snapshot.Categories.Where(category => category.Category != FrontendMsiLiveUpdateCatalogCategory.Utility))
            Assert.True(category.Entries.Count == 0,
                $"{category.Category} retained: {string.Join(", ", category.Entries.Select(entry => entry.Name))}");
        var utility = Assert.Single(snapshot.Categories,
            category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Utility);
        Assert.Equal(FrontendMsiLiveUpdateCatalogCategoryState.Success, utility.State);
        var retained = Assert.Single(utility.Entries);
        Assert.Equal("MSI Center M", retained.Name);
        Assert.Equal("3.0.2501.0", retained.InstalledVersion);
        Assert.Equal(9, diagnostics.Count(item => item.UiDisposition == "ExcludedByPolicy"));
        Assert.Equal(7, diagnostics.Count(item => item.UiDisposition == "ExcludedByPolicy" && item.DispositionReason == "UwpCatalogEntry"));
        Assert.Equal(2, diagnostics.Count(item => item.UiDisposition == "ExcludedByPolicy" && item.DispositionReason == "ExcludedUtility"));
    }

    [Fact]
    public void CG3EM_frozen_catalog_counts_preserve_only_the_MSI_Center_M_utility()
    {
        const string graphicsId = @"PCI\VEN_8086&DEV_64A0";
        const string wirelessId = @"PCI\VEN_8086&DEV_2725";
        const string bluetoothId = @"USB\VID_8087&PID_0033";
        const string npuId = @"PCI\VEN_8086&DEV_B03E";
        const string centerMGuid = "30112233-4455-6677-8899-aabbccddeeff";
        var devices = new[]
        {
            Device(graphicsId, [graphicsId], "DISPLAY", "Intel Arc Graphics", "32.0.100.1", 0x8086, 0x64A0),
            Device(wirelessId, [wirelessId], "NET", "Intel Wi-Fi", "23.0.0.1", 0x8086, 0x2725),
            Device(bluetoothId, [bluetoothId], "Bluetooth", "Intel Wireless Bluetooth", "22.0.0.1", 0x8087, 0x0033),
            Device(npuId, [npuId], "System", "Intel AI Boost NPU", "31.0.100.1", 0x8086, 0xB03E)
        };
        var driverRows = new List<MsiLiveUpdateCatalogEntry>
        {
            new("Intel Graphics Driver", "32.0.101.9150", null, null, DeviceId: graphicsId),
            new("Intel Wireless LAN Driver", "24.70.0.3", null, null, DeviceId: wirelessId),
            new("Intel Bluetooth Driver", "24.70.0.4", null, null, DeviceId: bluetoothId),
            new("Intel NPU Driver", "32.0.100.5540", null, null, DeviceId: npuId)
        };
        driverRows.AddRange(Enumerable.Range(1, 19)
            .Select(index => new MsiLiveUpdateCatalogEntry($"Unmapped CG3EM Driver {index}", "1.2.3.4", null, null)));

        var utilityRows = new MsiLiveUpdateCatalogCategoryResult(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success,
            [
                new("Intel Graphics Software", "1.0", null, null,
                    DiagnosticFields: StoreRowFields(null, "StoreLink")),
                new("Intel Graphics Command Center", "UWP", null, null),
                new("Realtek Audio Control", "1.0", null, null,
                    DiagnosticFields: StoreRowFields(".appx")),
                new("MSI App Player Installer CN", "1.0", null, null),
                new("MSI App Player Installer", "1.0", null, null),
                new("MSI Center M", "3.0.2608.3101", null, null, Guid: centerMGuid)
            ]);
        var msi = new MsiLiveUpdateCatalogQueryResult(MsiLiveUpdateCatalogState.Completed,
            "Claw 8 AI+ EX", "kr", "win11",
            [
                new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("CG3EM BIOS", "E1T52IMS.113", null, null)]),
                new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("Controller Firmware", "4.19", null, null)]),
                new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success, driverRows),
                utilityRows
            ], DateTimeOffset.UtcNow);
        var intel = new IntelDsaCatalogResult(true,
            [
                IntelPackage("graphics", "Intel Arc Graphics", false,
                    IntelComponent("Graphics", "32.0.101.9150", "VEN_8086&DEV_64A0")),
                IntelPackage("wireless", "Intel Wi-Fi", false,
                    IntelComponent("Wireless", "24.70.0.3", "VEN_8086&DEV_2725")),
                IntelPackage("bluetooth", "Intel Bluetooth", false,
                    IntelComponent("Bluetooth", "24.70.0.4", "VID_8087&PID_0033")),
                IntelPackage("npu", "Intel NPU Driver", false,
                    IntelComponent("OtherDrivers", null, "VEN_8086&DEV_B03E"), version: "32.0.100.5540")
            ]);
        var inventory = new MsiUpdateInstalledInventory([], devices,
            [new($"{{{centerMGuid}}}", centerMGuid, null, "MSI Center M", "3.0.2501.0", "MSI")], null, []);
        var rowDiagnostics = new List<MsiUpdateCatalogRowDiagnostic>();

        var snapshot = MsiUpdateCatalogComposer.Compose(msi, intel, inventory,
            onRowDiagnostic: rowDiagnostics.Add, hardwareModelId: "msi.claw.cg3em");

        Assert.Equal(FrontendMsiLiveUpdateCatalogState.Completed, snapshot.State);
        Assert.Equal(FrontendMsiLiveUpdateSourceState.Completed, snapshot.MsiSource!.State);
        Assert.Equal(31, rowDiagnostics.Count);
        Assert.Equal(1, rowDiagnostics.Count(row => row.Category == MsiLiveUpdateCatalogCategory.Bios));
        Assert.Equal(1, rowDiagnostics.Count(row => row.Category == MsiLiveUpdateCatalogCategory.Firmware));
        Assert.Equal(23, rowDiagnostics.Count(row => row.Category == MsiLiveUpdateCatalogCategory.Driver));
        Assert.Equal(6, rowDiagnostics.Count(row => row.Category == MsiLiveUpdateCatalogCategory.Utility));
        Assert.Equal(5, rowDiagnostics.Count(row => row.UiDisposition == "ExcludedByPolicy"));
        Assert.Equal(3, rowDiagnostics.Count(row => row.DispositionReason == "UwpCatalogEntry"));
        Assert.Equal(2, rowDiagnostics.Count(row => row.DispositionReason == "ExcludedUtility"));
        Assert.Equal(Enumerable.Range(0, 6), rowDiagnostics
            .Where(row => row.Category == MsiLiveUpdateCatalogCategory.Utility)
            .Select(row => row.SourceIndex).OrderBy(index => index));

        var categories = snapshot.Categories.ToDictionary(category => category.Category);
        Assert.Single(categories[FrontendMsiLiveUpdateCatalogCategory.Bios].Entries);
        Assert.Single(categories[FrontendMsiLiveUpdateCatalogCategory.Firmware].Entries);
        Assert.Equal(23, categories[FrontendMsiLiveUpdateCatalogCategory.Driver].Entries.Count);
        Assert.Single(categories[FrontendMsiLiveUpdateCatalogCategory.Utility].Entries);
        Assert.Equal("MSI Center M", Assert.Single(categories[FrontendMsiLiveUpdateCatalogCategory.Utility].Entries).Name);
        Assert.Equal(4, categories[FrontendMsiLiveUpdateCatalogCategory.Driver].Entries.Count(entry =>
            entry.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa));
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable,
            Assert.Single(categories[FrontendMsiLiveUpdateCatalogCategory.Utility].Entries).ComparisonStatus);
    }

    private static MsiLiveUpdateCatalogRawDiagnosticFields StoreRowFields(string? extension = null, string fileKind = "Missing") =>
        new("Missing", [], "Missing", null, "Missing", null, "Missing", "Missing", null,
            "Missing", null, fileKind, extension, "Missing", null, "Missing", null);

    [Fact]
    public void Synthetic_A2VM_capture_preserves_legacy_evidenced_version_verdicts()
    {
        const string goodixId = @"USB\VID_27C6&PID_6090";
        const string gameAssistId = @"PCI\VEN_8086&DEV_E45D";
        const string dynamicTuningId = @"ACPI\INTC10D5";
        const string trustedExecutionId = @"ACPI\VEN_INTC&DEV_1025";
        const string ppmId = @"ACPI\INTC1025";
        const string dtsId = @"SWC\VEN_DTSI&AID_DTSI2";
        const string intelliGoId = @"SWC\VEN_IGO&CID_APO";
        const string npuId = @"PCI\VEN_8086&DEV_B03E";

        var firmware = new MsiLiveUpdateCatalogEntry("Controller Firmware", "4.19", null, null,
            DeviceId: @"USB\VID_0DB0&PID_1901");
        var drivers = new MsiLiveUpdateCatalogCategoryResult(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success,
            [
                new("Goodix FingerPrinter Driver", "3.12144.0.100", null, null,
                    DeviceId: goodixId, PackageVersion: "3.12144.0.100"),
                new("Intel Game Assist Driver", "1.9.602.30", null, null, DeviceId: gameAssistId),
                new("Intel Dynamic Tuning Driver", "9.1.10009.1745", null, null, DeviceId: dynamicTuningId),
                new("Intel Trusted Execution Technology Driver", "1.21.18.0", null, null,
                    DeviceId: trustedExecutionId, PackageVersion: "1.21.18.0"),
                new("Intel PPM Provision Driver", "1.0.0.255", null, null, DeviceId: ppmId),
                new("DTS Driver", "1.16.4.0", null, null, DeviceId: dtsId),
                new("IntelliGo APO Driver", "100.1.4.2709", null, null,
                    DeviceId: intelliGoId, PackageVersion: "100.1.4.2709"),
                new("Unmatched Chipset Driver", "10.1.56.28", null, null)
            ]);
        var utilities = new MsiLiveUpdateCatalogCategoryResult(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success,
            [new("Realtek Audio Control", "UWP", null, null), new("DTS Audio Processing", "UWP", null, null)]);
        var msi = new MsiLiveUpdateCatalogQueryResult(MsiLiveUpdateCatalogState.Completed,
            "Claw 8 AI+ A2VM", "kr", "win11",
            [
                new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Success, [firmware]),
                drivers,
                utilities
            ], DateTimeOffset.UtcNow);
        var pnpDevices = new List<MsiUpdateInstalledPnpDevice>
        {
            DriverDevice(goodixId + @"\INSTANCE_1", goodixId, "Biometric", "Goodix", "3.12144.0.160"),
            DriverDevice(gameAssistId + @"\INSTANCE_1", gameAssistId, "System", "Intel", "2.0.1801.30"),
            DriverDevice(dynamicTuningId + @"\INSTANCE_1", dynamicTuningId, "System", "Intel", "9.1.10009.1745"),
            DriverDevice(trustedExecutionId + @"\INSTANCE_1", trustedExecutionId, "System", "Intel", "1.21.18.0"),
            DriverDevice(ppmId + @"\INSTANCE_1", ppmId, "SoftwareComponent", "Intel", "1.0.0.255"),
            DriverDevice(dtsId + @"\COMPONENT_1", dtsId, "SoftwareComponent", "DTS", "1.16.4.0"),
            DriverDevice(dtsId + @"\COMPONENT_2", dtsId, "AudioProcessingObject", "DTS", "1.16.4.0"),
            DriverDevice(intelliGoId + @"\COMPONENT_1", intelliGoId, "SoftwareComponent", "IntelliGo", "100.1.4.2709"),
            DriverDevice(intelliGoId + @"\COMPONENT_2", intelliGoId, "SoftwareComponent", "IntelliGo", "100.1.4.2709"),
            DriverDevice(intelliGoId + @"\COMPONENT_3", intelliGoId, "AudioProcessingObject", "IntelliGo", "100.1.4.2709"),
            DriverDevice(intelliGoId + @"\COMPONENT_4", intelliGoId, "AudioProcessingObject", "IntelliGo", "100.1.4.2709"),
            Device(npuId, [npuId], "ComputeAccelerator", "Intel AI Boost NPU", "32.0.100.5540", 0x8086, 0xB03E)
        };
        var inventory = new MsiUpdateInstalledInventory([], pnpDevices, [],
            new("4.19", FrontendMsiLiveUpdateInstalledVersionConfidence.Exact), []);
        var npuPackage = IntelPackage("npu", "Intel NPU Driver", false,
            IntelComponent("OtherDrivers", null, "VEN_8086&DEV_B03E"), version: "32.0.100.5540");

        var snapshot = MsiUpdateCatalogComposer.Compose(msi,
            new(true, [npuPackage]), inventory, hardwareModelId: "msi.claw.a2vm.8");
        var msiEntries = snapshot.Categories.SelectMany(category => category.Entries)
            .Where(entry => entry.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.Msi)
            .ToDictionary(entry => entry.Name, StringComparer.Ordinal);
        var dsaNpu = Assert.Single(snapshot.Categories.Single(category =>
            category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries,
            entry => entry.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa);

        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate,
            msiEntries["Controller Firmware"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate,
            msiEntries["Goodix FingerPrinter Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate,
            msiEntries["Intel Game Assist Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate,
            msiEntries["Intel Dynamic Tuning Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate,
            msiEntries["Intel Trusted Execution Technology Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate,
            msiEntries["Intel PPM Provision Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, msiEntries["DTS Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, msiEntries["IntelliGo APO Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, msiEntries["Unmatched Chipset Driver"].ComparisonStatus);
        Assert.DoesNotContain("Realtek Audio Control", msiEntries.Keys);
        Assert.DoesNotContain("DTS Audio Processing", msiEntries.Keys);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, dsaNpu.ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateVersionKind.Package, dsaNpu.LatestVersionKind);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic,
            msiEntries["DTS Driver"].InstalledVersionConfidence);
    }

    [Fact]
    public void CG3EM_fixed_mapping_covers_all_EX_driver_catalog_rows()
    {
        string[] catalogNames =
        [
            "Windows Studio Effects",
            "Intel HID Event Filter",
            "Integrated Sensor Solution",
            "Intel Chipset Driver",
            "Intel Platform Monitoring Technology Driver",
            "Intel Serial IO Driver",
            "Intel Dynamic Tuning Driver",
            "Intel IPF Driver",
            "Intel Management Engine Driver",
            "Intel Trusted Execution Technology Driver",
            "Intel PPM Provision Driver",
            "Intel Game Assist Driver",
            "IntelliGo APO Driver",
            "Intel Smart Sound Technology driver",
            "Realtek OED Driver",
            "Realtek Audio Driver",
            "DTS Driver",
            "Goodix FingerPrinter Driver",
            "Realtek Card Reader Driver"
        ];
        var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();
        var entries = catalogNames.Select(name => new MsiLiveUpdateCatalogEntry(name, "1.2.3.4", null, null)).ToArray();
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success, entries));

        var snapshot = MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"),
            Inventory(), onDiagnostic: diagnostics.Add, hardwareModelId: "msi.claw.cg3em");
        var composed = snapshot.Categories.Single(category =>
            category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries;

        Assert.Equal(catalogNames.Length, composed.Count);
        Assert.Equal(catalogNames.Length, diagnostics.Count);
        Assert.All(diagnostics.Where(diagnostic => diagnostic.Name is not
                ("Windows Studio Effects" or "Integrated Sensor Solution" or "Realtek OED Driver" or "Realtek Audio Driver")),
            diagnostic => Assert.Equal("Cg3emMappedNoMatchingPresentDevice", diagnostic.Outcome));
        Assert.All(diagnostics.Where(diagnostic => diagnostic.Name is
                ("Windows Studio Effects" or "Integrated Sensor Solution" or "Realtek OED Driver" or "Realtek Audio Driver")),
            diagnostic => Assert.Equal("Cg3emInvalidOemPackageIdentity", diagnostic.Outcome));
    }

    [Fact]
    public void CG3EM_unmapped_catalog_driver_does_not_fall_through_to_legacy_identity_matching()
    {
        const string hardwareId = @"PCI\VEN_8086&DEV_E402";
        var device = DriverDevice(hardwareId + @"\INSTANCE", hardwareId, "System", "Intel", "10.1.56.28");
        var entry = ComposeCg3emDriverEntry(
            new("Unmapped MSI Driver", "10.1.56.28", null, null, DeviceId: hardwareId, DriverVersion: "10.1.56.28"),
            device);

        Assert.Null(entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_fixed_mapping_compares_INF_backed_versions_and_coherent_device_groups()
    {
        const string dtsCore = @"SWC\VEN_DTSI&AID_DTSI2";
        const string dtsController = @"SWC\VEN_DTSI&HID_DTSIAPO4xCONTROLLER";
        const string dtsService = @"SWC\VEN_DTSI&HID_DTSIAPO4xSERVICE";
        const string dtsDap = @"SWC\VEN_DTSI&HID_DTSIDAP";
        const string intelliGoApo = @"SWC\VEN_IGO&CID_APO";
        const string intelliGoService = @"SWC\VEN_IGO&AUDIOSERVICE";
        const string dtt = @"SWC\VID8086_DTT_0001";
        const string dttConfig = @"SWC\VID8086_DTTCFG_0002";
        const string dttTech = @"SWC\VID8086_DTTTECH_0003";
        const string ppm = @"SWC\VID8086_PPM_1008_1.0";
        const string gameAssist = @"SWC\VID8086_DTT_GA_1.0";
        const string ipf = @"ACPI\INTC10D5";
        const string ipfEffect = @"SWC\VID8086_IPFEF_1.0";
        const string realtekAudio06 = @"SOUNDWIRE\SDCA_10&MAN_025D&FUNC_0721&TYPE_06&VER_01&ADR_01&LID_03&UID_00&SUBSYS_15101462";
        const string realtekAudio03 = @"SOUNDWIRE\SDCA_10&MAN_025D&FUNC_0721&TYPE_03&VER_01&ADR_02&LID_03&UID_00&SUBSYS_15101462";
        const string realtekAudio01 = @"SOUNDWIRE\SDCA_10&MAN_025D&FUNC_0721&TYPE_01&VER_01&ADR_04&LID_03&UID_00&SUBSYS_15101462";
        const string realtekOedE328 = @"INTELAUDIO\DSP_CTLR_DEV_E328&VEN_8086&DEV_0222&SUBSYS_15101462";
        const string realtekOedE428 = @"INTELAUDIO\DSP_CTLR_DEV_E428&VEN_8086&DEV_0222&SUBSYS_15101462";
        const string goodix = @"USB\VID_27C6&PID_6090";
        const string cardReader = @"PCI\VEN_10EC&DEV_5264";
        const string cameraEffects = @"SWC\MEP_CAM&VEN_8086_DEV_B03E";
        const string hidEvent = @"ACPI\INTC10CC";
        const string iss = @"PCI\VEN_8086&DEV_E345";
        const string chipsetE402 = @"PCI\VEN_8086&DEV_E402";
        const string chipsetE422 = @"PCI\VEN_8086&DEV_E422";
        const string chipsetE423 = @"PCI\VEN_8086&DEV_E423";
        const string pmt = @"PCI\VEN_8086&DEV_B07D";
        const string serialIoAcpi = @"ACPI\INTC105F";
        const string serialIoPci = @"PCI\VEN_8086&DEV_E450";
        const string managementEngine = @"PCI\VEN_8086&DEV_E470";
        const string trustedExecution = @"ACPI\INTC1025";
        const string smartSound = @"PCI\VEN_8086&DEV_E428";

        var entries = new MsiLiveUpdateCatalogEntry[]
        {
            new("Windows Studio Effects", "3.0.8.0", null, null, PackageVersion: "3.0.8.0",
                OemPackageIdentity: "dism_mep,audio,component.inf_AudioProcessingObject_Microsoft"),
            new("Intel HID Event Filter", "2.2.2.17", null, null),
            new("Integrated Sensor Solution", "5.8.58.0", null, null, PackageVersion: "5.8.61.0",
                OemPackageIdentity: "dism_ishheci.inf_System_Intel"),
            new("Intel Chipset Driver", "10.1.56.28", null, null),
            new("Intel Platform Monitoring Technology Driver", "7.3.11.3", null, null),
            new("Intel Serial IO Driver", "30.100.2534.18", null, null),
            new("Intel Dynamic Tuning Driver", "9.1.10009.1745", null, null),
            new("Intel IPF Driver", "2.3.20303.5058", null, null),
            new("Intel Management Engine Driver", "2603.9.4.0", null, null),
            new("Intel Trusted Execution Technology Driver", "1.21.18.0", null, null),
            new("Intel PPM Provision Driver", "1.0.0.255", null, null),
            new("Intel Game Assist Driver", "1.9.602.30", null, null),
            new("IntelliGo APO Driver", "100.1.4.2709", null, null),
            new("Intel Smart Sound Technology driver", "20.43.12741.0", null, null),
            new("Realtek OED Driver", "20.43.274.9", null, null, PackageVersion: "20.43.274.9",
                OemPackageIdentity: "dism_intcoed,oemlibpath.inf_Extension_Realtek"),
            new("Realtek Audio Driver", "10.0.378.1", null, null, PackageVersion: "10.0.378.1",
                OemPackageIdentity: "dism_rtksdcaxumsi.inf_Extension_Realtek"),
            new("DTS Driver", "1.16.4.0", null, null),
            new("Goodix FingerPrinter Driver", "3.12144.0.100", null, null, PackageVersion: "3.12144.0.100"),
            new("Realtek Card Reader Driver", "10.0.26100.21378", null, null, PackageVersion: "10.0.26100.21378")
        };
        var devices = new[]
        {
            DriverDevice(cameraEffects + @"\INSTANCE", cameraEffects, "SoftwareComponent", "Microsoft", "3.0.8.0"),
            DriverDevice(hidEvent + @"\INSTANCE", hidEvent, "HIDClass", "Intel(R) Corporation", "2.2.2.17"),
            DriverDevice(iss + @"\INSTANCE", iss, "System", "Intel", "5.8.58.0"),
            DriverDevice(chipsetE402 + @"\INSTANCE", chipsetE402, "System", "Intel", "10.1.56.28"),
            DriverDevice(chipsetE422 + @"\INSTANCE", chipsetE422, "System", "Intel", "10.1.56.28"),
            DriverDevice(chipsetE423 + @"\INSTANCE", chipsetE423, "System", "Intel", "10.1.56.28"),
            DriverDevice(pmt + @"\INSTANCE", pmt, "System", "Intel", "7.3.11.3"),
            DriverDevice(serialIoAcpi + @"\INSTANCE", serialIoAcpi, "System", "Intel", "30.100.2534.18"),
            DriverDevice(serialIoPci + @"\INSTANCE", serialIoPci, "System", "Intel", "30.100.2534.18"),
            DriverDevice(dtt + @"\INSTANCE", dtt, "SoftwareComponent", "Intel", "9.1.10009.1745"),
            DriverDevice(dttConfig + @"\INSTANCE", dttConfig, "SoftwareComponent", "Intel", "9.1.10009.1745"),
            DriverDevice(dttTech + @"\INSTANCE", dttTech, "SoftwareComponent", "Intel", "9.1.10009.1745"),
            DriverDevice(ipf + @"\INSTANCE", ipf, "System", "Intel", "2.3.20303.5058"),
            DriverDevice(ipfEffect + @"\INSTANCE", ipfEffect, "SoftwareComponent", "Intel", "2.3.20303.5058"),
            DriverDevice(managementEngine + @"\INSTANCE", managementEngine, "System", "Intel", "2603.9.4.0"),
            DriverDevice(trustedExecution + @"\INSTANCE", trustedExecution, "System", "Intel", "1.21.18.0"),
            DriverDevice(ppm + @"\INSTANCE", ppm, "SoftwareComponent", "Intel", "1.0.0.255"),
            DriverDevice(gameAssist + @"\INSTANCE", gameAssist, "SoftwareComponent", "Intel", "2.0.1801.30"),
            DriverDevice(intelliGoApo + @"\INSTANCE", intelliGoApo, "AudioProcessingObject", "intelliGo", "100.1.4.2709"),
            DriverDevice(intelliGoService + @"\INSTANCE", intelliGoService, "SoftwareComponent", "intelliGo", "100.1.4.2709"),
            DriverDevice(@"SWC\VEN_IGO&CID_UNRELATED\INSTANCE", @"SWC\VEN_IGO&CID_UNRELATED", "AudioProcessingObject", "intelliGo", "99.0.0.0"),
            DriverDevice(smartSound + @"\INSTANCE", smartSound, "System", "Intel", "20.43.12741.0"),
            DriverDevice(realtekOedE328 + @"\INSTANCE", realtekOedE328, "Extension", "Realtek Semiconductor Corp.", "20.43.274.9"),
            DriverDevice(realtekOedE428 + @"\INSTANCE", realtekOedE428, "Extension", "Realtek Semiconductor Corp.", "20.43.274.9"),
            DriverDevice(realtekAudio06 + @"\INSTANCE", realtekAudio06, "Extension", "Realtek", "10.0.378.1"),
            DriverDevice(realtekAudio03 + @"\INSTANCE", realtekAudio03, "Extension", "Realtek", "10.0.378.1"),
            DriverDevice(realtekAudio01 + @"\INSTANCE", realtekAudio01, "Extension", "Realtek", "10.0.378.1"),
            DriverDevice(dtsCore + @"\CORE", dtsCore, "AudioProcessingObject", "DTS", "1.16.4.0"),
            DriverDevice(dtsCore + @"\SOFTWARE", dtsCore, "SoftwareComponent", "DTS", "1.16.4.0"),
            DriverDevice(dtsController + @"\INSTANCE", dtsController, "SoftwareComponent", "DTS", "1.16.4.0"),
            DriverDevice(dtsService + @"\INSTANCE", dtsService, "SoftwareComponent", "DTS", "1.16.4.0"),
            DriverDevice(dtsDap + @"\INSTANCE", dtsDap, "SoftwareComponent", "DTS", "1.10.7.0"),
            DriverDevice(goodix + @"\INSTANCE", goodix, "Biometric", "Goodix", "3.12144.0.160"),
            DriverDevice(cardReader + @"\INSTANCE", cardReader, "MTD", "Realtek", "10.0.26200.21389"),
            DriverDevice(@"PCI\VEN_8086\UNRELATED", @"PCI\VEN_8086", "System", "Intel", "10.1.56.28")
        };
        var driverCategory = new MsiLiveUpdateCatalogCategoryResult(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success, entries);
        var inventory = Inventory(devices) with
        {
            DriverStore = new("Completed",
            [
                new("oem901.inf", "mep_audio_component.inf", "Microsoft Corporation", "AudioProcessingObject", "3.0.8.0"),
                new("oem677.inf", "ishheci.inf", "Intel", "System", "5.8.61.0"),
                new("oem902.inf", "IntcOED_OemLibPath.inf", "Realtek Semiconductor Corp.", "Extension", "20.43.274.009"),
                new("oem903.inf", "RtkSdcaXuMSI.inf", "Realtek", "Extension", "10.0.378.1")
            ])
        };
        var snapshot = MsiUpdateCatalogComposer.Compose(MsiResult(driverCategory),
            IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: "msi.claw.cg3em");
        var composed = snapshot.Categories.Single(category =>
            category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries
            .ToDictionary(entry => entry.Name, StringComparer.Ordinal);

        Assert.Equal("10.1.56.28", composed["Intel Chipset Driver"].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Intel Chipset Driver"].ComparisonStatus);
        Assert.Equal("30.100.2534.18", composed["Intel Serial IO Driver"].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Intel Serial IO Driver"].ComparisonStatus);
        Assert.Equal("7.3.11.3", composed["Intel Platform Monitoring Technology Driver"].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate,
            composed["Intel Platform Monitoring Technology Driver"].ComparisonStatus);
        Assert.Equal("2603.9.4.0", composed["Intel Management Engine Driver"].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate,
            composed["Intel Management Engine Driver"].ComparisonStatus);
        Assert.Equal("20.43.12741.0", composed["Intel Smart Sound Technology driver"].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate,
            composed["Intel Smart Sound Technology driver"].ComparisonStatus);
        Assert.Equal("10.0.26200.21389", composed["Realtek Card Reader Driver"].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Realtek Card Reader Driver"].ComparisonStatus);
        Assert.Equal("2.0.1801.30", composed["Intel Game Assist Driver"].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Intel Game Assist Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Goodix FingerPrinter Driver"].ComparisonStatus);
        Assert.Equal("1.16.4.0", composed["DTS Driver"].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["DTS Driver"].ComparisonStatus);
        Assert.Equal("100.1.4.2709", composed["IntelliGo APO Driver"].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["IntelliGo APO Driver"].ComparisonStatus);
        Assert.Equal("5.8.61.0", composed["Integrated Sensor Solution"].InstalledVersion);
        Assert.Equal("5.8.61.0", composed["Integrated Sensor Solution"].Version);
        Assert.Equal(FrontendMsiLiveUpdateVersionKind.Package, composed["Integrated Sensor Solution"].LatestVersionKind);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Integrated Sensor Solution"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Windows Studio Effects"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Intel HID Event Filter"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Intel Dynamic Tuning Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Intel IPF Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Intel Trusted Execution Technology Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Intel PPM Provision Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Realtek OED Driver"].ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed["Realtek Audio Driver"].ComparisonStatus);
    }

    [Theory]
    [InlineData("Windows Studio Effects", "3.0.8.0", "3.0.8.0", "UpToDate")]
    [InlineData("Windows Studio Effects", "3.0.7.0", "3.0.8.0", "UpdateAvailable")]
    [InlineData("Windows Studio Effects", "3.0.9.0", "3.0.8.0", "UpToDate")]
    [InlineData("Realtek OED Driver", "20.43.274.009", "20.43.274.9", "UpToDate")]
    [InlineData("Realtek OED Driver", "20.43.273.9", "20.43.274.9", "UpdateAvailable")]
    [InlineData("Realtek OED Driver", "20.43.275.9", "20.43.274.9", "UpToDate")]
    [InlineData("Realtek Audio Driver", "10.0.378.1", "10.0.378.1", "UpToDate")]
    [InlineData("Realtek Audio Driver", "10.0.377.1", "10.0.378.1", "UpdateAvailable")]
    [InlineData("Realtek Audio Driver", "10.0.379.1", "10.0.378.1", "UpToDate")]
    public void CG3EM_package_rows_compare_only_exact_DriverStore_package_versions(
        string name, string installedVersion, string packageVersion, string expectedStatus)
    {
        var row = GetCg3emDriverStoreCase(name);
        var installed = new MsiUpdateInstalledDriverPackage("oem100.inf", row.OriginalInfName,
            row.Provider, row.DeviceClass, installedVersion);
        var catalog = new MsiLiveUpdateCatalogEntry(name, "999.0.0.0", null, null,
            PackageVersion: packageVersion, DriverVersion: "999.0.0.0",
            OemPackageIdentity: row.OemPackageIdentity);

        var composed = ComposeCg3emDriverStoreEntry(catalog, new("Completed", [installed]));

        Assert.Equal(installedVersion, composed.InstalledVersion);
        Assert.Equal(packageVersion, composed.Version);
        Assert.Equal(FrontendMsiLiveUpdateVersionKind.Package, composed.LatestVersionKind);
        Assert.Equal(Enum.Parse<FrontendMsiLiveUpdateComparisonStatus>(expectedStatus), composed.ComparisonStatus);
    }

    [Theory]
    [InlineData("Windows Studio Effects")]
    [InlineData("Realtek OED Driver")]
    [InlineData("Realtek Audio Driver")]
    public void CG3EM_package_rows_reject_missing_or_wrong_identity_evidence(string name)
    {
        var row = GetCg3emDriverStoreCase(name);
        var package = new MsiUpdateInstalledDriverPackage("oem100.inf", row.OriginalInfName,
            row.Provider, row.DeviceClass, row.PackageVersion);
        var exactCatalogFields = new MsiLiveUpdateCatalogEntry(name, row.PackageVersion, null, null,
            PackageVersion: row.PackageVersion, OemPackageIdentity: row.OemPackageIdentity);
        var noPackage = ComposeCg3emDriverStoreEntry(exactCatalogFields, new("Completed", []));
        var wrongIdentity = ComposeCg3emDriverStoreEntry(
            exactCatalogFields with { OemPackageIdentity = row.OemPackageIdentity + "_other" },
            new("Completed", [package]));
        var wrongInf = ComposeCg3emDriverStoreEntry(exactCatalogFields,
            new("Completed", [package with { OriginalInfName = "different.inf" }]));
        var wrongClass = ComposeCg3emDriverStoreEntry(exactCatalogFields,
            new("Completed", [package with { Class = "System" }]));
        var wrongProvider = ComposeCg3emDriverStoreEntry(exactCatalogFields,
            new("Completed", [package with
            {
                Provider = string.Equals(row.Provider, "Realtek", StringComparison.OrdinalIgnoreCase)
                    ? "Realtek Semiconductor Corp."
                    : "Realtek"
            }]));

        foreach (var result in new[] { noPackage, wrongIdentity, wrongInf, wrongClass, wrongProvider })
        {
            Assert.Null(result.InstalledVersion);
            Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, result.ComparisonStatus);
        }
        Assert.Equal("NoMatchingInstalledDriverPackage",
            ComposeCg3emDriverStoreOutcome(exactCatalogFields, new("Completed", [])));
    }

    [Fact]
    public void CG3EM_package_match_uses_case_insensitive_INF_provider_and_class_identity()
    {
        var row = GetCg3emDriverStoreCase("Realtek OED Driver");
        var catalog = new MsiLiveUpdateCatalogEntry(row.CatalogName, null, null, null,
            PackageVersion: row.PackageVersion, OemPackageIdentity: row.OemPackageIdentity.ToUpperInvariant());
        var package = new MsiUpdateInstalledDriverPackage("OEM755.INF",
            row.OriginalInfName.ToUpperInvariant(), row.Provider.ToUpperInvariant(),
            row.DeviceClass.ToUpperInvariant(), "20.43.274.009");

        var composed = ComposeCg3emDriverStoreEntry(catalog, new("Completed", [package]));

        Assert.Equal("20.43.274.009", composed.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_package_comparison_does_not_fall_back_to_other_catalog_version_fields()
    {
        var row = GetCg3emDriverStoreCase("Windows Studio Effects");
        var entry = new MsiLiveUpdateCatalogEntry(row.CatalogName, row.PackageVersion, null, null,
            DriverVersion: row.PackageVersion, OemPackageIdentity: row.OemPackageIdentity);
        var package = new MsiUpdateInstalledDriverPackage("oem901.inf", row.OriginalInfName,
            row.Provider, row.DeviceClass, row.PackageVersion);

        var composed = ComposeCg3emDriverStoreEntry(entry, new("Completed", [package]));

        Assert.Equal(row.PackageVersion, composed.InstalledVersion);
        Assert.Null(composed.Version);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, composed.ComparisonStatus);
    }

    [Theory]
    [InlineData("Windows Studio Effects", "Microsoft")]
    [InlineData("Realtek OED Driver", "Realtek")]
    [InlineData("Realtek Audio Driver", "Realtek Semiconductor Corp.")]
    public void CG3EM_package_rows_do_not_accept_provider_name_only_matches(string name, string broadProvider)
    {
        var row = GetCg3emDriverStoreCase(name);
        var entry = new MsiLiveUpdateCatalogEntry(name, row.PackageVersion, null, null,
            PackageVersion: row.PackageVersion, OemPackageIdentity: row.OemPackageIdentity);
        var result = ComposeCg3emDriverStoreEntry(entry, new("Completed",
        [
            new("oem100.inf", row.OriginalInfName, broadProvider, row.DeviceClass, row.PackageVersion)
        ]));

        Assert.Null(result.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, result.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_package_rows_reject_conflicting_duplicates_and_unavailable_capture()
    {
        var row = GetCg3emDriverStoreCase("Realtek OED Driver");
        var entry = new MsiLiveUpdateCatalogEntry(row.CatalogName, row.PackageVersion, null, null,
            PackageVersion: row.PackageVersion, OemPackageIdentity: row.OemPackageIdentity);
        var first = new MsiUpdateInstalledDriverPackage("oem100.inf", row.OriginalInfName,
            row.Provider, row.DeviceClass, "20.43.274.9");
        var second = first with { PublishedInfName = "oem101.inf", Version = "20.43.275.9" };

        var conflicting = ComposeCg3emDriverStoreEntry(entry, new("Completed", [first, second]));
        var unavailable = ComposeCg3emDriverStoreEntry(entry, new("TimedOut", [], "Cg3emDriverStoreTimedOut"));

        Assert.Null(conflicting.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, conflicting.ComparisonStatus);
        Assert.Null(unavailable.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, unavailable.ComparisonStatus);
    }

    [Theory]
    [InlineData("Integrated Sensor Solution")]
    [InlineData("Windows Studio Effects")]
    [InlineData("Realtek OED Driver")]
    [InlineData("Realtek Audio Driver")]
    public void CG3EM_package_rows_remain_unknown_when_DriverStore_capture_times_out(string catalogName)
    {
        var row = GetCg3emDriverStoreCase(catalogName);
        var entry = new MsiLiveUpdateCatalogEntry(row.CatalogName, row.PackageVersion, null, null,
            PackageVersion: row.PackageVersion, OemPackageIdentity: row.OemPackageIdentity);

        var composed = ComposeCg3emDriverStoreEntry(entry,
            new("TimedOut", [], "Cg3emDriverStoreTimedOut"));

        Assert.Null(composed.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, composed.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_WSE_and_Realtek_package_matches_ignore_unrelated_matching_PnP_devices()
    {
        var row = GetCg3emDriverStoreCase("Windows Studio Effects");
        var pnp = DriverDevice(@"SWC\MEP_CAM&VEN_8086_DEV_B03E\INSTANCE", @"SWC\MEP_CAM&VEN_8086_DEV_B03E",
            "SoftwareComponent", "Microsoft", "3.0.8.0");
        var catalog = new MsiLiveUpdateCatalogEntry(row.CatalogName, row.PackageVersion, null, null,
            PackageVersion: row.PackageVersion, OemPackageIdentity: row.OemPackageIdentity);
        var result = ComposeCg3emDriverStoreEntry(catalog, MsiUpdateDriverStoreCapture.NotRequested, pnp);

        Assert.Null(result.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, result.ComparisonStatus);
    }

    [Theory]
    [InlineData("5.8.60.0", "UpdateAvailable")]
    [InlineData("5.8.61.0", "UpToDate")]
    [InlineData("5.8.62.0", "UpToDate")]
    public void CG3EM_ISS_compares_only_the_exact_HECI_INF_package_domain(
        string installedHeciVersion, string expectedStatus)
    {
        var row = GetCg3emDriverStoreCase("Integrated Sensor Solution");
        var catalog = new MsiLiveUpdateCatalogEntry(row.CatalogName, "5.8.58.0", null, null,
            PackageVersion: "5.8.61.0", OemPackageIdentity: row.OemPackageIdentity);
        var issDevice = DriverDevice(@"PCI\VEN_8086&DEV_E445\ISS", @"PCI\VEN_8086&DEV_E445",
            "System", "Intel", "5.8.58.0");
        var heciPackage = new MsiUpdateInstalledDriverPackage("oem677.inf", row.OriginalInfName,
            row.Provider, row.DeviceClass, installedHeciVersion);

        var composed = ComposeCg3emDriverStoreEntry(catalog, new("Completed", [heciPackage]), issDevice);

        Assert.Equal(installedHeciVersion, composed.InstalledVersion);
        Assert.Equal("5.8.61.0", composed.Version);
        Assert.Equal(FrontendMsiLiveUpdateVersionKind.Package, composed.LatestVersionKind);
        Assert.Equal(Enum.Parse<FrontendMsiLiveUpdateComparisonStatus>(expectedStatus), composed.ComparisonStatus);
        Assert.Equal("Cg3emInstalledInfPackageMatch",
            ComposeCg3emDriverStoreOutcome(catalog, new("Completed", [heciPackage])));
    }

    [Fact]
    public void CG3EM_ISS_main_device_version_alone_never_becomes_HECI_package_version()
    {
        var row = GetCg3emDriverStoreCase("Integrated Sensor Solution");
        var catalog = new MsiLiveUpdateCatalogEntry(row.CatalogName, "5.8.58.0", null, null,
            PackageVersion: "5.8.61.0", OemPackageIdentity: row.OemPackageIdentity);
        var issDevice = DriverDevice(@"PCI\VEN_8086&DEV_E445\ISS", @"PCI\VEN_8086&DEV_E445",
            "System", "Intel", "5.8.58.0");
        var activeOnly = ComposeCg3emDriverStoreEntry(catalog, MsiUpdateDriverStoreCapture.NotRequested, issDevice);

        Assert.Null(activeOnly.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, activeOnly.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_ISS_rejects_wrong_HECI_identity_and_conflicting_package_candidates()
    {
        var row = GetCg3emDriverStoreCase("Integrated Sensor Solution");
        var catalog = new MsiLiveUpdateCatalogEntry(row.CatalogName, "5.8.58.0", null, null,
            PackageVersion: "5.8.61.0", OemPackageIdentity: row.OemPackageIdentity);
        var package = new MsiUpdateInstalledDriverPackage("oem677.inf", row.OriginalInfName,
            row.Provider, row.DeviceClass, "5.8.61.0");
        var wrongOriginalInf = ComposeCg3emDriverStoreEntry(catalog,
            new("Completed", [package with { OriginalInfName = "oem677.inf" }]));
        var wrongProvider = ComposeCg3emDriverStoreEntry(catalog,
            new("Completed", [package with { Provider = "Intel Corporation" }]));
        var wrongClass = ComposeCg3emDriverStoreEntry(catalog,
            new("Completed", [package with { Class = "SoftwareComponent" }]));
        var conflicting = ComposeCg3emDriverStoreEntry(catalog, new("Completed",
        [
            package,
            package with { PublishedInfName = "oem678.inf", Version = "5.8.62.0" }
        ]));

        foreach (var result in new[] { wrongOriginalInf, wrongProvider, wrongClass, conflicting })
        {
            Assert.Null(result.InstalledVersion);
            Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, result.ComparisonStatus);
        }
    }

    [Fact]
    public void CG3EM_ISS_selects_only_the_started_package_when_versions_conflict()
    {
        var row = GetCg3emDriverStoreCase("Integrated Sensor Solution");
        var entry = new MsiLiveUpdateCatalogEntry(row.CatalogName, "5.8.58.0", null, null,
            PackageVersion: "5.8.61.0", OemPackageIdentity: row.OemPackageIdentity);
        var packages = new[]
        {
            new MsiUpdateInstalledDriverPackage("oem7.inf", row.OriginalInfName,
                row.Provider, row.DeviceClass, "5.8.30.0") { HasStartedDevice = false },
            new MsiUpdateInstalledDriverPackage("oem677.inf", row.OriginalInfName,
                row.Provider, row.DeviceClass, "5.8.61.0") { HasStartedDevice = true }
        };

        var composed = ComposeCg3emDriverStoreEntry(entry, new("Completed", packages));

        Assert.Equal("5.8.61.0", composed.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_Realtek_OED_selects_the_started_extension_package()
    {
        var row = GetCg3emDriverStoreCase("Realtek OED Driver");
        var entry = new MsiLiveUpdateCatalogEntry(row.CatalogName, null, null, null,
            PackageVersion: "20.43.274.9", OemPackageIdentity: row.OemPackageIdentity);
        var packages = new[]
        {
            new MsiUpdateInstalledDriverPackage("oem439.inf", row.OriginalInfName,
                row.Provider, row.DeviceClass, "20.42.274.2") { HasStartedDevice = false },
            new MsiUpdateInstalledDriverPackage("oem642.inf", row.OriginalInfName,
                row.Provider, row.DeviceClass, "20.43.274.009") { HasStartedDevice = true }
        };

        var composed = ComposeCg3emDriverStoreEntry(entry, new("Completed", packages));

        Assert.Equal("20.43.274.009", composed.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_conflicting_started_packages_remain_unknown()
    {
        var row = GetCg3emDriverStoreCase("Integrated Sensor Solution");
        var entry = new MsiLiveUpdateCatalogEntry(row.CatalogName, "5.8.58.0", null, null,
            PackageVersion: "5.8.61.0", OemPackageIdentity: row.OemPackageIdentity);
        var packages = new[]
        {
            new MsiUpdateInstalledDriverPackage("oem677.inf", row.OriginalInfName,
                row.Provider, row.DeviceClass, "5.8.61.0") { HasStartedDevice = true },
            new MsiUpdateInstalledDriverPackage("oem678.inf", row.OriginalInfName,
                row.Provider, row.DeviceClass, "5.8.62.0") { HasStartedDevice = true }
        };

        var composed = ComposeCg3emDriverStoreEntry(entry, new("Completed", packages));

        Assert.Null(composed.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, composed.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_conflicting_packages_with_no_started_binding_remain_unknown()
    {
        var row = GetCg3emDriverStoreCase("Realtek OED Driver");
        var entry = new MsiLiveUpdateCatalogEntry(row.CatalogName, null, null, null,
            PackageVersion: row.PackageVersion, OemPackageIdentity: row.OemPackageIdentity);
        var packages = new[]
        {
            new MsiUpdateInstalledDriverPackage("oem439.inf", row.OriginalInfName,
                row.Provider, row.DeviceClass, "20.42.274.2") { HasStartedDevice = false },
            new MsiUpdateInstalledDriverPackage("oem642.inf", row.OriginalInfName,
                row.Provider, row.DeviceClass, "20.43.274.9") { HasStartedDevice = false }
        };

        var composed = ComposeCg3emDriverStoreEntry(entry, new("Completed", packages));

        Assert.Null(composed.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, composed.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_partial_class_lookup_keeps_single_package_matches()
    {
        var row = GetCg3emDriverStoreCase("Windows Studio Effects");
        var entry = new MsiLiveUpdateCatalogEntry(row.CatalogName, row.PackageVersion, null, null,
            PackageVersion: row.PackageVersion, OemPackageIdentity: row.OemPackageIdentity);
        var package = new MsiUpdateInstalledDriverPackage("oem901.inf", row.OriginalInfName,
            row.Provider, row.DeviceClass, row.PackageVersion);

        var composed = ComposeCg3emDriverStoreEntry(entry,
            new("Partial", [package], "Cg3emDriverStoreClassLookupFailed"));

        Assert.Equal(row.PackageVersion, composed.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_partial_class_lookup_keeps_ambiguous_package_matches_unknown()
    {
        var row = GetCg3emDriverStoreCase("Integrated Sensor Solution");
        var entry = new MsiLiveUpdateCatalogEntry(row.CatalogName, "5.8.58.0", null, null,
            PackageVersion: "5.8.61.0", OemPackageIdentity: row.OemPackageIdentity);
        var packages = new[]
        {
            new MsiUpdateInstalledDriverPackage("oem7.inf", row.OriginalInfName,
                row.Provider, row.DeviceClass, "5.8.30.0"),
            new MsiUpdateInstalledDriverPackage("oem677.inf", row.OriginalInfName,
                row.Provider, row.DeviceClass, "5.8.61.0")
        };

        var composed = ComposeCg3emDriverStoreEntry(entry,
            new("Partial", packages, "Cg3emDriverStoreClassLookupFailed"));

        Assert.Null(composed.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, composed.ComparisonStatus);
    }

    [Fact]
    public void A2VM_ISS_does_not_consume_CG3EM_DriverStore_package_evidence()
    {
        var row = GetCg3emDriverStoreCase("Integrated Sensor Solution");
        var catalog = new MsiLiveUpdateCatalogEntry(row.CatalogName, "5.8.58.0", null, null,
            PackageVersion: "5.8.61.0", OemPackageIdentity: row.OemPackageIdentity);
        var inventory = new MsiUpdateInstalledInventory([], [], [], null, [])
        {
            DriverStore = new("Completed",
                [new("oem677.inf", row.OriginalInfName, row.Provider, row.DeviceClass, "5.8.61.0")])
        };
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success, [catalog]));

        var composed = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
                IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: "msi.claw.a2vm.8")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Null(composed.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, composed.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_DTS_prefers_the_core_version_and_uses_only_verified_fallbacks()
    {
        const string coreId = @"SWC\VEN_DTSI&AID_DTSI2";
        const string controllerId = @"SWC\VEN_DTSI&HID_DTSIAPO4xCONTROLLER";
        const string serviceId = @"SWC\VEN_DTSI&HID_DTSIAPO4xSERVICE";
        const string dapId = @"SWC\VEN_DTSI&HID_DTSIDAP";
        var core = DriverDevice(coreId + @"\CORE", coreId, "AudioProcessingObject", "DTS", "1.16.4.0");
        var controller = DriverDevice(controllerId + @"\INSTANCE", controllerId, "SoftwareComponent", "DTS", "1.16.4.0");
        var service = DriverDevice(serviceId + @"\INSTANCE", serviceId, "SoftwareComponent", "DTS", "1.16.4.0");
        var dap = DriverDevice(dapId + @"\INSTANCE", dapId, "SoftwareComponent", "DTS", "1.10.7.0");

        var current = ComposeCg3emDriverEntry(new("DTS Driver", "1.16.4.0", null, null), core, controller, service, dap);
        var outdated = ComposeCg3emDriverEntry(new("DTS Driver", "1.16.4.0", null, null),
            core with { DriverVersion = "1.16.3.0" }, dap);
        var fallback = ComposeCg3emDriverEntry(new("DTS Driver", "1.16.4.0", null, null), controller, service, dap);
        var conflictingFallback = ComposeCg3emDriverEntry(new("DTS Driver", "1.16.4.0", null, null),
            controller, service with { DriverVersion = "1.16.3.0" }, dap);
        var conflictingCoreAndFallback = ComposeCg3emDriverEntry(new("DTS Driver", "1.16.4.0", null, null),
            core, controller with { DriverVersion = "1.16.3.0" }, service, dap);
        var dapOnly = ComposeCg3emDriverEntry(new("DTS Driver", "1.16.4.0", null, null), dap);

        Assert.Equal("1.16.4.0", current.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, current.ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable, outdated.ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, fallback.ComparisonStatus);
        Assert.Null(conflictingFallback.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, conflictingFallback.ComparisonStatus);
        Assert.Null(conflictingCoreAndFallback.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, conflictingCoreAndFallback.ComparisonStatus);
        Assert.Null(dapOnly.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, dapOnly.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_fixed_mapping_preserves_strong_catalog_identity_within_the_mapped_family()
    {
        const string chipsetE402 = @"PCI\VEN_8086&DEV_E402";
        const string chipsetE422 = @"PCI\VEN_8086&DEV_E422";
        var first = DriverDevice(chipsetE402 + @"\ONE", chipsetE402, "System", "Intel", "10.1.56.28")
            with { InfName = "oem402.inf" };
        var second = DriverDevice(chipsetE422 + @"\TWO", chipsetE422, "System", "Intel", "10.1.54.0")
            with { InfName = "oem422.inf" };

        var exactDeviceId = ComposeCg3emDriverEntry(
            new("Intel Chipset Driver", "10.1.56.28", null, null, DeviceId: chipsetE402), first, second);
        var exactInfName = ComposeCg3emDriverEntry(
            new("Intel Chipset Driver", "10.1.56.28", null, null, InfName: "oem402.inf"), first, second);

        Assert.Equal("10.1.56.28", exactDeviceId.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, exactDeviceId.ComparisonStatus);
        Assert.Equal("10.1.56.28", exactInfName.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, exactInfName.ComparisonStatus);
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    public void CG3EM_fixed_mapping_does_not_run_for_A2VM_models(string model)
    {
        const string identity = @"SWC\VEN_DTSI&AID_DTSI2";
        var dts = DriverDevice(identity + @"\CORE", identity, "AudioProcessingObject", "DTS", "1.16.4.0");
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success,
            [new("DTS Driver", "1.16.4.0", null, null)]));

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
                IntelDsaCatalogResult.Failed("Http403"), Inventory(dts), hardwareModelId: model)
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Null(entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Fact]
    public void A2VM_legacy_DTS_multi_component_matching_remains_unchanged()
    {
        const string identity = @"SWC\VEN_DTSI&DEV_1234";
        var entry = new MsiLiveUpdateCatalogEntry("DTS Driver", "1.16.4.0", null, null, DeviceId: identity);
        var devices = new[]
        {
            DriverDevice(identity + @"\CORE", identity, "AudioProcessingObject", "DTS", "1.16.4.0"),
            DriverDevice(identity + @"\SERVICE", identity, "SoftwareComponent", "DTS", "1.16.4.0")
        };
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success, [entry]));

        var composed = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
                IntelDsaCatalogResult.Failed("Http403"), Inventory(devices), hardwareModelId: "msi.claw.a2vm.8")
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Equal("1.16.4.0", composed.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpToDate, composed.ComparisonStatus);
    }

    [Fact]
    public void CG3EM_fixed_mapping_rejects_conflicting_versions_wrong_provider_and_AppX_substitutes()
    {
        const string chipset = @"PCI\VEN_8086&DEV_E402";
        const string goodix = @"USB\VID_27C6&PID_6090";
        const string realtekAudio = @"SOUNDWIRE\SDCA_10&MAN_025D&FUNC_0721&TYPE_06&VER_01&ADR_01&LID_03&UID_00&SUBSYS_15101462";
        var conflictedChipset = ComposeCg3emDriverEntry(new("Intel Chipset Driver", "10.1.56.28", null, null),
            DriverDevice(chipset + @"\ONE", chipset, "System", "Intel", "10.1.56.28"),
            DriverDevice(@"PCI\VEN_8086&DEV_E422\TWO", @"PCI\VEN_8086&DEV_E422", "System", "Intel", "10.1.55.0"));
        var wrongGoodixProvider = ComposeCg3emDriverEntry(new("Goodix FingerPrinter Driver", "3.12144.0.100", null, null),
            DriverDevice(goodix + @"\INSTANCE", goodix, "Biometric", "Other Vendor", "3.12144.0.100"));
        var conflictingCatalogVersions = ComposeCg3emDriverEntry(new("Goodix FingerPrinter Driver", "3.12144.0.100", null, null,
                PackageVersion: "3.12144.0.101"),
            DriverDevice(goodix + @"\INSTANCE", goodix, "Biometric", "Goodix", "3.12144.0.160"));
        var realtekWithoutExactDevice = MsiUpdateCatalogComposer.Compose(
                MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("Realtek Audio Driver", "10.0.378.1", null, null, PackageVersion: "10.0.378.1")])),
                IntelDsaCatalogResult.Failed("Http403"),
                new MsiUpdateInstalledInventory([], [], [], null, []),
                hardwareModelId: "msi.claw.cg3em")
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries.Single();
        var broadIntel = ComposeCg3emDriverEntry(new("Intel Chipset Driver", "10.1.56.28", null, null),
            DriverDevice(@"PCI\VEN_8086\OTHER", @"PCI\VEN_8086", "System", "Intel", "10.1.56.28"));
        var wrongSoundwireClass = ComposeCg3emDriverEntry(new("Realtek Audio Driver", "10.0.378.1", null, null),
            DriverDevice(realtekAudio + @"\CODEC", realtekAudio, "MEDIA", "Realtek", "10.0.378.1"));

        Assert.Null(conflictedChipset.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, conflictedChipset.ComparisonStatus);
        Assert.Null(wrongGoodixProvider.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, wrongGoodixProvider.ComparisonStatus);
        Assert.Equal("3.12144.0.160", conflictingCatalogVersions.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, conflictingCatalogVersions.ComparisonStatus);
        Assert.Null(realtekWithoutExactDevice.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, realtekWithoutExactDevice.ComparisonStatus);
        Assert.Null(broadIntel.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, broadIntel.ComparisonStatus);
        Assert.Null(wrongSoundwireClass.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, wrongSoundwireClass.ComparisonStatus);
    }

    [Fact]
    public void Utility_exclusions_are_exact_case_insensitive_and_precede_installed_version_matching()
    {
        const string excludedGuid1 = "00112233-4455-6677-8899-aabbccddeeff";
        const string excludedGuid2 = "10112233-4455-6677-8899-aabbccddeeff";
        const string excludedGuid3 = "20112233-4455-6677-8899-aabbccddeeff";
        const string msiCenterGuid = "30112233-4455-6677-8899-aabbccddeeff";
        var utilities = new MsiLiveUpdateCatalogCategoryResult(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success,
            [
                new(" MSI App Player Installer CN ", "1.0", null, null, Guid: excludedGuid1),
                new("intel graphics software", "1.0", null, null, Guid: excludedGuid2),
                new("MSI App Player Installer", "1.0", null, null, Guid: excludedGuid3),
                new("MSI Center M", "3.0.2608.3101", null, null, Guid: msiCenterGuid),
                new("Intel Graphics Command Center", "UWP", null, null)
            ]);
        var msi = new MsiLiveUpdateCatalogQueryResult(MsiLiveUpdateCatalogState.Completed,
            "Claw 8 AI+ A2VM", "kr", "win11",
            [
                new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("BIOS", "E1T52IMS.113", null, null)]),
                new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("Controller Firmware", "2.0.0.0", null, null)]),
                new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("Intel IPF Driver", "1.2.3.4", null, null)]),
                utilities
            ], DateTimeOffset.UtcNow);
        var inventory = new MsiUpdateInstalledInventory([], [],
            [
                new("{00112233-4455-6677-8899-aabbccddeeff}", excludedGuid1, null, "MSI App Player Installer CN", "1.0", "MSI"),
                new("{10112233-4455-6677-8899-aabbccddeeff}", excludedGuid2, null, "Intel Graphics Software", "1.0", "Intel"),
                new("{20112233-4455-6677-8899-aabbccddeeff}", excludedGuid3, null, "MSI App Player Installer", "1.0", "MSI"),
                new("{30112233-4455-6677-8899-aabbccddeeff}", msiCenterGuid, null, "MSI Center M", "3.0.2501.0", "MSI")
            ],
            null, []);
        var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();

        var snapshot = MsiUpdateCatalogComposer.Compose(msi, new(true, []), inventory,
            onDiagnostic: diagnostics.Add);
        var utility = Assert.Single(snapshot.Categories,
            category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Utility);

        Assert.Equal(FrontendMsiLiveUpdateCatalogCategoryState.Success, utility.State);
        Assert.Equal(new[] { "MSI Center M" }, utility.Entries.Select(entry => entry.Name));
        Assert.Equal("3.0.2501.0", utility.Entries[0].InstalledVersion);
        Assert.Single(snapshot.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Bios).Entries);
        Assert.Single(snapshot.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Firmware).Entries);
        Assert.Contains(snapshot.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries,
            entry => entry.Name == "Intel IPF Driver");
        Assert.Contains(diagnostics, item => item.Category == MsiLiveUpdateCatalogCategory.Utility
            && item.Outcome == "ExcludedByPolicy" && item.FilteredCount == 4 && item.Name is null);
        Assert.Equal(1, diagnostics.Count(item => item.Category == MsiLiveUpdateCatalogCategory.Utility
            && item.VersionSource == "MSI" && item.Outcome == "MatchedRegistry"));
        Assert.DoesNotContain(diagnostics, item => item.Name is "MSI App Player Installer CN"
            or "intel graphics software" or "MSI App Player Installer");
    }

    [Fact]
    public void All_excluded_utility_rows_remain_a_successful_empty_category()
    {
        var msi = new MsiLiveUpdateCatalogQueryResult(MsiLiveUpdateCatalogState.Completed,
            "Claw 8 AI+ A2VM", "kr", "win11",
            [
                new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Utility, MsiLiveUpdateCatalogCategoryState.Success,
                    [
                        new("MSI App Player Installer CN", "1.0", null, null),
                        new("Intel Graphics Software", "1.0", null, null),
                        new("MSI App Player Installer", "1.0", null, null)
                    ])
            ], DateTimeOffset.UtcNow);

        var snapshot = MsiUpdateCatalogComposer.Compose(msi, new(true, []), new([], [], [], null, []));
        var utility = Assert.Single(snapshot.Categories,
            category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Utility);

        Assert.Equal(FrontendMsiLiveUpdateCatalogState.Completed, snapshot.State);
        Assert.Equal(FrontendMsiLiveUpdateCatalogCategoryState.Success, utility.State);
        Assert.Empty(utility.Entries);
    }

    [Fact]
    public void Shared_INF_version_is_labeled_heuristic_instead_of_exact()
    {
        var first = Device(@"PCI\VEN_8086&DEV_1234", [@"PCI\VEN_8086&DEV_1234"], "NET", "Intel Network A", "1.2.3.4", 0x8086, 0x1234);
        var second = Device(@"PCI\VEN_8086&DEV_5678", [@"PCI\VEN_8086&DEV_5678"], "NET", "Intel Network B", "1.2.3.4", 0x8086, 0x5678);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Network Driver", "1.2.4.0", null, null, InfName: "oem1.inf")]));

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"),
                Inventory(first, second))
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Equal("1.2.3.4", entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, entry.InstalledVersionConfidence);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Theory]
    [InlineData("Graphics", "VEN_8086&DEV_1234", "PCI\\VEN_8086&DEV_1234", "DISPLAY", "Intel Arc Graphics", 0x8086, 0x1234, "1.2.3.4", "1.2.3.5", FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable)]
    [InlineData("Graphics", "VEN_8086&DEV_1234", "PCI\\VEN_8086&DEV_1234", "DISPLAY", "Intel Arc Graphics", 0x8086, 0x1234, "1.2.3.5", "1.2.3.5", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Graphics", "VEN_8086&DEV_1234", "PCI\\VEN_8086&DEV_1234", "DISPLAY", "Intel Arc Graphics", 0x8086, 0x1234, "1.2.3.6", "1.2.3.5", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Wireless", "VEN_8086&DEV_5678", "PCI\\VEN_8086&DEV_5678", "NET", "Intel Wi-Fi", 0x8086, 0x5678, "24.70.0.3", "24.70.0.4", FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable)]
    [InlineData("Wireless", "VEN_8086&DEV_5678", "PCI\\VEN_8086&DEV_5678", "NET", "Intel Wi-Fi", 0x8086, 0x5678, "24.70.0.4", "24.70.0.4", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Wireless", "VEN_8086&DEV_5678", "PCI\\VEN_8086&DEV_5678", "NET", "Intel Wi-Fi", 0x8086, 0x5678, "24.70.0.5", "24.70.0.4", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Bluetooth", "VID_8087&PID_0033", "USB\\VID_8087&PID_0033", "Bluetooth", "Intel Bluetooth", 0x8087, 0x0033, "24.70.0.3", "24.70.0.4", FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable)]
    [InlineData("Bluetooth", "VID_8087&PID_0033", "USB\\VID_8087&PID_0033", "Bluetooth", "Intel Bluetooth", 0x8087, 0x0033, "24.70.0.4", "24.70.0.4", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("Bluetooth", "VID_8087&PID_0033", "USB\\VID_8087&PID_0033", "Bluetooth", "Intel Bluetooth", 0x8087, 0x0033, "24.70.0.5", "24.70.0.4", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    public void Intel_DSA_driver_versions_compare_only_for_exact_GPU_WiFi_and_Bluetooth_matches(
        string category, string detectionValue, string instanceId, string deviceClass, string deviceName,
        int vendorId, int productId, string installed, string offered,
        FrontendMsiLiveUpdateComparisonStatus expected)
    {
        var device = Device(instanceId, [instanceId], deviceClass, deviceName, installed,
            (ushort)vendorId, (ushort)productId);
        var package = IntelPackage("compare", "Intel package", false,
            IntelComponent(category, offered, detectionValue));
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Empty, []));

        var result = MsiUpdateCatalogComposer.Compose(msi, new(true, [package]), Inventory(device));
        var entry = Assert.Single(result.Categories.Single(item =>
            item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Equal(FrontendMsiLiveUpdateVersionSource.IntelDsa, entry.LatestVersionSource);
        Assert.Equal(FrontendMsiLiveUpdateVersionKind.Driver, entry.LatestVersionKind);
        Assert.Equal(expected, entry.ComparisonStatus);
    }

    [Fact]
    public void Intel_NPU_package_version_never_compares_with_its_installed_driver_version()
    {
        var deviceId = @"PCI\VEN_8086&DEV_643E";
        var device = Device(deviceId, [deviceId], "System", "Intel AI Boost NPU", "32.0.90.1", 0x8086, 0x643E);
        var package = IntelPackage("npu", "Intel NPU Driver", false,
            IntelComponent("OtherDrivers", null, "VEN_8086&DEV_643E"), version: "32.0.100.5540");
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Empty, []));

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, new(true, [package]), Inventory(device))
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Equal(FrontendMsiLiveUpdateVersionKind.Package, entry.LatestVersionKind);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Theory]
    [InlineData("32.0.100.5540", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("32.0.100.5539", FrontendMsiLiveUpdateComparisonStatus.Unknown)]
    [InlineData("32.0.100.5541", FrontendMsiLiveUpdateComparisonStatus.Unknown)]
    public void Intel_NPU_package_verdict_allows_exact_identity_equality_only(
        string installed, FrontendMsiLiveUpdateComparisonStatus expected)
    {
        var deviceId = @"PCI\VEN_8086&DEV_643E";
        var device = Device(deviceId, [deviceId], "System", "Intel AI Boost NPU", installed, 0x8086, 0x643E);
        var package = IntelPackage("npu", "Intel NPU Driver", false,
            IntelComponent("OtherDrivers", null, "VEN_8086&DEV_643E"), version: "32.0.100.5540");

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(
                MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Empty, [])),
                new(true, [package]), Inventory(device))
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Equal(FrontendMsiLiveUpdateVersionKind.Package, entry.LatestVersionKind);
        Assert.Equal(expected, entry.ComparisonStatus);
    }

    [Fact]
    public void Intel_NPU_package_verdict_rejects_malformed_equal_versions()
    {
        var deviceId = @"PCI\VEN_8086&DEV_643E";
        var device = Device(deviceId, [deviceId], "System", "Intel AI Boost NPU", "not-a-version", 0x8086, 0x643E);
        var package = IntelPackage("npu", "Intel NPU Driver", false,
            IntelComponent("OtherDrivers", null, "VEN_8086&DEV_643E"), version: "not-a-version");

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(
                MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Empty, [])),
                new(true, [package]), Inventory(device))
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);

        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Fact]
    public void Intel_NPU_package_verdict_does_not_transfer_equal_versions_to_another_hardware_id()
    {
        var installedId = @"PCI\VEN_8086&DEV_B03E";
        var unrelatedPackage = IntelPackage("npu", "Intel NPU Driver", false,
            IntelComponent("OtherDrivers", null, "VEN_8086&DEV_B03F"), version: "32.0.100.5540");
        var installedDevice = Device(installedId, [installedId], "ComputeAccelerator",
            "Intel AI Boost NPU", "32.0.100.5540", 0x8086, 0xB03E);

        var snapshot = MsiUpdateCatalogComposer.Compose(
            MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Empty, [])),
            new(true, [unrelatedPackage]), Inventory(installedDevice));

        Assert.DoesNotContain(snapshot.Categories.SelectMany(category => category.Entries),
            entry => entry.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa);
    }

    [Theory]
    [InlineData("1.2.3.4", "1.2.3.5", FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable)]
    [InlineData("1.2.3.5", "1.2.3.5", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("1.2.3.6", "1.2.3.5", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    public void MSI_driver_compares_only_a_unique_exact_identity_and_typed_driver_version(
        string installed, string offered, FrontendMsiLiveUpdateComparisonStatus expected)
    {
        Assert.Equal(expected, ComposeMsiDriverComparison(installed, offered));
    }

    [Fact]
    public void MSI_unique_exact_INF_can_compare_but_heuristic_shared_INF_cannot()
    {
        var one = Device(@"PCI\VEN_1234&DEV_5678", [@"PCI\VEN_1234&DEV_5678"],
            "SYSTEM", "Test Device", "1.2.3.4", 0x1234, 0x5678);
        var exactInf = new MsiLiveUpdateCatalogEntry("OEM Device Driver", "1.2.3.5", null, null,
            DriverVersion: "1.2.3.5", InfName: "oem1.inf");
        var exactEntry = ComposeDriverEntry(exactInf, one);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable, exactEntry.ComparisonStatus);

        var shared = Device(@"PCI\VEN_1234&DEV_8765", [@"PCI\VEN_1234&DEV_8765"],
            "SYSTEM", "Other Test Device", "1.2.3.4", 0x1234, 0x8765);
        var heuristicEntry = ComposeDriverEntry(exactInf, one, shared);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, heuristicEntry.InstalledVersionConfidence);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, heuristicEntry.ComparisonStatus);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3.4")]
    [InlineData("1.0.0.bad", "2.0.0.0")]
    [InlineData("4294967296.0.0.0", "4294967297.0.0.0")]
    [InlineData("1.2.3.4", "2.0.0.0 label")]
    public void MSI_driver_version_comparison_fails_closed_for_malformed_or_incomparable_values(
        string installed, string offered)
    {
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown,
            ComposeMsiDriverComparison(installed, offered));
    }

    [Fact]
    public void MSI_unmatched_ambiguous_and_heuristic_driver_versions_remain_unknown()
    {
        var offered = new MsiLiveUpdateCatalogEntry("OEM Device Driver", "2.0.0.0", null, null,
            DeviceId: @"PCI\VEN_1234&DEV_5678", DriverVersion: "2.0.0.0");
        var missing = ComposeDriverEntry(offered);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, missing.ComparisonStatus);

        var firstId = @"PCI\VEN_1234&DEV_5678&SUBSYS_00010001";
        var secondId = @"PCI\VEN_1234&DEV_5678&SUBSYS_00020002";
        var first = Device(firstId, [firstId, @"PCI\VEN_1234&DEV_5678"], "SYSTEM", "First", "1.0.0.0", 0x1234, 0x5678);
        var second = Device(secondId, [secondId, @"PCI\VEN_1234&DEV_5678"], "SYSTEM", "Second", "1.1.0.0", 0x1234, 0x5678);
        var ambiguous = ComposeDriverEntry(offered, first, second);
        Assert.Null(ambiguous.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, ambiguous.ComparisonStatus);

        var family = new MsiLiveUpdateCatalogEntry("Intel Graphics Driver", "2.0.0.0", null, null,
            DriverVersion: "2.0.0.0");
        var graphics = Device(@"PCI\VEN_8086&DEV_1234", [@"PCI\VEN_8086&DEV_1234"],
            "DISPLAY", "Intel Arc Graphics", "1.0.0.0", 0x8086, 0x1234);
        var heuristic = ComposeDriverEntry(family, graphics);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, heuristic.InstalledVersionConfidence);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, heuristic.ComparisonStatus);
    }

    [Theory]
    [InlineData("E1T52IMS.110", "E1T52IMS.10F", FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable)]
    [InlineData("E1T52IMS.110", "E1T52IMS.110", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("E1T52IMS.10F", "E1T52IMS.110", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    public void BIOS_versions_compare_same_board_hexadecimal_revisions(
        string offered, string installed, FrontendMsiLiveUpdateComparisonStatus expected)
    {
        Assert.Equal(expected, ComposeBiosComparison(offered, installed));
    }

    [Theory]
    [InlineData("E1T52IMS.110", "E1T54IMS.10F")]
    [InlineData("E1T52IMS.110", "E1T52IMS.10FT")]
    [InlineData("E1T52IMS.110T", "E1T52IMS.10F")]
    [InlineData("E1T52IMS.1G0", "E1T52IMS.10F")]
    public void BIOS_comparison_is_unknown_for_other_boards_test_builds_and_malformed_values(
        string offered, string installed)
    {
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, ComposeBiosComparison(offered, installed));
    }

    [Fact]
    public void BIOS_comparison_is_unknown_when_local_board_revisions_conflict()
    {
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown,
            ComposeBiosComparison("E1T52IMS.120", "E1T52IMS.10F", "E1T52IMS.110"));
    }

    [Theory]
    [InlineData("1.2.3.4", "1.2.3.5", FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable)]
    [InlineData("1.2.3.5", "1.2.3.5", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("1.2.3.6", "1.2.3.5", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    public void MSI_Center_M_compares_exact_registry_DisplayVersion_with_equal_catalog_versions(
        string installed, string offered, FrontendMsiLiveUpdateComparisonStatus expected)
    {
        const string productCode = "00112233-4455-6677-8899-aabbccddeeff";
        var utility = new MsiLiveUpdateCatalogEntry("MSI Center M", offered, null, null,
            Guid: productCode,
            PackageVersion: offered);
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success, [utility]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", productCode, null, "MSI Center M", installed, "MSI")], null, []);

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: "msi.claw.cg3em")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Equal(FrontendMsiLiveUpdateVersionKind.Package, entry.LatestVersionKind);
        Assert.Equal(expected, entry.ComparisonStatus);
    }

    [Theory]
    [InlineData("3.0.2608.3101", "3.0.2608.3102")]
    [InlineData("not-a-version", "not-a-version")]
    public void MSI_Center_M_comparison_fails_closed_when_catalog_versions_disagree_or_are_malformed(
        string version, string packageVersion)
    {
        const string productCode = "00112233-4455-6677-8899-aabbccddeeff";
        var registryEntry = new MsiLiveUpdateCatalogEntry("MSI Center M", version, null, null,
            Guid: productCode, PackageVersion: packageVersion);
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success, [registryEntry]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", productCode, null, "MSI Center M", "3.0.2501.0", "MSI")], null, []);

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: "msi.claw.cg3em")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Theory]
    [InlineData("msi.claw.cg3em")]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    public void MSI_Center_M_uses_unique_known_name_and_verified_publisher_fallback_on_supported_models(string model)
    {
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success,
            [new("MSI Center M", "3.0.2608.3101", null, null, PackageVersion: "3.0.2608.3101")]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", null, null, " MSI Center M ", "3.0.2501.0", " MSI ")], null, []);
        var rowDiagnostics = new List<MsiUpdateCatalogRowDiagnostic>();

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory,
            onRowDiagnostic: rowDiagnostics.Add, hardwareModelId: model)
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Equal("3.0.2501.0", entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable, entry.ComparisonStatus);
        Assert.Equal("MatchedCenterMKnownNamePublisher", Assert.Single(rowDiagnostics).MatchOutcome);
    }

    [Theory]
    [InlineData("MSI Center M 3.0.2608.3101", "MSI Corporation")]
    [InlineData("MSI Center M v3.0.2608.3101", "MSI")]
    public void MSI_Center_M_accepts_bounded_versioned_registry_name_and_verified_publisher(
        string displayName, string publisher)
    {
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success,
            [new("MSI Center M", "3.0.2608.3101", null, null, PackageVersion: "3.0.2608.3101")]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", null, null, displayName, "3.0.2501.0", publisher)], null, []);
        var rowDiagnostics = new List<MsiUpdateCatalogRowDiagnostic>();

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory,
            onRowDiagnostic: rowDiagnostics.Add, hardwareModelId: "msi.claw.cg3em")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Equal("3.0.2501.0", entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable, entry.ComparisonStatus);
        Assert.Equal("MatchedCenterMKnownNamePublisher", Assert.Single(rowDiagnostics).MatchOutcome);
    }

    [Theory]
    [InlineData("3.0.2501.0", FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable)]
    [InlineData("3.0.2608.3101", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("3.0.2700.0", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    public void MSI_Center_M_SDK_registry_alias_matches_the_observed_unique_HKLM32_entry(
        string installedVersion, FrontendMsiLiveUpdateComparisonStatus expected)
    {
        const string catalogGuid = "0da6126a-8430-4367-b282-aa285afc2f10";
        var catalogEntry = new MsiLiveUpdateCatalogEntry("MSI Center M", "3.0.2608.3101", null, null,
            Guid: catalogGuid, PackageVersion: "3.0.2608.3101");
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success, [catalogEntry]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", null, null, " MSI Center M SDK ", installedVersion, "MSI", "HKLM32")], null, []);
        var diagnostics = new List<MsiUpdateCatalogRowDiagnostic>();

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory,
            onRowDiagnostic: diagnostics.Add, hardwareModelId: "msi.claw.cg3em")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Equal(installedVersion, entry.InstalledVersion);
        Assert.Equal(expected, entry.ComparisonStatus);
        Assert.Equal("MatchedCenterMKnownNamePublisher", Assert.Single(diagnostics).MatchOutcome);
    }

    [Theory]
    [InlineData("MSI Center M SDK Updater", "MSI")]
    [InlineData("Other MSI Center M SDK", "MSI")]
    [InlineData("MSI Center M SDK", "Micro-Star International")]
    public void MSI_Center_M_SDK_registry_alias_rejects_unverified_lookalikes(
        string displayName, string publisher)
    {
        var catalogEntry = new MsiLiveUpdateCatalogEntry("MSI Center M", "3.0.2608.3101", null, null,
            Guid: "0da6126a-8430-4367-b282-aa285afc2f10", PackageVersion: "3.0.2608.3101");
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success, [catalogEntry]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", null, null, displayName, "3.0.2608.3101", publisher, "HKLM32")], null, []);

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: "msi.claw.cg3em")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Null(entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Fact]
    public void MSI_Center_M_SDK_registry_alias_requires_one_non_conflicting_candidate()
    {
        var catalogEntry = new MsiLiveUpdateCatalogEntry("MSI Center M", "3.0.2608.3101", null, null,
            Guid: "0da6126a-8430-4367-b282-aa285afc2f10", PackageVersion: "3.0.2608.3101");
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success, [catalogEntry]));
        var inventory = new MsiUpdateInstalledInventory([], [],
        [
            new("uninstall-1", null, null, "MSI Center M SDK", "3.0.2608.3101", "MSI", "HKLM32"),
            new("uninstall-2", null, null, "MSI Center M SDK", "3.0.2608.3101", "MSI Corporation", "HKLM32")
        ], null, []);

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: "msi.claw.cg3em")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Null(entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Fact]
    public void MSI_Center_M_SDK_registry_alias_rejects_a_conflicting_explicit_product_code()
    {
        var catalogEntry = new MsiLiveUpdateCatalogEntry("MSI Center M", "3.0.2608.3101", null, null,
            Guid: "0da6126a-8430-4367-b282-aa285afc2f10", PackageVersion: "3.0.2608.3101");
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success, [catalogEntry]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", "20112233-4455-6677-8899-aabbccddeeff", null,
                "MSI Center M SDK", "3.0.2608.3101", "MSI", "HKLM32")], null, []);

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: "msi.claw.cg3em")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Null(entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Fact]
    public void MSI_Center_M_name_fallback_compares_a_single_numeric_catalog_version_field()
    {
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success,
            [new("MSI Center M", "3.0.2608.3101", null, null)]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", null, null, "MSI Center M", "3.0.2501.0", "MSI")], null, []);

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: "msi.claw.a2vm.8")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable, entry.ComparisonStatus);
    }

    [Fact]
    public void MSI_Center_M_name_fallback_requires_one_registry_candidate()
    {
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success,
            [new("MSI Center M", "3.0.2608.3101", null, null)]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [
                new("uninstall-1", null, null, "MSI Center M", "3.0.2501.0", "MSI"),
                new("uninstall-2", null, null, "MSI Center M 3.0.2608.3101", "3.0.2502.0", "MSI Corporation")
            ], null, []);

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: "msi.claw.a2vm.7")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);
        Assert.Null(entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Theory]
    [InlineData("msi.claw.cg3em", "MSI Center M", "Other Publisher", null, null)]
    [InlineData("msi.claw.cg3em", "MSI Center M Helper", "MSI", null, null)]
    [InlineData("msi.claw.cg3em", "MSI Center M 3.0.2608.3101 Preview", "MSI Corporation", null, null)]
    [InlineData("msi.claw.cg3em", "MSI Center M 3.0.2608.3101", "Micro-Star International", null, null)]
    [InlineData("msi.claw.cg3em", "MSI Center M", "MSI", "20112233-4455-6677-8899-aabbccddeeff", null)]
    [InlineData("msi.claw.other", "MSI Center M", "MSI", null, null)]
    public void MSI_Center_M_name_fallback_rejects_unverified_or_conflicting_candidates(
        string model, string displayName, string publisher, string? productCode, string? upgradeCode)
    {
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success,
            [new("MSI Center M", "3.0.2608.3101", null, null)]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", productCode, upgradeCode, displayName, "3.0.2501.0", publisher)], null, []);

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: model)
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);
        Assert.Null(entry.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Theory]
    [InlineData("3.0.2501.0", "3.0.2608.3101", FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable)]
    [InlineData("3.0.2608.3101", "3.0.2608.3101", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    [InlineData("3.0.2700.0", "3.0.2608.3101", FrontendMsiLiveUpdateComparisonStatus.UpToDate)]
    public void Utility_registry_compares_same_catalog_and_installed_DisplayVersion_for_exact_product_identity(
        string installed, string offered, FrontendMsiLiveUpdateComparisonStatus expected)
    {
        const string productCode = "00112233-4455-6677-8899-aabbccddeeff";
        var catalogEntry = new MsiLiveUpdateCatalogEntry("MSI Center M", offered, null, null,
            Guid: productCode, PackageVersion: offered);
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success, [catalogEntry]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", productCode, null, "MSI Center M", installed, "MSI")], null, []);

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: "msi.claw.cg3em")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Equal(FrontendMsiLiveUpdateVersionKind.Package, entry.LatestVersionKind);
        Assert.Equal(expected, entry.ComparisonStatus);
    }

    [Fact]
    public void Utility_registry_comparison_requires_one_exact_product_or_upgrade_code_match()
    {
        const string upgradeCode = "10112233-4455-6677-8899-aabbccddeeff";
        var catalogEntry = new MsiLiveUpdateCatalogEntry("MSI Center M", "3.0.2608.3101", null, null,
            UpgradeGuid: upgradeCode);
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success, [catalogEntry]));
        var matching = new MsiUpdateInstalledSoftware("key-1", "00112233-4455-6677-8899-aabbccddeeff",
            upgradeCode, "MSI Center M", "3.0.2501.0", "MSI");
        var secondMatching = matching with
        {
            RegistryKeyName = "key-2",
            ProductCode = "20112233-4455-6677-8899-aabbccddeeff"
        };

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), new([], [], [matching, secondMatching], null, []))
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.Unknown, entry.ComparisonStatus);
    }

    [Fact]
    public void Utility_registry_can_compare_an_exact_unique_UpgradeCode_with_matching_DisplayVersion_domain()
    {
        const string upgradeCode = "10112233-4455-6677-8899-aabbccddeeff";
        var catalogEntry = new MsiLiveUpdateCatalogEntry("MSI Center M", "3.0.2608.3101", null, null,
            UpgradeGuid: upgradeCode);
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Utility,
            MsiLiveUpdateCatalogCategoryState.Success, [catalogEntry]));
        var inventory = new MsiUpdateInstalledInventory([], [],
            [new("uninstall-key", "00112233-4455-6677-8899-aabbccddeeff", upgradeCode,
                "MSI Center M", "3.0.2501.0", "MSI")], null, []);

        var entry = Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory)
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable, entry.ComparisonStatus);
    }

    [Fact]
    public void Independent_valid_rows_keep_verdicts_when_another_source_is_partial()
    {
        var gpuId = @"PCI\VEN_8086&DEV_1234";
        var gpu = Device(gpuId, [gpuId], "DISPLAY", "Intel Arc Graphics", "1.0.0.0", 0x8086, 0x1234);
        var msiDriverId = @"PCI\VEN_1234&DEV_5678";
        var msiDevice = Device(msiDriverId, [msiDriverId], "SYSTEM", "OEM Device", "1.0.0.0", 0x1234, 0x5678);
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("OEM Device Driver", "2.0.0.0", null, null, DeviceId: msiDriverId, DriverVersion: "2.0.0.0")]))
            with { State = MsiLiveUpdateCatalogState.PartialFailure };
        var intel = new IntelDsaCatalogResult(true,
            [IntelPackage("gpu", "Intel Arc", false, IntelComponent("Graphics", "2.0.0.0", "VEN_8086&DEV_1234"))]);

        var snapshot = MsiUpdateCatalogComposer.Compose(msi, intel, Inventory(gpu, msiDevice),
            hardwareModelId: "msi.claw.a2vm.8");
        var entries = snapshot.Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries;

        Assert.Equal(FrontendMsiLiveUpdateCatalogState.PartialFailure, snapshot.State);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable,
            Assert.Single(entries, item => item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.Msi).ComparisonStatus);
        Assert.Equal(FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable,
            Assert.Single(entries, item => item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa).ComparisonStatus);
    }

    private static FrontendMsiLiveUpdateComparisonStatus ComposeMsiDriverComparison(string installed, string offered)
    {
        var deviceId = @"PCI\VEN_1234&DEV_5678&SUBSYS_00010001";
        var device = Device(deviceId, [deviceId], "SYSTEM", "OEM Device", installed, 0x1234, 0x5678);
        var entry = new MsiLiveUpdateCatalogEntry("OEM Device Driver", offered, null, null,
            DeviceId: deviceId, DriverVersion: offered);
        return ComposeDriverEntry(entry, device).ComparisonStatus;
    }

    private static FrontendMsiLiveUpdateCatalogEntry ComposeDriverEntry(
        MsiLiveUpdateCatalogEntry entry, params MsiUpdateInstalledPnpDevice[] devices)
    {
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success, [entry]));
        return Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), Inventory(devices), hardwareModelId: "msi.claw.a2vm.8")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);
    }

    private static FrontendMsiLiveUpdateCatalogEntry ComposeCg3emDriverEntry(
        MsiLiveUpdateCatalogEntry entry, params MsiUpdateInstalledPnpDevice[] devices)
    {
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success, [entry]));
        return Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
                IntelDsaCatalogResult.Failed("Http403"), Inventory(devices), hardwareModelId: "msi.claw.cg3em")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);
    }

    private static FrontendMsiLiveUpdateCatalogEntry ComposeCg3emDriverStoreEntry(
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateDriverStoreCapture driverStore,
        params MsiUpdateInstalledPnpDevice[] devices)
    {
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success, [entry]));
        var inventory = Inventory(devices) with { DriverStore = driverStore };
        return Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
                IntelDsaCatalogResult.Failed("Http403"), inventory, hardwareModelId: "msi.claw.cg3em")
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);
    }

    private static string ComposeCg3emDriverStoreOutcome(
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateDriverStoreCapture driverStore)
    {
        var diagnostics = new List<MsiUpdateCatalogMatchDiagnostic>();
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver,
            MsiLiveUpdateCatalogCategoryState.Success, [entry]));
        var inventory = Inventory() with { DriverStore = driverStore };
        MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"), inventory,
            onDiagnostic: diagnostics.Add, hardwareModelId: "msi.claw.cg3em");
        return Assert.Single(diagnostics).Outcome;
    }

    private static Cg3emDriverStoreCase GetCg3emDriverStoreCase(string catalogName) => catalogName switch
    {
        "Windows Studio Effects" => new(catalogName,
            "dism_mep,audio,component.inf_AudioProcessingObject_Microsoft", "mep_audio_component.inf",
            "Microsoft Corporation", "AudioProcessingObject", "3.0.8.0"),
        "Realtek OED Driver" => new(catalogName,
            "dism_intcoed,oemlibpath.inf_Extension_Realtek", "IntcOED_OemLibPath.inf",
            "Realtek Semiconductor Corp.", "Extension", "20.43.274.9"),
        "Realtek Audio Driver" => new(catalogName,
            "dism_rtksdcaxumsi.inf_Extension_Realtek", "RtkSdcaXuMSI.inf",
            "Realtek", "Extension", "10.0.378.1"),
        "Integrated Sensor Solution" => new(catalogName,
            "dism_ishheci.inf_System_Intel", "ishheci.inf", "Intel", "System", "5.8.61.0"),
        _ => throw new ArgumentOutOfRangeException(nameof(catalogName), catalogName, null)
    };

    private sealed record Cg3emDriverStoreCase(
        string CatalogName,
        string OemPackageIdentity,
        string OriginalInfName,
        string Provider,
        string DeviceClass,
        string PackageVersion);

    private static FrontendMsiLiveUpdateComparisonStatus ComposeBiosComparison(
        string offered, params string[] installed)
    {
        var msi = MsiResultWithCategory(new(MsiLiveUpdateCatalogCategory.Bios,
            MsiLiveUpdateCatalogCategoryState.Success, [new("BIOS", offered, null, null)]));
        var inventory = new MsiUpdateInstalledInventory(installed, [], [], null, []);
        return Assert.Single(MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory)
            .Categories.Single(item => item.Category == FrontendMsiLiveUpdateCatalogCategory.Bios).Entries).ComparisonStatus;
    }

    private static MsiLiveUpdateCatalogQueryResult MsiResultWithCategory(MsiLiveUpdateCatalogCategoryResult category) =>
        new(MsiLiveUpdateCatalogState.Completed, "Claw 8 AI+ A2VM", "kr", "win11",
        [
            category.Category == MsiLiveUpdateCatalogCategory.Bios
                ? category
                : new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Empty, []),
            category.Category == MsiLiveUpdateCatalogCategory.Firmware
                ? category
                : new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Empty, []),
            category.Category == MsiLiveUpdateCatalogCategory.Driver
                ? category
                : new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Empty, []),
            category.Category == MsiLiveUpdateCatalogCategory.Utility
                ? category
                : new(MsiLiveUpdateCatalogCategory.Utility, MsiLiveUpdateCatalogCategoryState.Empty, [])
        ], DateTimeOffset.UtcNow);

    [Fact]
    public void Exact_installed_revision_and_GUID_helpers_do_not_invent_values()
    {
        Assert.Equal("0x0230", MsiUpdateInstalledInventory.ParseRevision(@"USB\VID_0DB0&PID_1902&REV_0230"));
        Assert.Null(MsiUpdateInstalledInventory.ParseRevision(@"USB\VID_0DB0&PID_1902&REV_23"));
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", MsiUpdateInstalledInventory.ParseGuid("{00112233-4455-6677-8899-AABBCCDDEEFF}"));
        Assert.Null(MsiUpdateInstalledInventory.ParseGuid("not-a-guid"));
    }

    private static MsiUpdateInstalledInventory Inventory(params MsiUpdateInstalledPnpDevice[] devices) =>
        new([], devices, [], null, []);

    private static MsiUpdateInstalledPnpDevice DriverDevice(
        string instanceId, string hardwareId, string deviceClass, string provider, string driverVersion) =>
        new(instanceId, [hardwareId], [], deviceClass, $"{provider} {deviceClass}", deviceClass,
            $"{provider} {deviceClass}", provider, driverVersion, "oem1.inf", null, null);

    private static MsiUpdateInstalledPnpDevice Device(string instanceId, string[] hardwareIds, string deviceClass,
        string name, string driverVersion, ushort vendorId, ushort productId) =>
        new(instanceId, hardwareIds, [], deviceClass, name, deviceClass, name, "Intel", driverVersion, "oem1.inf", vendorId, productId);

    private static IntelDsaPackage IntelPackage(string id, string name, bool isBeta, IntelDsaComponent component,
        string? version = null, IReadOnlyList<string>? operatingSystems = null) =>
        new(id, name, version ?? component.Version, "2026-10-18", null, isBeta, operatingSystems ?? [], [component]);

    private static IntelDsaComponent IntelComponent(string category, string? version, params string[] detectionValues) =>
        new(category, version, detectionValues, []);

    private static MsiLiveUpdateCatalogQueryResult MsiResult(MsiLiveUpdateCatalogCategoryResult driver) =>
        new(MsiLiveUpdateCatalogState.Completed, "Claw 8 AI+ A2VM", "kr", "win11",
        [
            new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Empty, []),
            new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Empty, []),
            driver,
            new(MsiLiveUpdateCatalogCategory.Utility, MsiLiveUpdateCatalogCategoryState.Empty, [])
        ], DateTimeOffset.UtcNow);

    private static object Package(string id, string name, string version, bool isBeta, object component,
        IReadOnlyList<string>? operatingSystems = null, object? operatingSystemOverrides = null) => new
    {
        Id = int.TryParse(id, out var numericId) ? (object)numericId : id,
        Name = name,
        Version = version,
        DisplayReleaseDate = "2026-10-18",
        IsBeta = isBeta,
        Url = "https://download.example.test/driver.exe",
        Components = new[] { component },
        SupportedOperatingSystems = operatingSystems,
        OperatingSystemOverrides = operatingSystemOverrides
    };

    private static object Component(string category, string? version, string[] detectionValues, string[] operatingSystems,
        object? operatingSystemOverrides = null) => new
    {
        Category = category,
        Version = version,
        DetectionValues = detectionValues,
        SupportedOperatingSystems = operatingSystems,
        OperatingSystemOverrides = operatingSystemOverrides
    };

    private static byte[] CreateArchive(IEnumerable<object> packages, string? meta = null,
        bool includeCatalog = true, bool includeUntrustedBinary = false)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeCatalog)
                WriteEntry(archive, "software-configurations.json", JsonSerializer.Serialize(packages));
            if (meta is not null) WriteEntry(archive, "meta.json", meta);
            if (includeUntrustedBinary) WriteEntry(archive, "DSARules.dll", "this must remain inert");
        }
        return stream.ToArray();
    }

    private static byte[] CreateArchiveBytes(string json) => ZipWithCatalog(json);

    private static byte[] ZipWithCatalog(string json)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(archive, "software-configurations.json", json);
        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

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

}
