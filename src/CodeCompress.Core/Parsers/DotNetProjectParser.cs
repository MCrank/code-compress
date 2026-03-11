using System.Text;
using System.Xml;
using System.Xml.Linq;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed class DotNetProjectParser : ILanguageParser
{
    public string LanguageId => "dotnet-project";

    public IReadOnlyList<string> FileExtensions { get; } = [".csproj", ".fsproj", ".vbproj", ".props"];

    private static readonly HashSet<string> InterestingProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "TargetFramework",
        "TargetFrameworks",
        "LangVersion",
        "OutputType",
        "RootNamespace",
        "AssemblyName",
        "Nullable",
        "ImplicitUsings"
    };

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return new ParseResult([], []);
        }

        var text = Encoding.UTF8.GetString(content);
        var lineByteOffsets = ComputeLineByteOffsets(content);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            return new ParseResult([], []);
        }

        var symbols = new List<SymbolInfo>();
        var dependencies = new List<DependencyInfo>();

        foreach (var element in doc.Descendants())
        {
            var localName = element.Name.LocalName;

            switch (localName)
            {
                case "PackageReference":
                    ParsePackageReference(element, lineByteOffsets, content.Length, symbols);
                    break;

                case "PackageVersion":
                    ParsePackageVersion(element, lineByteOffsets, content.Length, symbols);
                    break;

                case "ProjectReference":
                    ParseProjectReference(element, lineByteOffsets, content.Length, symbols, dependencies);
                    break;

                default:
                    if (InterestingProperties.Contains(localName))
                    {
                        ParseBuildProperty(element, lineByteOffsets, content.Length, symbols);
                    }

                    break;
            }
        }

        return new ParseResult(symbols, dependencies);
    }

    private static void ParsePackageReference(
        XElement element, int[] lineByteOffsets, int contentLength, List<SymbolInfo> symbols)
    {
        var name = element.Attribute("Include")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var version = element.Attribute("Version")?.Value
                      ?? element.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;

        var signature = string.IsNullOrEmpty(version)
            ? $"PackageReference: {name} (centrally managed)"
            : $"PackageReference: {name} ({version})";

        var (byteOffset, byteLength, lineStart, lineEnd) =
            ComputeLocation(element, lineByteOffsets, contentLength);

        symbols.Add(new SymbolInfo(
            name, SymbolKind.Module, signature, null,
            byteOffset, byteLength, lineStart, lineEnd,
            Visibility.Public, null));
    }

    private static void ParsePackageVersion(
        XElement element, int[] lineByteOffsets, int contentLength, List<SymbolInfo> symbols)
    {
        var name = element.Attribute("Include")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var version = element.Attribute("Version")?.Value ?? "unknown";

        var signature = $"PackageVersion: {name} ({version})";

        var (byteOffset, byteLength, lineStart, lineEnd) =
            ComputeLocation(element, lineByteOffsets, contentLength);

        symbols.Add(new SymbolInfo(
            name, SymbolKind.Module, signature, null,
            byteOffset, byteLength, lineStart, lineEnd,
            Visibility.Public, null));
    }

    private static void ParseProjectReference(
        XElement element, int[] lineByteOffsets, int contentLength,
        List<SymbolInfo> symbols, List<DependencyInfo> dependencies)
    {
        var includePath = element.Attribute("Include")?.Value;
        if (string.IsNullOrEmpty(includePath))
        {
            return;
        }

        // Extract project name from path (e.g., "..\Foo\Foo.csproj" → "Foo")
        // Normalize backslashes to forward slashes for cross-platform compatibility,
        // since MSBuild paths in XML often use backslashes regardless of OS.
        var normalizedPath = includePath.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalizedPath);
        if (string.IsNullOrEmpty(fileName))
        {
            return;
        }

        var signature = $"ProjectReference: {includePath}";

        var (byteOffset, byteLength, lineStart, lineEnd) =
            ComputeLocation(element, lineByteOffsets, contentLength);

        symbols.Add(new SymbolInfo(
            fileName, SymbolKind.Module, signature, null,
            byteOffset, byteLength, lineStart, lineEnd,
            Visibility.Public, null));

        dependencies.Add(new DependencyInfo(includePath, null));
    }

    private static void ParseBuildProperty(
        XElement element, int[] lineByteOffsets, int contentLength, List<SymbolInfo> symbols)
    {
        var value = element.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var propertyName = element.Name.LocalName;
        var signature = $"{propertyName}: {value}";

        var (byteOffset, byteLength, lineStart, lineEnd) =
            ComputeLocation(element, lineByteOffsets, contentLength);

        symbols.Add(new SymbolInfo(
            propertyName, SymbolKind.Constant, signature, null,
            byteOffset, byteLength, lineStart, lineEnd,
            Visibility.Public, null));
    }

    private static (int ByteOffset, int ByteLength, int LineStart, int LineEnd) ComputeLocation(
        XElement element, int[] lineByteOffsets, int contentLength)
    {
        var lineInfo = (IXmlLineInfo)element;
        var lineStart = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
        var lineEnd = lineStart;

        // Try to compute end line from descendants
        foreach (var descendant in element.DescendantsAndSelf())
        {
            var descLineInfo = (IXmlLineInfo)descendant;
            if (descLineInfo.HasLineInfo() && descLineInfo.LineNumber > lineEnd)
            {
                lineEnd = descLineInfo.LineNumber;
            }
        }

        var byteOffset = lineStart - 1 < lineByteOffsets.Length
            ? lineByteOffsets[lineStart - 1]
            : 0;

        var elementText = element.ToString();
        var byteLength = Encoding.UTF8.GetByteCount(elementText);

        // Ensure we don't exceed content bounds
        if (byteOffset + byteLength > contentLength)
        {
            byteLength = contentLength - byteOffset;
        }

        return (byteOffset, byteLength, lineStart, lineEnd);
    }

    private static int[] ComputeLineByteOffsets(ReadOnlySpan<byte> content)
    {
        var offsets = new List<int> { 0 };
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == (byte)'\n')
            {
                offsets.Add(i + 1);
            }
        }

        return offsets.ToArray();
    }
}
