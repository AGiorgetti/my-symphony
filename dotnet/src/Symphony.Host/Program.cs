using Symphony.Application.DependencyInjection;
using Symphony.Host.Api;
using Symphony.Host.Composition;
using Symphony.Host.Components;
using Symphony.Host.Dashboard;
using Symphony.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSymphonyLogging(builder.Configuration);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddSymphonyApplication()
    .AddSymphonyInfrastructure()
    .AddConfiguredTrackerAdapter(builder.Configuration);
builder.Services.AddSingleton<IDashboardStateService, DashboardStateService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapSymphonyApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
