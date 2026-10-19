using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.MSI.LiveUpdate;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiUpdateDiagnosticSnapshotTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 19, 12, 30, 0, TimeSpan.Zero);
    private const string ProductCode = "00112233-4455-6677-8899-aabbccddeeff";

    [Fact]
    public void Parser_classifies_raw_OEM_markers_without_changing_rows_or_persisting_URLs_and_commands()
    {
        var privateUrl = "https://catalog.example.test/users/onehoon/setup.exe?token=private-query-token";
        var privateCommand = @"C:\Users\onehoon\setup.exe /quiet /token=private-command-token";
        var deviceId = @"PCI\VEN_10EC&DEV_0256\INSTANCE_SERIAL_PRIVATE-DEVICE";
        var utilityJson = JsonSerializer.Serialize(new object[]
        {
            new
            {
                name = "MSI Center M", version = "UWP", releaseDate = "2026-10-18", description = privateUrl,
                deviceID = deviceId, GUID = "UWP", Upgrade_Guid = $"{{{ProductCode}}}", Crc = "UWP",
                Type = "Utility", Other = privateUrl, File = privateUrl, Siparam = privateCommand
            },
            new { name = "Malformed Optional", version = "1.2.3", Type = new[] { "ignored" }, Other = new { secret = "private" } },
            new { name = "Missing OEM Identity", version = "1.0", deviceID = "NOT_FOUND", GUID = "NOT_FOUND", upgradeGuid = "UWP", Crc = "NOT_FOUND" },
            new { name = "Partial OEM Identity", version = "1.0", deviceID = "VEN_8086&DEV_1234" },
            new { name = "Opaque OEM Selectors", version = "1.0", Other = "onehoon-private-selector", Crc = "private-crc-payload" }
        });
        var request = new MsiLiveUpdateCatalogRequest("Claw 8 AI+ A2VM", "kr", "win11");
        var msi = MsiLiveUpdateCatalogResultParser.ParseHelperOutput(HelperOutput(
            ("bios", "[]"), ("firmware", "[]"), ("driver", "[]"), ("utility", utilityJson)), request, CapturedAt);

        Assert.Equal(MsiLiveUpdateCatalogState.Completed, msi.State);
        var rows = msi.Categories.Single(category => category.Category == MsiLiveUpdateCatalogCategory.Utility).Entries;
        Assert.Equal(5, rows.Count);
        var first = rows[0];
        var fields = first.DiagnosticFields!;
        Assert.Null(first.UpgradeGuid); // Existing matcher input remains unchanged for the raw Upgrade_Guid alias.
        Assert.Equal("Valid", fields.DeviceIdKind);
        Assert.Equal(new[] { @"PCI\VEN_10EC&DEV_0256" }, fields.HardwareTypes);
        Assert.Equal("UwpSentinel", fields.GuidKind);
        Assert.Equal("Valid", fields.UpgradeGuidKind);
        Assert.Equal(ProductCode, fields.SafeUpgradeGuid);
        Assert.Equal("UwpSentinel", fields.CrcKind);
        Assert.Equal("Utility", fields.Type);
        Assert.Equal("Url", fields.FileKind);
        Assert.Equal(".exe", fields.FileExtension);
        Assert.Equal("RedactedCommand", fields.SiparamKind);
        var fields1 = rows[1].DiagnosticFields!;
        Assert.Equal("Array", fields1.TypeKind);
        Assert.Equal("Object", fields1.OtherKind);
        Assert.Null(fields1.Type);
        var fields2 = rows[2].DiagnosticFields!;
        Assert.Equal("NotFoundSentinel", fields2.DeviceIdKind);
        Assert.Equal("NotFoundSentinel", fields2.GuidKind);
        Assert.Equal("UwpSentinel", fields2.UpgradeGuidKind);
        Assert.Equal("NotFoundSentinel", fields2.CrcKind);
        Assert.Equal("Partial", rows[3].DiagnosticFields!.DeviceIdKind);
        var fields4 = rows[4].DiagnosticFields!;
        Assert.Equal("RedactedValue", fields4.OtherKind);
        Assert.Null(fields4.Other);
        Assert.Equal("RedactedValue", fields4.CrcKind);
        Assert.Null(fields4.Crc);

        var input = CreateInput(msi, IntelDsaCatalogResult.Failed("Http403"), new([], [],
            [new(@"C:\Users\onehoon\AppData\Local\PrivateRegistryKey", ProductCode, null,
                "MSI Center M", "3.0.1", "MSI Corporation")],
            [new("MSI_Center_M", "MSI_Center_M_8wekyb3d8bbwe", "3.0.1"),
                new("onehoon.MSI_Center_M", "onehoon.MSI_Center_M_8wekyb3d8bbwe", "3.0.2")], null, []));
        var document = MsiUpdateDiagnosticSnapshotBuilder.Build(input);
        var json = Encoding.UTF8.GetString(MsiUpdateDiagnosticSnapshotBuilder.SerializeWithinLimit(document));
        var userNamedPackage = Assert.Single(document.Inventory.AppxPackages, row => row.Version == "3.0.2");

        Assert.Contains("UwpSentinel", json, StringComparison.Ordinal);
        Assert.Contains("RedactedCommand", json, StringComparison.Ordinal);
        Assert.Contains("MSI Center M", json, StringComparison.Ordinal);
        Assert.Null(userNamedPackage.Name);
        Assert.Equal("RedactedUnrecognizedToken", userNamedPackage.NameDisposition);
        Assert.Null(userNamedPackage.FamilyName);
        Assert.Equal("RedactedUnrecognizedToken", userNamedPackage.FamilyNameDisposition);
        Assert.DoesNotContain("private-query-token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-command-token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onehoon", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SERIAL_PRIVATE-DEVICE", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.test", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateRegistryKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onehoon-private-selector", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-crc-payload", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("UwpSentinel", document.MsiRows.Single(row => row.Name == "MSI Center M").Latest.UnavailableReason);
    }

    [Fact]
    public void Projection_preserves_duplicate_source_ordinals_dispositions_and_independent_match_reasons()
    {
        const string gpuId = @"PCI\VEN_8086&DEV_1234";
        var localGpu = Pnp(gpuId + @"\LOCAL_SERIAL_PRIVATE", gpuId, "DISPLAY", "USER_ASSIGNED_PRIVATE_NAME", "32.0.100.1");
        var msi = MsiResult(
            new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Empty, []),
            new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Empty, []),
            new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success,
            [
                new("Intel Graphics Driver", "UWP", null, null, DeviceId: gpuId),
                new("Intel Graphics Driver", "2.0.0.0", null, null, DeviceId: @"PCI\VEN_8086&DEV_9999"),
                new("Intel Graphics Driver", "2.0.0.0", null, null),
                new("Exact GPU Component", "2.0.0.0", null, null, DeviceId: gpuId),
                new("Unidentified Driver", "2.0.0.0", null, null),
                new("Missing Device Driver", "2.0.0.0", null, null, DeviceId: @"PCI\VEN_8086&DEV_9999")
            ]),
            new(MsiLiveUpdateCatalogCategory.Utility, MsiLiveUpdateCatalogCategoryState.Success,
            [
                new("Intel Graphics Software", "1.0", null, null),
                new("MSI Center M", "1.0", null, null),
                new("MSI Center M", "1.1", null, null)
            ]));
        var intel = new IntelDsaCatalogResult(true,
            [new IntelDsaPackage("930514", "Intel Arc Graphics", "32.0.101.9150", "2026-10-18",
                "https://download.example.test/driver.exe?token=private-dsa-token", false, [],
                [new IntelDsaComponent("Graphics", "32.0.101.9150", ["VEN_8086&DEV_1234"], [])])],
            CatalogCreatedUtc: CapturedAt);
        var inventory = new MsiUpdateInstalledInventory([], [localGpu], [], [], null, []);
        var input = ComposeInput(msi, intel, inventory);
        var rows = input.MsiRows;
        var driverRows = rows.Where(row => row.Category == MsiLiveUpdateCatalogCategory.Driver).ToArray();
        var utilityRows = rows.Where(row => row.Category == MsiLiveUpdateCatalogCategory.Utility).ToArray();

        Assert.Equal(6, driverRows.Length);
        Assert.Equal(Enumerable.Range(0, 6), driverRows.Select(row => row.SourceIndex));
        Assert.Equal("SuppressedByExactIdentity", driverRows[0].DuplicateOutcome);
        Assert.Equal("RetainedConflictingIdentity", driverRows[1].DuplicateOutcome);
        Assert.Equal("SuppressedByUniqueDsaFamily", driverRows[2].DuplicateOutcome);
        Assert.Equal("ExactHardwareId", driverRows[3].MatchOutcome);
        Assert.Equal("NoUsableIdentity", driverRows[4].MatchOutcome);
        Assert.Equal("NoMatchingPresentDevice", driverRows[5].MatchOutcome);
        Assert.Equal(new[] { 0, 1, 2 }, utilityRows.Select(row => row.SourceIndex));
        Assert.Equal("ExcludedByPolicy", utilityRows[0].UiDisposition);
        Assert.Equal(new[] { "MSI Center M", "MSI Center M" }, utilityRows.Skip(1).Select(row => row.MappedEntry!.Name));
        Assert.All(utilityRows.Skip(1), row => Assert.Equal("Unmatched", row.MatchOutcome));

        var document = MsiUpdateDiagnosticSnapshotBuilder.Build(input);
        Assert.Equal("SupersededByIntelDsa", document.MsiRows.Single(row => row.SourceCategory == "Driver" && row.SourceIndex == 0)
            .Latest.UnavailableReason);
        Assert.Equal(1, document.Sources.IntelDsa.ResolvedCount);
        Assert.Equal(1, document.Counts.AppendedIntelDsaRows);
        Assert.Contains(document.Inventory.LocalPnp, row => row.HardwareTypes.Contains(gpuId));
        var json = Encoding.UTF8.GetString(MsiUpdateDiagnosticSnapshotBuilder.SerializeWithinLimit(document));
        Assert.DoesNotContain("private-dsa-token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("download.example.test", json, StringComparison.OrdinalIgnoreCase);

        foreach (var model in new[] { "msi.claw.a2vm.7", "msi.claw.a2vm.8", "msi.claw.cg3em" })
        {
            var modelDocument = MsiUpdateDiagnosticSnapshotBuilder.Build(input with { HardwareModelId = model });
            Assert.Equal(model, modelDocument.HardwareModelId);
            Assert.Equal(1, modelDocument.SchemaVersion);
        }

        Assert.DoesNotContain("LOCAL_SERIAL_PRIVATE", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("USER_ASSIGNED_PRIVATE_NAME", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onehoon", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Store_writes_valid_JSON_retains_five_owned_reports_and_preserves_unrelated_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MsiUpdateDiagnosticSnapshotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var unrelated = Path.Combine(directory, "unrelated.json");
            var nonOwned = Path.Combine(directory, "msi-update-not-owned.json");
            var nestedDirectory = Path.Combine(directory, "nested");
            Directory.CreateDirectory(nestedDirectory);
            var nested = Path.Combine(nestedDirectory, "msi-update-20000101-000000.000-00000000.json");
            await File.WriteAllTextAsync(unrelated, "{}", CancellationToken.None);
            await File.WriteAllTextAsync(nonOwned, "{}", CancellationToken.None);
            await File.WriteAllTextAsync(nested, "{}", CancellationToken.None);
            var stale = Path.Combine(directory, "msi-update-20000101-000000.000-00000000.json");
            await File.WriteAllTextAsync(stale, "{}", CancellationToken.None);
            File.SetLastWriteTimeUtc(stale, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var store = new MsiUpdateDiagnosticSnapshotStore(directory);
            var input = CreateInput(MsiResult(
                new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Failed, [], "Http503"),
                new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Utility, MsiLiveUpdateCatalogCategoryState.Empty, []),
                MsiLiveUpdateCatalogState.PartialFailure, "Http503"), IntelDsaCatalogResult.Failed("Http403"));

            MsiUpdateDiagnosticSnapshotWriteResult? latest = null;
            for (var index = 0; index < 6; index++)
            {
                latest = await store.WriteAsync(input with { CapturedAtUtc = CapturedAt.AddSeconds(index) }, CancellationToken.None);
                Assert.True(latest.Succeeded, latest.ErrorKind);
            }

            var owned = Directory.GetFiles(directory, "msi-update-*.json")
                .Where(path => Path.GetFileName(path) is var name
                    && System.Text.RegularExpressions.Regex.IsMatch(name,
                        @"^msi-update-\d{8}-\d{6}\.\d{3}-[0-9a-f]{8}\.json$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                .ToArray();
            Assert.Equal(5, owned.Length);
            Assert.True(File.Exists(unrelated));
            Assert.True(File.Exists(nonOwned));
            Assert.True(File.Exists(nested));
            Assert.DoesNotContain(stale, owned);
            Assert.NotNull(latest!.FileName);
            var savedPath = Path.Combine(directory, latest.FileName!);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(savedPath));
            Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(json.RootElement.GetProperty("truncated").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.Equal("PartialFailure", json.RootElement.GetProperty("sources").GetProperty("msiState").GetString());
            Assert.Equal(4, json.RootElement.GetProperty("sources").GetProperty("categories").GetArrayLength());
            Assert.Equal("Failed", json.RootElement.GetProperty("sources").GetProperty("categories")[1].GetProperty("state").GetString());
            Assert.True(new FileInfo(savedPath).Length <= 2 * 1024 * 1024);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Projection_caps_rows_and_marks_the_snapshot_truncated()
    {
        var entries = Enumerable.Range(0, 2_200)
            .Select(index => new MsiLiveUpdateCatalogEntry($"Diagnostic Driver {index}", "1.2.3.4", null, null))
            .ToArray();
        var input = CreateInput(MsiResult(
            new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Empty, []),
            new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Empty, []),
            new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Success, entries),
            new(MsiLiveUpdateCatalogCategory.Utility, MsiLiveUpdateCatalogCategoryState.Empty, [])));

        var document = MsiUpdateDiagnosticSnapshotBuilder.Build(input);
        var bytes = MsiUpdateDiagnosticSnapshotBuilder.SerializeWithinLimit(document);

        Assert.True(document.Truncated);
        Assert.Equal(2_200, document.Counts.OriginalMsiItems);
        Assert.InRange(document.MsiRows.Count, 1, 2_000);
        Assert.Equal(document.MsiRows.Count,
            document.Sources.Categories.Single(category => category.Category == "Driver").CapturedCount);
        Assert.Equal(2_200 - document.MsiRows.Count, document.Truncation.MsiRowsOmitted);
        Assert.True(bytes.Length <= 2 * 1024 * 1024);
    }

    [Fact]
    public async Task Store_failure_is_best_effort_and_cancellation_publishes_no_report()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "MsiUpdateDiagnosticSnapshotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryRoot)!);
        try
        {
            await File.WriteAllTextAsync(temporaryRoot, "block directory creation", CancellationToken.None);
            var input = CreateInput(MsiResult(
                new(MsiLiveUpdateCatalogCategory.Bios, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Firmware, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Driver, MsiLiveUpdateCatalogCategoryState.Empty, []),
                new(MsiLiveUpdateCatalogCategory.Utility, MsiLiveUpdateCatalogCategoryState.Empty, [])),
                IntelDsaCatalogResult.Failed("Http403"));
            var failed = await new MsiUpdateDiagnosticSnapshotStore(temporaryRoot).WriteAsync(input, CancellationToken.None);

            Assert.False(failed.Succeeded);
            Assert.NotNull(failed.ErrorKind);
            Assert.True(File.Exists(temporaryRoot));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(temporaryRoot)!, "msi-update-*.json", SearchOption.TopDirectoryOnly));

            var canceledDirectory = temporaryRoot + "-canceled";
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new MsiUpdateDiagnosticSnapshotStore(canceledDirectory).WriteAsync(input, cancellation.Token));
            Assert.False(Directory.Exists(canceledDirectory));
        }
        finally
        {
            if (File.Exists(temporaryRoot)) File.Delete(temporaryRoot);
            var parent = Path.GetDirectoryName(temporaryRoot)!;
            if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
        }
    }

    [Fact]
    public void Manual_check_keeps_unsupported_hardware_outside_diagnostic_capture_and_writes_from_the_existing_inventory()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw", "Frontend", "InProcessAddonFrontendControl.cs"));
        var methodStart = source.IndexOf("public async Task<FrontendMsiLiveUpdateCatalogSnapshot> RequestMsiLiveUpdateCatalogAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static string? SafeMsiCatalogDiagnosticName", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        var unsupportedCheck = method.IndexOf("if (!MsiLiveUpdateCatalogDevicePolicy.IsSupported", StringComparison.Ordinal);
        var compose = method.IndexOf("MsiUpdateCatalogComposer.Compose", StringComparison.Ordinal);
        var write = method.IndexOf("MsiUpdateDiagnosticSnapshotStore", StringComparison.Ordinal);
        Assert.True(unsupportedCheck >= 0 && compose > unsupportedCheck && write > compose);
        Assert.Equal(1, CountOccurrences(method, "CaptureMsiUpdateInventorySafelyAsync(cancellationToken)"));
        Assert.Contains("WriteAsync(diagnosticInput, cancellationToken)", method, StringComparison.Ordinal);
        Assert.Contains("return FrontendMsiLiveUpdateCatalogSnapshot.Unavailable(\"UnsupportedDevice\")", method, StringComparison.Ordinal);
    }

    private static MsiUpdateDiagnosticSnapshotInput ComposeInput(MsiLiveUpdateCatalogQueryResult msi,
        IntelDsaCatalogResult intel, MsiUpdateInstalledInventory inventory)
    {
        var rows = new List<MsiUpdateCatalogRowDiagnostic>();
        IReadOnlyList<IntelDsaResolvedDriver> matches = [];
        var composed = MsiUpdateCatalogComposer.Compose(msi, intel, inventory,
            onRowDiagnostic: rows.Add, onResolvedIntelMatches: result => matches = result);
        return new("msi.claw.cg3em", msi, intel, inventory, composed, rows, matches, CapturedAt, "0.1.403");
    }

    private static MsiUpdateDiagnosticSnapshotInput CreateInput(MsiLiveUpdateCatalogQueryResult msi,
        IntelDsaCatalogResult? intel = null, MsiUpdateInstalledInventory? inventory = null) =>
        ComposeInput(msi, intel ?? IntelDsaCatalogResult.Failed("Http403"),
            inventory ?? new([], [], [], [], null, []));

    private static MsiLiveUpdateCatalogQueryResult MsiResult(
        MsiLiveUpdateCatalogCategoryResult bios,
        MsiLiveUpdateCatalogCategoryResult firmware,
        MsiLiveUpdateCatalogCategoryResult driver,
        MsiLiveUpdateCatalogCategoryResult utility,
        MsiLiveUpdateCatalogState state = MsiLiveUpdateCatalogState.Completed,
        string? errorKind = null) => new(state, "Claw 8 AI+ A2VM", "kr", "win11",
            [bios, firmware, driver, utility], CapturedAt, errorKind);

    private static MsiUpdateInstalledPnpDevice Pnp(string deviceId, string hardwareId, string deviceClass,
        string friendlyName, string version) => new(deviceId, [hardwareId], [], deviceClass, friendlyName,
        deviceClass, friendlyName, "Intel Corporation", version, @"C:\Windows\INF\oem42.inf", 0x8086, 0x1234);

    private static string HelperOutput(params (string Category, string Body)[] responses) =>
        JsonSerializer.Serialize(new
        {
            status = "Completed",
            categories = responses.Select(response => new
            {
                category = response.Category,
                transportSucceeded = true,
                responseJson = response.Body,
                errorKind = (string?)null
            })
        });

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0; index += token.Length)
            count++;
        return count;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
