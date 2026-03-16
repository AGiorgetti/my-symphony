using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Orchestration;
using Symphony.Application.Polling;

namespace Symphony.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddSymphonyApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ApplicationServiceMarker>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OrchestratorDispatchQueue>();
        services.TryAddSingleton<IOrchestratorDispatchQueue>(serviceProvider => serviceProvider.GetRequiredService<OrchestratorDispatchQueue>());
        services.TryAddSingleton<IOrchestratorDispatchStatusReader>(serviceProvider => serviceProvider.GetRequiredService<OrchestratorDispatchQueue>());
        services.TryAddSingleton<IQueuedIssueWorker, NoOpQueuedIssueWorker>();
        services.TryAddSingleton<IPollingIterationHandler, NoOpPollingIterationHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DispatchWorkerBackgroundService>());

        return services;
    }
}
