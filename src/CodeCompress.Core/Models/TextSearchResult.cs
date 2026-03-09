namespace CodeCompress.Core.Models;

public sealed record TextSearchResult(
    string FilePath,
    string Snippet,
    double Rank);
