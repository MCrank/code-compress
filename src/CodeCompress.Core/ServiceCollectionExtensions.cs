using CodeCompress.Core.Indexing;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace CodeCompress.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCodeCompressCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Validation
        services.AddSingleton<IPathValidator, PathValidatorService>();

        // Parsers
        services.AddSingleton<ILanguageParser, LuauParser>();
        services.AddSingleton<ILanguageParser, CSharpParser>();
        services.AddSingleton<ILanguageParser, DotNetProjectParser>();
        services.AddSingleton<ILanguageParser, JsonConfigParser>();
        services.AddSingleton<ILanguageParser, BlazorRazorParser>();
        services.AddSingleton<ILanguageParser, TerraformParser>();

        // Indexing
        services.AddSingleton<IFileHasher, FileHasher>();
        services.AddSingleton<IChangeTracker, ChangeTracker>();
        services.AddSingleton<IIndexEngine, IndexEngine>();

        return services;
    }
}
