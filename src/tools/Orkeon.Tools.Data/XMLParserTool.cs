using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Data.Constants.Xml;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Data;

// ── Enums ─────────────────────────────────────────────────────────────────

/// <summary>
/// XML operations supported by the XmlParserTool.
/// </summary>
public enum XmlOperation
{
    /// <summary>Parse the XML and return structure metadata.</summary>
    Parse,

    /// <summary>Query elements using an XPath expression.</summary>
    Query
}

// ── Request / Response records ────────────────────────────────────────

/// <summary>
/// Request parameters for parsing XML data or querying with XPath.
/// </summary>
public record XmlParserRequest
{
    /// <summary>Gets the file path to an XML file, or an XML string to parse directly.</summary>
    [FieldSchema(Description = "File path to an XML file, or an XML string", Example = "/data/config.xml")]
    public string Input { get; init; } = "";

    /// <summary>Gets an optional XPath expression to query specific elements.</summary>
    [FieldSchema(Description = "Optional XPath expression to query specific elements", Example = "//book[@category='fiction']")]
    public string? Xpath { get; init; }

    /// <summary>Gets the operation to perform: Parse or Query.</summary>
    [FieldSchema(Description = "Operation to perform: Parse or Query", IsRequired = false, Example = "Parse")]
    public XmlOperation Operation { get; init; } = XmlOperation.Parse;
}

/// <summary>
/// Information about a child XML element including its name, value, and attributes.
/// </summary>
public record XmlParserChildInfo
{
    /// <summary>Gets the element's local name.</summary>
    [ReturnSchema(Description = "Element local name", Example = "book")]
    public string Name { get; init; } = "";

    /// <summary>Gets the element's text value or a summary of its child count.</summary>
    [ReturnSchema(Description = "Element text value or child count summary", Example = "The Great Gatsby")]
    public string Value { get; init; } = "";

    /// <summary>Gets the element attributes as key-value pairs.</summary>
    [ReturnSchema(Description = "Element attributes as key-value pairs")]
    public Dictionary<string, object> Attributes { get; init; } = [];
}

/// <summary>
/// Response from parsing or querying an XML document.
/// </summary>
public record XmlParserResponse
{
    // Parse operation fields

    /// <summary>Gets the root element's local name.</summary>
    [ReturnSchema(Description = "Root element local name", Example = "catalog")]
    public string? RootElement { get; init; }

    /// <summary>Gets the root element's XML namespace.</summary>
    [ReturnSchema(Description = "Root element XML namespace", Example = "http://www.w3.org/2005/Atom")]
    public string? Namespace { get; init; }

    /// <summary>Gets the number of direct child elements.</summary>
    [ReturnSchema(Description = "Number of direct child elements", Example = 8)]
    public int? ChildCount { get; init; }

    /// <summary>Gets the number of attributes on the root element.</summary>
    [ReturnSchema(Description = "Number of attributes on the root element", Example = 2)]
    public int? AttributeCount { get; init; }

    /// <summary>Gets the root element attributes as key-value pairs.</summary>
    [ReturnSchema(Description = "Root element attributes as key-value pairs")]
    public Dictionary<string, object>? Attributes { get; init; }

    /// <summary>Gets the direct child elements with their names, values, and attributes.</summary>
    [ReturnSchema(Description = "Direct child elements with names, values, and attributes")]
    public IReadOnlyList<XmlParserChildInfo>? Children { get; init; }

    /// <summary>Gets the XML declaration (version, encoding).</summary>
    [ReturnSchema(Description = "XML declaration (version, encoding)", Example = "<?xml version=\"1.0\" encoding=\"utf-8\"?>")]
    public string? Declaration { get; init; }

    /// <summary>Gets the input source: 'file' or 'string'.</summary>
    [ReturnSchema(Description = "Input source: 'file' or 'string'", Example = "file")]
    public string? Source { get; init; }

    // Query operation fields

    /// <summary>Gets the XPath expression that was evaluated.</summary>
    [ReturnSchema(Description = "The XPath expression that was evaluated", Example = "//book[@category='fiction']")]
    public string? Xpath { get; init; }

    /// <summary>Gets the number of elements matching the XPath expression.</summary>
    [ReturnSchema(Description = "Number of elements matching the XPath", Example = 3)]
    public int? MatchCount { get; init; }

    /// <summary>Gets the matching elements with their names, values, and attributes.</summary>
    [ReturnSchema(Description = "Matching elements with names, values, and attributes")]
    public IReadOnlyList<XmlParserChildInfo>? Matches { get; init; }
}

