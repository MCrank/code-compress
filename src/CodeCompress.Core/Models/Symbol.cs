namespace CodeCompress.Core.Models;

public sealed record Symbol(
    long Id,
    long FileId,
    string Name,
    string Kind,
    string Signature,
    string? ParentSymbol,
    int ByteOffset,
    int ByteLength,
    int LineStart,
    int LineEnd,
    string Visibility,
    string? DocComment);
