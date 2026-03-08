namespace CodeCompress.Core.Models;

public sealed record ParseResult(
    IReadOnlyList<SymbolInfo> Symbols,
    IReadOnlyList<DependencyInfo> Dependencies);
