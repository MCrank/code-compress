namespace CodeCompress.Core.Models;

public sealed record ModuleApi(
    FileRecord File,
    IReadOnlyList<Symbol> Symbols,
    IReadOnlyList<Dependency> Dependencies);
