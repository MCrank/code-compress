using CodeCompress.Core.Storage;

namespace CodeCompress.Core.Tests.Storage;

internal sealed class Fts5SanitizerTests
{
    [Test]
    public async Task SanitizeReturnsEmptyForNull()
    {
        var result = Fts5Sanitizer.Sanitize(null);

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeReturnsEmptyForEmptyString()
    {
        var result = Fts5Sanitizer.Sanitize("");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeReturnsEmptyForWhitespace()
    {
        var result = Fts5Sanitizer.Sanitize("   ");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeQuotesSingleTerm()
    {
        var result = Fts5Sanitizer.Sanitize("hello");

        await Assert.That(result).IsEqualTo("\"hello\"");
    }

    [Test]
    public async Task SanitizeQuotesMultipleTerms()
    {
        var result = Fts5Sanitizer.Sanitize("hello world");

        await Assert.That(result).IsEqualTo("\"hello\" \"world\"");
    }

    [Test]
    public async Task SanitizeStripsSpecialCharacters()
    {
        var result = Fts5Sanitizer.Sanitize("name:*");

        await Assert.That(result).IsEqualTo("\"name\"");
    }

    [Test]
    public async Task SanitizeNeutralizesOperators()
    {
        var result = Fts5Sanitizer.Sanitize("foo OR bar");

        await Assert.That(result).IsEqualTo("\"foo\" \"OR\" \"bar\"");
    }

    [Test]
    public async Task SanitizeHandlesAdversarialInput()
    {
        var result = Fts5Sanitizer.Sanitize("NEAR/3 OR DROP TABLE");

        await Assert.That(result.Length).IsGreaterThan(0);
        await Assert.That(result).Contains("\"");
    }

    [Test]
    public async Task SanitizeStripsParenthesesAndDashes()
    {
        var result = Fts5Sanitizer.Sanitize(")(--");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeStripsQuotesAndQuotesTerms()
    {
        var result = Fts5Sanitizer.Sanitize("\"injected phrase\"");

        await Assert.That(result).IsEqualTo("\"injected\" \"phrase\"");
    }

    [Test]
    [Arguments("AND")]
    [Arguments("OR")]
    [Arguments("NOT")]
    [Arguments("NEAR")]
    public async Task SanitizeQuotesReservedKeyword(string keyword)
    {
        var result = Fts5Sanitizer.Sanitize(keyword);

        await Assert.That(result).IsEqualTo($"\"{keyword}\"");
    }

    [Test]
    public async Task SanitizeStripsPlusAndCaret()
    {
        var result = Fts5Sanitizer.Sanitize("+prefix ^boost");

        await Assert.That(result).IsEqualTo("\"prefix\" \"boost\"");
    }

    [Test]
    public async Task SanitizeStripsCurlyBraces()
    {
        var result = Fts5Sanitizer.Sanitize("{col1 col2}:term");

        await Assert.That(result).IsEqualTo("\"col1\" \"col2\" \"term\"");
    }

    [Test]
    public async Task SanitizeStripsBackslash()
    {
        var result = Fts5Sanitizer.Sanitize("path\\to\\file");

        await Assert.That(result).IsEqualTo("\"path\" \"to\" \"file\"");
    }

    [Test]
    public async Task SanitizePreservesAlphanumericTerms()
    {
        var result = Fts5Sanitizer.Sanitize("MyClass123 method42");

        await Assert.That(result).IsEqualTo("\"MyClass123\" \"method42\"");
    }
}
