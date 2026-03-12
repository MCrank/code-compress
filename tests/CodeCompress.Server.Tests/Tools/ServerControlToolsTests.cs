using System.Text.Json;
using CodeCompress.Server.Tools;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace CodeCompress.Server.Tests.Tools;

internal sealed class ServerControlToolsTests
{
    [Test]
    public async Task StopServerReturnsConfirmationMessage()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var tools = new ServerControlTools(lifetime);

        var result = await tools.StopServer().ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("success").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("message").GetString()).IsNotNull();
    }

    [Test]
    public async Task StopServerCallsStopApplicationAfterDelay()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var tools = new ServerControlTools(lifetime);

        var result = await tools.StopServer().ConfigureAwait(false);

        // StopApplication is called after a 500ms delay to allow response to be sent
        await Task.Delay(600).ConfigureAwait(false);

        lifetime.Received(1).StopApplication();
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task StopServerMessageIndicatesAutoRestart()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var tools = new ServerControlTools(lifetime);

        var result = await tools.StopServer().ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var message = doc.RootElement.GetProperty("message").GetString()!;
        await Assert.That(message).Contains("restart");
    }
}
