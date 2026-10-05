using System.Xml;
using System.Xml.Linq;

namespace SteamInputAddonforClaw.Diagnostics.XboxCatalog;

internal sealed record XboxCatalogExecutable(string Name, string? Id, string? TargetDeviceFamily, string? Architecture);

internal sealed record MicrosoftGameConfig(
    string? DefaultDisplayName,
    string IdentityName,
    string IdentityPublisher,
    string? IdentityResourceId,
    string? StoreId,
    string? TitleId,
    IReadOnlyList<XboxCatalogExecutable> Executables);

internal sealed record MicrosoftGameConfigReadResult(
    bool XmlParsed,
    bool RecognizedRoot,
    MicrosoftGameConfig? Config,
    string? FailureReason);

internal static class MicrosoftGameConfigReader
{
    internal static async Task<MicrosoftGameConfigReadResult> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2_000_000,
            MaxCharactersFromEntities = 0,
            CloseInput = false,
        };

        try
        {
            using var reader = XmlReader.Create(stream, settings);
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
            return Parse(document);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new(false, false, null, Describe(exception));
        }
    }

    internal static MicrosoftGameConfigReadResult Parse(string xml)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 };
        try
        {
            using var reader = XmlReader.Create(stream, settings);
            return Parse(XDocument.Load(reader, LoadOptions.None));
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            return new(false, false, null, Describe(exception));
        }
    }

    private static MicrosoftGameConfigReadResult Parse(XDocument document)
    {
        if (document.Root is not { } root)
            return new(true, false, null, "MicrosoftGame.config has no root element.");
        if (!string.Equals(root.Name.LocalName, "Game", StringComparison.Ordinal))
            return new(true, false, null, $"Unrecognized root element '{root.Name.LocalName}'. Expected 'Game'.");

        var identity = Child(root, "Identity");
        var identityName = Attribute(identity, "Name");
        var identityPublisher = Attribute(identity, "Publisher");
        if (identity is null || identityName is null || identityPublisher is null)
            return new(true, true, null, "Required Identity Name or Publisher is missing.");

        var executables = Child(root, "ExecutableList")?
            .Elements()
            .Where(element => string.Equals(element.Name.LocalName, "Executable", StringComparison.Ordinal))
            .Select(element => new XboxCatalogExecutable(
                Attribute(element, "Name") ?? string.Empty,
                Attribute(element, "Id"),
                Attribute(element, "TargetDeviceFamily"),
                Attribute(element, "Architecture")))
            .Where(executable => !string.IsNullOrWhiteSpace(executable.Name))
            .ToArray() ?? [];
        if (executables.Length == 0)
            return new(true, true, null, "ExecutableList contains no usable Executable Name.");

        var shellVisuals = Child(root, "ShellVisuals");
        return new(true, true, new MicrosoftGameConfig(
            Attribute(shellVisuals, "DefaultDisplayName"),
            identityName,
            identityPublisher,
            Attribute(identity, "ResourceId"),
            ElementValue(root, "StoreId"),
            ElementValue(root, "TitleId"),
            executables), null);
    }

    private static XElement? Child(XElement parent, string name) => parent.Elements()
        .FirstOrDefault(element => string.Equals(element.Name.LocalName, name, StringComparison.Ordinal));

    private static string? ElementValue(XElement parent, string name) =>
        Child(parent, name)?.Value.Trim() is { Length: > 0 } value ? value : null;

    private static string? Attribute(XElement? element, string name) =>
        element?.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))?.Value.Trim() is { Length: > 0 } value
            ? value
            : null;

    internal static string Describe(Exception exception) =>
        $"{exception.GetType().Name} (HRESULT 0x{exception.HResult:X8}): {exception.Message.Replace('\r', ' ').Replace('\n', ' ')}";
}
