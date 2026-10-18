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
        Assert.Contains(entries, item => item.Name == "Intel IPF Driver" && item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.Msi);

        var fallback = MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"), inventory)
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries;
        Assert.Contains(fallback, item => item.Name == "Intel Arc Display Driver"
            && item.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.Msi);
        Assert.Equal(FrontendMsiLiveUpdateSourceState.Unavailable, MsiUpdateCatalogComposer.Compose(msi,
            IntelDsaCatalogResult.Failed("Http403"), inventory).IntelDsaSource!.State);
    }

    [Fact]
    public void Missing_local_inventory_is_partial_and_never_fabricates_an_installed_version()
    {
        var msi = MsiResult(new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [new("Intel Display Driver", "32.0.101.9150", null, null, DeviceId: GpuDeviceId)]));
        var combined = MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"),
            new([], [], [], [], null, ["WmiDriverReadFailed", "AppxReadFailed"]));
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

        var driver = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, intel, Inventory(device)).Categories,
            category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver);

        var entry = Assert.Single(driver.Entries);
        Assert.Equal(FrontendMsiLiveUpdateVersionSource.IntelDsa, entry.LatestVersionSource);
        Assert.Equal("32.0.101.9150", entry.Version);
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
            [new("uninstall-key", productCode, null, "MSI Center M", "3.0.2501.0", "MSI")], [],
            new("0x0230", FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic), []);

        var snapshot = MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"), inventory);
        var bios = snapshot.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Bios).Entries;
        var firmware = Assert.Single(snapshot.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Firmware).Entries);
        var driver = Assert.Single(snapshot.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Driver).Entries);
        var utility = Assert.Single(snapshot.Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Equal("E1T52IMS.113", bios[0].InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Exact, bios[0].InstalledVersionConfidence);
        Assert.Null(bios[1].InstalledVersion);
        Assert.Equal("0x0230", firmware.InstalledVersion);
        Assert.Equal(FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, firmware.InstalledVersionConfidence);
        Assert.Equal("32.0.100.1", driver.InstalledVersion);
        Assert.Equal("3.0.2501.0", utility.InstalledVersion);
    }

    [Fact]
    public void UWP_sentinel_is_not_displayed_as_a_version_and_AppX_requires_exact_package_identity()
    {
        var msi = new MsiLiveUpdateCatalogQueryResult(MsiLiveUpdateCatalogState.Completed,
            "Claw 8 AI+ A2VM", "kr", "win11",
            [
                new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Utility, MsiLiveUpdateCatalogCategoryState.Success,
                    [new("Intel Graphics Command Center", "UWP", null, null)])
            ], DateTimeOffset.UtcNow);
        var matchingInventory = new MsiUpdateInstalledInventory([], [], [],
            [new("Intel Graphics Command Center", "IntelGraphicsExperience_8wekyb3d8bbwe", "1.2.3.4")], null, []);
        var unmatchedInventory = new MsiUpdateInstalledInventory([], [], [],
            [new("IntelGraphicsExperience", "IntelGraphicsExperience_8wekyb3d8bbwe", "1.2.3.4")], null, []);

        var matching = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"), matchingInventory)
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);
        var unmatched = Assert.Single(MsiUpdateCatalogComposer.Compose(msi, IntelDsaCatalogResult.Failed("Http403"), unmatchedInventory)
            .Categories.Single(category => category.Category == FrontendMsiLiveUpdateCatalogCategory.Utility).Entries);

        Assert.Null(matching.Version);
        Assert.Equal("1.2.3.4", matching.InstalledVersion);
        Assert.Null(unmatched.InstalledVersion);
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
    }

    [Fact]
    public void Exact_installed_revision_and_GUID_helpers_do_not_invent_values()
    {
        Assert.Equal("0x0230", MsiUpdateInstalledInventory.ParseRevision(@"USB\VID_0DB0&PID_1902&REV_0230"));
        Assert.Null(MsiUpdateInstalledInventory.ParseRevision(@"USB\VID_0DB0&PID_1902&REV_23"));
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", MsiUpdateInstalledInventory.ParseGuid("{00112233-4455-6677-8899-AABBCCDDEEFF}"));
        Assert.Null(MsiUpdateInstalledInventory.ParseGuid("not-a-guid"));
    }

    private static MsiUpdateInstalledInventory Inventory(params MsiUpdateInstalledPnpDevice[] devices) =>
        new([], devices, [], [], null, []);

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
