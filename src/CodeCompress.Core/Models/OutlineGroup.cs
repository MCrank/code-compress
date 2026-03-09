namespace CodeCompress.Core.Models;

public sealed record OutlineGroup(
    string Name,
    IReadOnlyList<Symbol> Symbols,
    IReadOnlyList<OutlineGroup> Children);
