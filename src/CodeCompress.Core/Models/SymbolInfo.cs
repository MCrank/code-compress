namespace CodeCompress.Core.Models;

public sealed record SymbolInfo(
    string Name,
    SymbolKind Kind,
    string Signature,
    string? ParentSymbol,
    int ByteOffset,
    int ByteLength,
    int LineStart,
    int LineEnd,
    Visibility Visibility,
    string? DocComment);
