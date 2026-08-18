using System.Text.Json.Serialization;

namespace CodeCompress.Core.Contracts;

public sealed record StopServerResult
{
    [property: JsonPropertyName("success")]
    public required bool Success { get; init; }

    [property: JsonPropertyName("message")]
    public required string Message { get; init; }
}
