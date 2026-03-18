using Symphony.Application.DependencyInjection;
using Symphony.Host.Api;
using Symphony.Host.Components;
using Symphony.Host.Dashboard;
using Symphony.Host.Health;
using Symphony.Infrastructure.DependencyInjection;

namespace Symphony.Host.Composition;

public static class SymphonyHostCompositionExtensions
{
    public static WebApplicationBuilder AddSymphonyHost(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddSymphonyLogging(builder.Configuration);

        builder.Services
            .AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services
            .AddSymphonyApplication()
            .AddSymphonyInfrastructure()
            .AddConfiguredTrackerAdapter(builder.Configuration);
        builder.Services.AddSingleton<ServiceHealthSnapshotProvider>();
        builder.Services.AddSingleton<IDashboardStateService, DashboardStateService>();

        return builder;
    }

    public static WebApplication MapSymphonyHost(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapSymphonyApi();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
