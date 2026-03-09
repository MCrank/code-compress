using CodeCompress.Core;
using CodeCompress.Core.Storage;
using CodeCompress.Server.Scoping;
using Microsoft.Extensions.DependencyInjection;

namespace CodeCompress.Server;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCodeCompressServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCodeCompressCore();

        // Storage — SqliteConnectionFactory creates per-project connections.
        // ISymbolStore and IIndexEngine are resolved per-project at tool invocation time,
        // not at startup, because they require an active SqliteConnection.
        services.AddSingleton<IConnectionFactory, SqliteConnectionFactory>();

        // Scoping — creates per-project scope with connection, store, and engine
        services.AddSingleton<IProjectScopeFactory, ProjectScopeFactory>();

        return services;
    }
}
