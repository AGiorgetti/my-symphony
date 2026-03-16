using Symphony.Application.DependencyInjection;
using Symphony.Host.Api;
using Symphony.Host.Composition;
using Symphony.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSymphonyLogging(builder.Configuration);

builder.Services
    .AddSymphonyApplication()
    .AddSymphonyInfrastructure()
    .AddConfiguredTrackerAdapter(builder.Configuration);

var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapSymphonyApi();

app.Run();
