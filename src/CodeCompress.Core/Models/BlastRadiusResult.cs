namespace CodeCompress.Core.Models;

public sealed record BlastRadiusDepth(
    int Depth,
    IReadOnlyList<string> Files);

public sealed record BlastRadiusResult(
    int TotalAffected,
    IReadOnlyList<BlastRadiusDepth> Depths);
