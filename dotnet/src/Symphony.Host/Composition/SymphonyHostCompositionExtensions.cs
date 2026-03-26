using MudBlazor.Services;
using Symphony.Application.Orchestration;
using Symphony.Application.DependencyInjection;
using Symphony.Host.Api;
using Symphony.Host.Components;
using Symphony.Host.Configuration;
using Symphony.Host.Dashboard;
using Symphony.Host.Health;
using Symphony.Host.Theming;
using Symphony.Infrastructure.DependencyInjection;

namespace Symphony.Host.Composition;

public static class SymphonyHostCompositionExtensions
{
    public static WebApplicationBuilder AddSymphonyHost(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ApplyConfiguredHttpServerPort();
        builder.Logging.AddSymphonyLogging(builder.Configuration);
        builder.Services.Configure<DashboardUiOptions>(
            builder.Configuration.GetSection(DashboardUiOptions.SectionName));
        builder.Services.Configure<OrchestratorControlOptions>(
            builder.Configuration.GetSection(OrchestratorControlOptions.SectionName));

        builder.Services
            .AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddMudServices();

        builder.Services
            .AddSymphonyApplication()
            .AddSymphonyInfrastructure()
            .AddConfiguredTrackerAdapter(builder.Configuration);
        builder.Services.AddSingleton<ServiceHealthSnapshotProvider>();
        builder.Services.AddSingleton<ISessionActivityStore, SessionActivityStore>();
        builder.Services.AddSingleton<IDashboardStateService, DashboardStateService>();
        builder.Services.AddScoped<IThemeService, ThemeService>();
        builder.Services.AddScoped<ThemeService>();

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