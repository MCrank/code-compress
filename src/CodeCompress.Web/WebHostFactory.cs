using BlazorBlueprint.Components;
using CodeCompress.Core;
using CodeCompress.Web.Components;
using CodeCompress.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CodeCompress.Web;

public static class WebHostFactory
{
    public static WebApplication Build(int port, string bindAddress)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddBlazorBlueprintComponents(configureTheme: options =>
        {
            options.DefaultDarkMode = true;
            options.DetectSystemPreference = false;
            options.PersistToLocalStorage = true;
        });
        builder.Services.AddCodeCompressCore();
        builder.Services.AddScoped<IIndexFacade, IndexFacade>();

        builder.WebHost.UseUrls($"http://{bindAddress}:{port}");

        var app = builder.Build();

        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
