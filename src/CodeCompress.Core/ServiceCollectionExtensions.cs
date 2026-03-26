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
        services.AddSingleton<ILanguageParser, JavaParser>();
        services.AddSingleton<ILanguageParser, GoParser>();
        services.AddSingleton<ILanguageParser, RustParser>();
        services.AddSingleton<ILanguageParser, PythonParser>();
        services.AddSingleton<ILanguageParser, TypeScriptJavaScriptParser>();

        // Indexing
        services.AddSingleton<IFileHasher, FileHasher>();
        services.AddSingleton<IChangeTracker, ChangeTracker>();
        services.AddSingleton<IIndexEngine, IndexEngine>();
        services.AddSingleton<IProjectRootResolver, ProjectRootResolver>();

        return services;
    }
}
