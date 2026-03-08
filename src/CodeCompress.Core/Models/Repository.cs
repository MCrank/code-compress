namespace CodeCompress.Core.Models;

public sealed record Repository(
    string Id,
    string RootPath,
    string Name,
    string Language,
    long LastIndexed,
    int FileCount,
    int SymbolCount);
