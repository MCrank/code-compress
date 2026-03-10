using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public interface ILanguageParser
{
    public string LanguageId { get; }
    public IReadOnlyList<string> FileExtensions { get; }
    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content);
}
