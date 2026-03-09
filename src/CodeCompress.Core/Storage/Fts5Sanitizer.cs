using System.Text;
using System.Text.RegularExpressions;

namespace CodeCompress.Core.Storage;

public static partial class Fts5Sanitizer
{
    [GeneratedRegex(@"[*""()+\-^:{}\\]", RegexOptions.Compiled)]
    private static partial Regex SpecialCharsPattern();

    public static string Sanitize(string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return string.Empty;
        }

        // 1. Strip special FTS5 characters
        var cleaned = SpecialCharsPattern().Replace(rawQuery, " ");

        // 2. Split into terms
        var terms = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (terms.Length == 0)
        {
            return string.Empty;
        }

        // 3. Quote each term — wrapping in double quotes makes FTS5 treat them as literals,
        //    which neutralizes operators like AND, OR, NOT, NEAR
        var builder = new StringBuilder();
        for (int i = 0; i < terms.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            var term = terms[i].Replace("\"", "", StringComparison.Ordinal);
            if (term.Length > 0)
            {
                builder.Append('"');
                builder.Append(term);
                builder.Append('"');
            }
        }

        return builder.ToString();
    }
}
