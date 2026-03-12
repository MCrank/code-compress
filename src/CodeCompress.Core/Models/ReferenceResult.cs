namespace CodeCompress.Core.Models;

public sealed record ReferenceResult(
    string FilePath,
    int Line,
    string ContextSnippet,
    double Rank);
