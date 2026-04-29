using System.Reflection;
using CodeCompress.Server.Prompts;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tests.Prompts;

internal sealed class PromptsProviderTests
{
    private static readonly string[] ExpectedMethodNames =
    [
        nameof(PromptsProvider.ExploreCodebase),
        nameof(PromptsProvider.FindImpact),
        nameof(PromptsProvider.ReviewChanges),
        nameof(PromptsProvider.DebugSymbol),
    ];

    [Test]
    public async Task ProviderHasMcpServerPromptTypeAttribute()
    {
        var attr = typeof(PromptsProvider).GetCustomAttribute<McpServerPromptTypeAttribute>();

        await Assert.That(attr).IsNotNull();
    }

    [Test]
    public async Task ProviderHasExactlyFourPromptMethods()
    {
        var methods = GetPromptMethods();

        await Assert.That(methods).Count().IsEqualTo(4);
    }

    [Test]
    public async Task AllExpectedPromptMethodsArePresent()
    {
        var actualNames = GetPromptMethods()
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expected in ExpectedMethodNames)
        {
            await Assert.That(actualNames.Contains(expected)).IsTrue();
        }
    }

    [Test]
    public async Task ExploreCodebaseReturnsUserRoleMessage()
    {
        var message = PromptsProvider.ExploreCodebase();

        await Assert.That(message).IsNotNull();
        await Assert.That(message.Role).IsEqualTo(ChatRole.User);
    }

    [Test]
    public async Task FindImpactReturnsUserRoleMessage()
    {
        var message = PromptsProvider.FindImpact();

        await Assert.That(message).IsNotNull();
        await Assert.That(message.Role).IsEqualTo(ChatRole.User);
    }

    [Test]
    public async Task ReviewChangesReturnsUserRoleMessage()
    {
        var message = PromptsProvider.ReviewChanges();

        await Assert.That(message).IsNotNull();
        await Assert.That(message.Role).IsEqualTo(ChatRole.User);
    }

    [Test]
    public async Task DebugSymbolReturnsUserRoleMessage()
    {
        var message = PromptsProvider.DebugSymbol();

        await Assert.That(message).IsNotNull();
        await Assert.That(message.Role).IsEqualTo(ChatRole.User);
    }

    [Test]
    public async Task AllPromptMessagesHaveNonEmptyText()
    {
        foreach (var name in ExpectedMethodNames)
        {
            var method = typeof(PromptsProvider).GetMethod(name,
                BindingFlags.Public | BindingFlags.Static);
            await Assert.That(method).IsNotNull();

            var message = (ChatMessage)method!.Invoke(null, null)!;
            await Assert.That(message.Text).IsNotNull();
            await Assert.That(string.IsNullOrWhiteSpace(message.Text)).IsFalse();
        }
    }

    [Test]
    public async Task AllPromptMessagesAreUnder500Tokens()
    {
        const double charsPerToken = 4.0;
        const int maxChars = (int)(500 * charsPerToken);

        foreach (var name in ExpectedMethodNames)
        {
            var method = typeof(PromptsProvider).GetMethod(name,
                BindingFlags.Public | BindingFlags.Static);
            var message = (ChatMessage)method!.Invoke(null, null)!;
            var textLength = message.Text?.Length ?? 0;

            await Assert.That(textLength).IsLessThanOrEqualTo(maxChars);
        }
    }

    private static List<MethodInfo> GetPromptMethods() =>
        typeof(PromptsProvider)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() is not null)
            .ToList();
}
