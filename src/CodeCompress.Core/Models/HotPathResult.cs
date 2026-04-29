namespace CodeCompress.Core.Models;

public sealed record HotPathContextLine(int LineNumber, string Text);

public sealed record HotPathMatch(
    string Identifier,
    int Line,
    IReadOnlyList<HotPathContextLine> Context);

public sealed record HotPathResult(
    string Symbol,
    string File,
    int TotalLines,
    int ReturnedLines,
    IReadOnlyList<HotPathMatch> Matches);
