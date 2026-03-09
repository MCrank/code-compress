using System.Text;
using System.Text.RegularExpressions;

namespace CodeCompress.Server.Sanitization;

internal static partial class Fts5QuerySanitizer
{
    /// <summary>
    /// Sanitizes an FTS5 query string. Returns the sanitized query.
    /// If the result is empty after sanitization, wraps the original input as a literal phrase.
    /// </summary>
    internal static string Sanitize(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var result = query;

        // 1. Strip column filters: "name:foo" → "foo"
        result = ColumnFilterPattern().Replace(result, string.Empty);

        // 2. Strip NEAR operator: "NEAR(a, b)" → "a b" or "NEAR/3(a, b)" → "a b"
        result = NearPattern().Replace(result, match =>
        {
            var inner = match.Groups[1].Value;
            return inner.Replace(',', ' ');
        });

        // 3. Strip caret
        result = result.Replace("^", string.Empty, StringComparison.Ordinal);

        // 4. Balance quotes
        result = BalanceQuotes(result);

        // 5. Balance parentheses
        result = BalanceParentheses(result);

        // 6. Trim
        result = result.Trim();

        // 7. Fallback if empty
        if (string.IsNullOrWhiteSpace(result))
        {
            var literal = query.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();
            return string.IsNullOrWhiteSpace(literal) ? string.Empty : $"\"{literal}\"";
        }

        return result;
    }

    /// <summary>
    /// Sanitizes a glob pattern for use in SQL GLOB/LIKE clauses.
    /// Allows only alphanumeric, *, ?, /, ., -, _
    /// </summary>
    internal static string SanitizeGlob(string glob)
    {
        if (string.IsNullOrWhiteSpace(glob))
        {
            return string.Empty;
        }

        var allowedChars = glob
            .Where(c => char.IsLetterOrDigit(c) || c is '*' or '?' or '/' or '.' or '-' or '_');

        return string.Concat(allowedChars);
    }

    private static string BalanceQuotes(string input)
    {
        var count = input.Count(c => c == '"');
        if (count % 2 == 0)
        {
            return input;
        }

        // Remove last unmatched quote
        var lastIndex = input.LastIndexOf('"');
        return input.Remove(lastIndex, 1);
    }

    private static string BalanceParentheses(string input)
    {
        // First pass: remove excess closing parens
        var sb = new StringBuilder(input.Length);
        var depth = 0;
        foreach (var c in input)
        {
            if (c == '(')
            {
                depth++;
                sb.Append(c);
            }
            else if (c == ')')
            {
                if (depth > 0)
                {
                    depth--;
                    sb.Append(c);
                }
                // else: skip excess closing paren
            }
            else
            {
                sb.Append(c);
            }
        }

        // Second pass: remove excess opening parens (depth > 0 means unmatched opens)
        if (depth > 0)
        {
            var result = sb.ToString();
            sb.Clear();
            // Remove `depth` opening parens from right-to-left
            var toRemove = depth;
            for (var i = result.Length - 1; i >= 0; i--)
            {
                if (result[i] == '(' && toRemove > 0)
                {
                    toRemove--;
                    continue;
                }

                sb.Insert(0, result[i]);
            }

            return sb.ToString();
        }

        return sb.ToString();
    }

    // Match column filters like "name:foo" — removes the "name:" prefix, leaving the word after
    [GeneratedRegex(@"\b\w+:(?=\w)")]
    private static partial Regex ColumnFilterPattern();

    // Match NEAR(...) or NEAR/N(...) operators
    [GeneratedRegex(@"NEAR(?:/\d+)?\(([^)]*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex NearPattern();
}
