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

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsNotNull();
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

        await Assert.That(result.Message).Contains("restart");
    }
}
