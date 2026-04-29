using CodeCompress.Core.Storage;

namespace CodeCompress.Core.Tests.Storage;

internal sealed class IdentifierSplitterTests
{
    [Test]
    [Arguments("getUserProfile", "get user profile")]
    [Arguments("UserProfileService", "user profile service")]
    [Arguments("ValidateEmailAddress", "validate email address")]
    [Arguments("getHTTPResponse", "get http response")]
    [Arguments("MyHTMLParser", "my html parser")]
    [Arguments("user_profile_handler", "user profile handler")]
    [Arguments("CONSTANT_VALUE", "constant value")]
    [Arguments("max-retry-count", "max retry count")]
    [Arguments("my-component-name", "my component name")]
    [Arguments("handler", "handler")]
    [Arguments("ID", "id")]
    [Arguments("MyClass", "my class")]
    public async Task SplitTokenizesIdentifier(string input, string expected)
    {
        var result = IdentifierSplitter.Split(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task SplitEmptyStringReturnsEmpty()
    {
        var result = IdentifierSplitter.Split(string.Empty);
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SplitProducesTokensThatEnableWordSearch()
    {
        var result = IdentifierSplitter.Split("getUserProfile");
        await Assert.That(result).Contains("user");
        await Assert.That(result).Contains("profile");
    }
}
