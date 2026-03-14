using CodeCompress.Core.Indexing;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tests;

internal sealed class ServerHostTests
{
    private IHost _host = null!;

    [Before(Test)]
    public void SetUp()
    {
        var builder = Host.CreateApplicationBuilder([]);
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });
        builder.Services
            .AddCodeCompressServer()
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        _host = builder.Build();
    }

    [After(Test)]
    public void TearDown()
    {
        _host?.Dispose();
    }

    [Test]
    public async Task HostBuildsWithoutErrors()
    {
        await Assert.That(_host).IsNotNull();
    }

    [Test]
    public async Task PathValidatorResolves()
    {
        var pathValidator = _host.Services.GetService<IPathValidator>();

        await Assert.That(pathValidator).IsNotNull();
    }

    [Test]
    public async Task ConnectionFactoryResolves()
    {
        var factory = _host.Services.GetService<IConnectionFactory>();

        await Assert.That(factory).IsNotNull();
    }

    [Test]
    public async Task FileHasherResolves()
    {
        var hasher = _host.Services.GetService<IFileHasher>();

        await Assert.That(hasher).IsNotNull();
    }

    [Test]
    public async Task ChangeTrackerResolves()
    {
        var tracker = _host.Services.GetService<IChangeTracker>();

        await Assert.That(tracker).IsNotNull();
    }

    [Test]
    public async Task LanguageParsersResolveAsCollection()
    {
        var parsers = _host.Services.GetService<IEnumerable<ILanguageParser>>();

        await Assert.That(parsers).IsNotNull();
        await Assert.That(parsers!.ToList()).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task LanguageParsersContainLuauParser()
    {
        var parsers = _host.Services.GetRequiredService<IEnumerable<ILanguageParser>>().ToList();

        var hasLuau = parsers.Any(p => p.LanguageId == "luau");

        await Assert.That(hasLuau).IsTrue();
    }

    [Test]
    public async Task AllExpectedServiceDescriptorsAreRegistered()
    {
        var builder = Host.CreateApplicationBuilder([]);
        builder.Services.AddCodeCompressServer();

        var hasConnectionFactory = builder.Services.Any(d => d.ServiceType == typeof(IConnectionFactory));
        var hasPathValidator = builder.Services.Any(d => d.ServiceType == typeof(IPathValidator));
        var hasFileHasher = builder.Services.Any(d => d.ServiceType == typeof(IFileHasher));
        var hasChangeTracker = builder.Services.Any(d => d.ServiceType == typeof(IChangeTracker));
        var hasParser = builder.Services.Any(d => d.ServiceType == typeof(ILanguageParser));

        await Assert.That(hasConnectionFactory).IsTrue();
        await Assert.That(hasPathValidator).IsTrue();
        await Assert.That(hasFileHasher).IsTrue();
        await Assert.That(hasChangeTracker).IsTrue();
        await Assert.That(hasParser).IsTrue();
    }

    [Test]
    public async Task McpServerIsConfigured()
    {
        // Verify MCP server services are registered via AddMcpServer() + WithStdioServerTransport()
        var mcpServer = _host.Services.GetService<McpServer>();

        await Assert.That(mcpServer).IsNotNull();
    }

}
