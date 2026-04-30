using CodeCompress.Core;
using CodeCompress.Server.Scoping;
using Microsoft.Extensions.DependencyInjection;

namespace CodeCompress.Server;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCodeCompressServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCodeCompressCore();

        // Scoping — creates per-project scope with connection, store, and engine
        services.AddSingleton<IProjectScopeFactory, ProjectScopeFactory>();

        return services;
    }
}
