using CodeCompress.Server.Sanitization;

namespace CodeCompress.Server.Tests.Sanitization;

internal sealed class Fts5QuerySanitizerTests
{
    [Test]
    public async Task SanitizeSimpleWordPassesThrough()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage");

        await Assert.That(result).IsEqualTo("damage");
    }

    [Test]
    public async Task SanitizeMultipleWordsPassesThrough()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage health");

        await Assert.That(result).IsEqualTo("damage health");
    }

    [Test]
    public async Task SanitizeBooleanOperatorsAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage OR health");

        await Assert.That(result).IsEqualTo("damage OR health");
    }

    [Test]
    public async Task SanitizeQuotedPhraseAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("\"damage result\"");

        await Assert.That(result).IsEqualTo("\"damage result\"");
    }

    [Test]
    public async Task SanitizePrefixMatchAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("combat*");

        await Assert.That(result).IsEqualTo("combat*");
    }

    [Test]
    public async Task SanitizeUnbalancedQuotesStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage \"result");

        await Assert.That(result).IsEqualTo("damage result");
    }

    [Test]
    public async Task SanitizeColumnFilterStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("name:foo");

        await Assert.That(result).IsEqualTo("foo");
    }

    [Test]
    public async Task SanitizeNearOperatorStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("NEAR(damage, health)");

        await Assert.That(result).IsEqualTo("damage  health");
    }

    [Test]
    public async Task SanitizeCaretOperatorStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("^damage");

        await Assert.That(result).IsEqualTo("damage");
    }

    [Test]
    public async Task SanitizeUnbalancedParenthesesStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("(damage OR");

        await Assert.That(result).IsEqualTo("damage OR");
    }

    [Test]
    public async Task SanitizeBalancedParenthesesAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("(damage OR health)");

        await Assert.That(result).IsEqualTo("(damage OR health)");
    }

    [Test]
    public async Task SanitizeEmptyAfterSanitizationFallsBackToLiteral()
    {
        var result = Fts5QuerySanitizer.Sanitize("^");

        await Assert.That(result).IsEqualTo("\"^\"");
    }

    [Test]
    public async Task SanitizeEmptyInputReturnsEmpty()
    {
        var result = Fts5QuerySanitizer.Sanitize("");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeWhitespaceInputReturnsEmpty()
    {
        var result = Fts5QuerySanitizer.Sanitize("   ");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeGlobSimplePatternPassesThrough()
    {
        var result = Fts5QuerySanitizer.SanitizeGlob("*.luau");

        await Assert.That(result).IsEqualTo("*.luau");
    }

    [Test]
    public async Task SanitizeGlobPathPatternPassesThrough()
    {
        var result = Fts5QuerySanitizer.SanitizeGlob("src/services/*.lua");

        await Assert.That(result).IsEqualTo("src/services/*.lua");
    }

    [Test]
    public async Task SanitizeGlobMaliciousInputSanitized()
    {
        var result = Fts5QuerySanitizer.SanitizeGlob("*.luau; DROP TABLE files");

        await Assert.That(result).IsEqualTo("*.luauDROPTABLEfiles");
    }

    [Test]
    public async Task SanitizeGlobEmptyInputReturnsEmpty()
    {
        var result = Fts5QuerySanitizer.SanitizeGlob("");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeExcessClosingParenthesesStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage OR health))");

        await Assert.That(result).IsEqualTo("damage OR health");
    }

    [Test]
    public async Task SanitizeNearWithDistanceStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("NEAR/3(damage, health)");

        await Assert.That(result).IsEqualTo("damage  health");
    }

    [Test]
    public async Task SanitizeNotOperatorAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage NOT health");

        await Assert.That(result).IsEqualTo("damage NOT health");
    }

    [Test]
    public async Task SanitizeAndOperatorAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage AND health");

        await Assert.That(result).IsEqualTo("damage AND health");
    }
}