/// <summary>
/// Tool for parsing XML data and supporting XPath queries.
/// Accepts either a file path or an XML string.
/// </summary>
[ToolContract("xml_parser",
    Name = "xml_parser",
    Description = "Parse XML data from files or strings with XPath query support.")]
public partial class XmlParserTool : FileToolBase<XmlParserRequest, XmlParserResponse>
{
    /// <summary>Initializes a new instance of <see cref="XmlParserTool"/> with virtual file system support.</summary>
    /// <param name="fileSystemService">Virtual file system service for mount-aware path resolution.</param>
    /// <param name="pathValidator">Path validator for traversal/SSRF protection (defense in depth).</param>
    /// <param name="logger">Optional logger instance.</param>
    public XmlParserTool(
        IFileSystemService fileSystemService,
        IPathValidator pathValidator,
        ILogger<XmlParserTool>? logger = null)
        : base(fileSystemService, pathValidator, logger)
    {
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(XmlParserRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Input))
            return "Input cannot be empty";

        return null;
    }

    /// <inheritdoc />
    protected override Task<XmlParserResponse> ExecuteTypedAsync(
        XmlParserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<XmlParserResponse> ExecuteTypedCoreAsync()
        {
            var (doc, isFile) = await ParseXmlDocumentAsync(request.Input, cancellationToken).ConfigureAwait(false);

            return request.Operation switch
            {
                XmlOperation.Parse => HandleParse(doc, isFile),
                XmlOperation.Query => HandleQuery(doc, request.Xpath),
                _ => throw new InvalidOperationException(
                    $"Unknown operation: {request.Operation}. Supported: Parse, Query")
            };
        }
    }

    private async Task<(XDocument Doc, bool IsFile)> ParseXmlDocumentAsync(
        string input, CancellationToken cancellationToken)
    {
        if (!input.TrimStart().StartsWith('<'))
        {
            // VFS path: read the file through the virtual file system, which resolves the
            // virtual path (e.g. /data/x.xml) to its physical mount location.
            var vfsResult = ResolveVirtualPath(input, FileAccessRights.Read);
            if (!vfsResult.IsAllowed)
                throw new InvalidOperationException(vfsResult.DenialReason ?? "path validation failed");

            var vfsContent = await _fileSystemService.TryReadAllTextAsync(input, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException($"File not found: {input}");
            return (ParseSecure(vfsContent), true);
        }

        return (ParseSecure(input), false);
    }

    /// <summary>
    /// Parses XML content using secure settings that explicitly prohibit DTD processing
    /// and disable external entity resolution (defense-in-depth against XXE attacks).
    /// </summary>
    private static XDocument ParseSecure(string xmlContent)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = XmlDefaults.MaxEntityCharacters
        };
        using var reader = XmlReader.Create(new StringReader(xmlContent), settings);
        return XDocument.Load(reader);
    }

    private XmlParserResponse HandleParse(XDocument doc, bool isFile)
    {
        var root = doc.Root
            ?? throw new InvalidOperationException("XML document has no root element");

        LogParsedXml(root.Name.LocalName);

        return new XmlParserResponse
        {
            RootElement = root.Name.LocalName,
            Namespace = root.Name.NamespaceName,
            ChildCount = root.Elements().Count(),
            AttributeCount = root.Attributes().Count(),
            Attributes = root.Attributes().ToDictionary(a => a.Name.LocalName, a => (object)a.Value),
            Children = root.Elements().Select(e => new XmlParserChildInfo
            {
                Name = e.Name.LocalName,
                Value = e.HasElements ? $"[{e.Elements().Count()} children]" : e.Value,
                Attributes = e.Attributes().ToDictionary(a => a.Name.LocalName, a => (object)a.Value)
            }).ToList(),
            Declaration = doc.Declaration?.ToString() ?? "",
            Source = isFile ? "file" : "string"
        };
    }

    private static XmlParserResponse HandleQuery(XDocument doc, string? xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath))
            throw new InvalidOperationException("XPath parameter is required for query operation");

        var elements = doc.XPathSelectElements(xpath).ToList();
        var values = elements.Select(e => new XmlParserChildInfo
        {
            Name = e.Name.LocalName,
            Value = e.HasElements ? e.ToString() : e.Value,
            Attributes = e.Attributes().ToDictionary(a => a.Name.LocalName, a => (object)a.Value)
        }).ToList();

        return new XmlParserResponse
        {
            Xpath = xpath,
            MatchCount = values.Count,
            Matches = values
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully parsed XML with root element: {Root}")]
    private partial void LogParsedXml(string root);
}
