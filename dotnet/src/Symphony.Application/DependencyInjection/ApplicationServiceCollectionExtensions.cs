using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Orchestration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;

namespace Symphony.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddSymphonyApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ApplicationServiceMarker>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PollingRefreshTrigger>();
        services.TryAddSingleton<PollingStatusTracker>();
        services.TryAddSingleton<AttemptHistoryTracker>();
        services.TryAddSingleton<RetryDelayPlanner>();
        services.TryAddSingleton<OrchestratorControlService>();
        services.TryAddSingleton<IOrchestratorControl>(serviceProvider => serviceProvider.GetRequiredService<OrchestratorControlService>());
        services.TryAddSingleton<IOrchestratorControlStatusReader>(serviceProvider => serviceProvider.GetRequiredService<OrchestratorControlService>());
        services.TryAddSingleton<IOrchestratorExecutionGate>(serviceProvider => serviceProvider.GetRequiredService<OrchestratorControlService>());
        services.TryAddSingleton<OrchestratorDispatchQueue>();
        services.TryAddSingleton<IOrchestratorDispatchQueue>(serviceProvider => serviceProvider.GetRequiredService<OrchestratorDispatchQueue>());
        services.TryAddSingleton<IOrchestratorDispatchStatusReader>(serviceProvider => serviceProvider.GetRequiredService<OrchestratorDispatchQueue>());
        services.TryAddSingleton<ActiveSessionRegistry>();
        services.TryAddSingleton<IActiveSessionRegistry>(serviceProvider => serviceProvider.GetRequiredService<ActiveSessionRegistry>());
        services.TryAddSingleton<IOrchestratorRuntimeService, OrchestratorRuntimeService>();
        services.TryAddSingleton<IQueuedIssueWorker, NoOpQueuedIssueWorker>();
        services.TryAddSingleton<IPollingIterationHandler, OrchestratorPollingIterationHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DispatchWorkerBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RetryDispatchBackgroundService>());

        return services;
    }
}
