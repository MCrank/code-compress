using System.Text.RegularExpressions;

namespace CodeCompress.Core.Storage;

internal static partial class IdentifierSplitter
{
    // Splits at: lowercase/digit → uppercase, or uppercase → uppercase+lowercase (acronym end)
    [GeneratedRegex(@"(?<=[a-z\d])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex CaseBoundaryRegex();

    public static string Split(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
        {
            return string.Empty;
        }

        var segments = name.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);

        var tokens = segments
            .SelectMany(segment => CaseBoundaryRegex().Split(segment))
            .Where(static t => t.Length > 0);

#pragma warning disable CA1308 // Tokens are stored for FTS5 search, not for round-trip normalization
        return string.Join(' ', tokens.Select(static t => t.ToLowerInvariant()));
#pragma warning restore CA1308
    }
}
