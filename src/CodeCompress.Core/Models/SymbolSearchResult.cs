namespace CodeCompress.Core.Models;

public sealed record SymbolSearchResult(
    Symbol Symbol,
    string FilePath,
    double Rank);
