using CodeCompress.Core;
using CodeCompress.Core.Storage;
using CodeCompress.Server.Scoping;
using CodeCompress.Server.Services;
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

        // Activity tracking — shared singleton reset by each tool call
        services.AddSingleton<IActivityTracker, ActivityTracker>();

        // Idle timeout — BackgroundService that shuts down the server after inactivity
        services.AddHostedService<IdleTimeoutService>();

        return services;
    }
}
