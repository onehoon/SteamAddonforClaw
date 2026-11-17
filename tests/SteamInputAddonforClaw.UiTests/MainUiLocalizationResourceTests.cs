using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SteamInputAddonforClaw.UI;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class MainUiLocalizationResourceTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Regex LocalizationCallPattern = new(
        @"MainUiStrings\.(?<method>Get|Format)\s*\(\s*(?<first>[^,]*)",
        RegexOptions.CultureInvariant);
    private static readonly Regex ResourceKeyLiteralPattern = new(
        "\"(?<value>[A-Z][A-Za-z0-9]+_[A-Za-z0-9_./{}-]+)\"",
        RegexOptions.CultureInvariant);
    private static readonly Regex PlaceholderPattern = new(
        @"(?<!\{)\{(?<index>\d+)(?:[,}:])",
        RegexOptions.CultureInvariant);
    private static readonly HashSet<string> LocalizableXamlProperties = new(StringComparer.Ordinal)
    {
        "Content", "Text", "Header", "Description", "Title", "Message",
        "PlaceholderText", "ToolTip", "Label", "OffContent", "OnContent"
    };
    private static readonly IReadOnlyDictionary<string, string[]> ResourceOnlyXamlUidSuffixes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // QuickOverlayPage sets this resource-only automation name in code-behind.
            ["QuickOverlay_YButtonQuickJumpComboBox"] = ["_A11y"]
        };
    private static readonly IReadOnlyDictionary<string, string> XamlPropertyResourceKeyOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // SettingsPage updates this description from code-behind to show authoritative theme-save failures.
            ["Settings_AppThemeCard.Description"] = "Settings_AppTheme_Description"
        };

    [Fact]
    public void Supported_resource_files_share_keys_and_keep_every_value_nonempty()
    {
        var resources = ReadAllResources();
        Assert.NotEmpty(resources);

        var expectedKeys = resources.First().Value.Keys
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        foreach (var values in resources.Values)
        {
            Assert.Equal(expectedKeys, values.Keys.OrderBy(key => key, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Xaml_uid_properties_and_main_ui_string_calls_resolve_in_each_language()
    {
        var root = FindRepositoryRoot();
        var resources = ReadAllResources();
        var uiRoot = Path.Combine(root, "src", "SteamInputAddonforClaw.UI");

        foreach (var file in Directory.EnumerateFiles(uiRoot, "*.xaml", SearchOption.AllDirectories)
                     .Where(IsSourceFile))
        {
            var document = XDocument.Load(file);
            AssertXamlLocalizationResourcesExist(document, resources);
        }

        var sourceFiles = Directory.EnumerateFiles(uiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourceFile);
        var referencedKeys = new HashSet<string>(StringComparer.Ordinal);
        var referencedPrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            foreach (Match call in LocalizationCallPattern.Matches(source))
            {
                foreach (Match literal in ResourceKeyLiteralPattern.Matches(call.Groups["first"].Value))
                {
                    var key = literal.Groups["value"].Value.Replace('/', '.');
                    if (key.Contains('{'))
                    {
                        referencedPrefixes.Add(key[..key.IndexOf('{')]);
                    }
                    else if (key.EndsWith('_'))
                    {
                        referencedPrefixes.Add(key);
                    }
                    else
                    {
                        referencedKeys.Add(key);
                    }
                }
            }
        }

        foreach (var key in referencedKeys)
            AssertResourceExists(resources, key);

        foreach (var prefix in referencedPrefixes)
        {
            foreach (var (language, values) in resources)
            {
                var matches = values.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                Assert.NotEmpty(matches);
                Assert.All(matches, entry =>
                    Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"{language}:{entry.Key}"));
            }
        }
    }

    [Fact]
    public void Xaml_localization_keys_missing_from_every_locale_are_rejected()
    {
        var resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en-US"] = new Dictionary<string, string>(StringComparer.Ordinal),
            ["ko-KR"] = new Dictionary<string, string>(StringComparer.Ordinal)
        };

        var inlineFallbackDocument = XDocument.Parse(
            """<TextBlock xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Uid="MissingKey" Text="Fallback" />""");
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => AssertXamlLocalizationResourcesExist(inlineFallbackDocument, resources));

        var resourceOnlyDocument = XDocument.Parse(
            """<ComboBox xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Uid="QuickOverlay_YButtonQuickJumpComboBox" />""");
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => AssertXamlLocalizationResourcesExist(resourceOnlyDocument, resources));
    }

    [Fact]
    public void Referenced_format_resources_have_compatible_placeholders_and_format_safely()
    {
        var root = FindRepositoryRoot();
        var resources = ReadAllResources();
        var formatKeys = new HashSet<string>(StringComparer.Ordinal);
        var uiRoot = Path.Combine(root, "src", "SteamInputAddonforClaw.UI");

        foreach (var sourceFile in Directory.EnumerateFiles(uiRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(IsSourceFile))
        {
            var source = File.ReadAllText(sourceFile);
            foreach (Match call in LocalizationCallPattern.Matches(source)
                         .Where(match => match.Groups["method"].Value == "Format"))
            {
                foreach (Match literal in ResourceKeyLiteralPattern.Matches(call.Groups["first"].Value))
                {
                    var key = literal.Groups["value"].Value.Replace('/', '.');
                    if (!key.Contains('{') && !key.EndsWith('_'))
                        formatKeys.Add(key);
                }
            }
        }

        Assert.NotEmpty(formatKeys);
        foreach (var key in formatKeys)
        {
            var translatedValues = resources.Select(language =>
            {
                Assert.True(language.Value.TryGetValue(key, out var value), $"{language.Key}:{key}");
                Assert.False(string.IsNullOrWhiteSpace(value), $"{language.Key}:{key}");
                return value!;
            }).ToArray();

            var expectedIndexes = PlaceholderIndexes(translatedValues[0]);
            Assert.NotEmpty(expectedIndexes);
            foreach (var value in translatedValues)
            {
                Assert.Equal(expectedIndexes, PlaceholderIndexes(value));
                var arguments = Enumerable.Range(0, expectedIndexes.Max() + 1)
                    .Select(_ => (object)new SampleFormatValue())
                    .ToArray();
                Assert.NotNull(string.Format(CultureInfo.InvariantCulture, value, arguments));
            }
        }
    }

    [Fact]
    public void Missing_resource_and_invalid_format_use_the_supplied_fallback()
    {
        const string missingKey = "LocalizationTests_Missing_Resource_Key";
        const string fallback = "Fallback {0}";

        Assert.Equal("Fallback", MainUiStrings.Get(missingKey, "Fallback"));
        Assert.Equal("Fallback value", MainUiStrings.Format(missingKey, fallback, "value"));

        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "MainUiStrings.cs"));
        Assert.Contains("catch (FormatException)", source, StringComparison.Ordinal);
        Assert.Contains("return string.Format(CultureInfo.CurrentUICulture, englishFallback, args);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_discrete_mode_values_keep_their_resource_key_mapping()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "DevicePage.xaml.cs"));
        var resources = ReadAllResources();
        var cpuModes = Regex.Matches(
                source,
                @"new\(CpuBoostMode\.(?<mode>\w+),\s*MainUiStrings\.Get\(\s*""(?<key>Device_CpuBoost_Mode_[^""]+)""")
            .Select(match => (Mode: match.Groups["mode"].Value, Key: match.Groups["key"].Value))
            .OrderBy(item => item.Mode, StringComparer.Ordinal)
            .ToArray();
        var powerModes = Regex.Matches(
                source,
                @"new\(WindowsPowerMode\.(?<mode>\w+),\s*MainUiStrings\.Get\(\s*""(?<key>Device_PowerMode_Mode_[^""]+)""")
            .Select(match => (Mode: match.Groups["mode"].Value, Key: match.Groups["key"].Value))
            .OrderBy(item => item.Mode, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                ("Aggressive", "Device_CpuBoost_Mode_Aggressive"),
                ("AggressiveAtGuaranteed", "Device_CpuBoost_Mode_AggressiveAtGuaranteed"),
                ("Disabled", "Device_CpuBoost_Mode_Disabled"),
                ("EfficientAggressive", "Device_CpuBoost_Mode_EfficientAggressive"),
                ("EfficientAggressiveAtGuaranteed", "Device_CpuBoost_Mode_EfficientAggressiveAtGuaranteed"),
                ("EfficientEnabled", "Device_CpuBoost_Mode_EfficientEnabled"),
                ("Enabled", "Device_CpuBoost_Mode_Enabled")
            },
            cpuModes);
        Assert.Equal(
            new[]
            {
                ("Balanced", "Device_PowerMode_Mode_Balanced"),
                ("BestPerformance", "Device_PowerMode_Mode_BestPerformance"),
                ("BestPowerEfficiency", "Device_PowerMode_Mode_BestPowerEfficiency")
            },
            powerModes);

        foreach (var key in cpuModes.Select(item => item.Key).Concat(powerModes.Select(item => item.Key)))
            AssertResourceExists(resources, key);
    }

    [Fact]
    public void Center_m_transition_requires_confirmation_and_keeps_runtime_failure_authoritative()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views", "DevicePage.xaml.cs"));
        var methodStart = source.IndexOf("private async Task RequestCenterMTransitionAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private sealed record MaximumProcessorStatePercentItem", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        var confirmation = method.IndexOf("dialog.ShowAsync()", StringComparison.Ordinal);
        var mutation = method.IndexOf("_frontend.RequestCenterMAuthorityTransitionAsync(centerMEnabled)", StringComparison.Ordinal);
        Assert.True(confirmation >= 0 && mutation > confirmation);
        Assert.Contains("!= ContentDialogResult.Primary) return;", method, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", method, StringComparison.Ordinal);
        Assert.Matches(
            @"CenterMStartupInfoBar\.Message\s*=\s*result\.FailureMessage\s*\?\?",
            method);
        Assert.Contains(
            "!centerMEnabled && result.Snapshot.State == FrontendCenterMStartupState.Enabled",
            method,
            StringComparison.Ordinal);

        var render = source[source.IndexOf("private void RenderCenterMStartup", StringComparison.Ordinal)..methodStart];
        Assert.Matches(
            @"snapshot\.FailureMessage\s*\?\?\s*MainUiStrings\.Get\(\s*""Device_CenterM_Unavailable""",
            render);
    }

    private static void AssertResourceExists(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources,
        string key)
    {
        foreach (var (language, values) in resources)
        {
            Assert.True(values.TryGetValue(key, out var value), $"{language}:{key}");
            Assert.False(string.IsNullOrWhiteSpace(value), $"{language}:{key}");
        }
    }

    private static void AssertXamlLocalizationResourcesExist(
        XDocument document,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources)
    {
        var referencedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in document.Descendants())
        {
            var uid = (string?)element.Attribute(XamlNamespace + "Uid");
            if (string.IsNullOrWhiteSpace(uid))
                continue;

            foreach (var attribute in element.Attributes())
            {
                if (!LocalizableXamlProperties.Contains(attribute.Name.LocalName)
                    || string.IsNullOrWhiteSpace(attribute.Value)
                    || attribute.Value.StartsWith('{'))
                {
                    continue;
                }

                var key = $"{uid}.{attribute.Name.LocalName}";
                referencedKeys.Add(XamlPropertyResourceKeyOverrides.TryGetValue(key, out var resourceKey)
                    ? resourceKey
                    : key);
            }

            if (ResourceOnlyXamlUidSuffixes.TryGetValue(uid, out var suffixes))
            {
                foreach (var suffix in suffixes)
                    referencedKeys.Add(uid + suffix);
            }
        }

        foreach (var key in referencedKeys.OrderBy(key => key, StringComparer.Ordinal))
            AssertResourceExists(resources, key);
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> ReadAllResources()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Strings");
        return Directory.EnumerateDirectories(directory)
            .Select(path => (Language: Path.GetFileName(path), ResourcePath: Path.Combine(path, "Resources.resw")))
            .Where(entry => File.Exists(entry.ResourcePath))
            .OrderBy(entry => entry.Language, StringComparer.Ordinal)
            .ToDictionary(
                entry => entry.Language,
                entry => (IReadOnlyDictionary<string, string>)XDocument.Load(entry.ResourcePath)
                    .Root!
                    .Elements("data")
                    .ToDictionary(
                        element => (string)element.Attribute("name")!,
                        element => element.Element("value")!.Value,
                        StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static bool IsSourceFile(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static int[] PlaceholderIndexes(string value) => PlaceholderPattern.Matches(value)
        .Select(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture))
        .Distinct()
        .OrderBy(index => index)
        .ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class SampleFormatValue : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => "sample";

        public override string ToString() => "sample";
    }
}
