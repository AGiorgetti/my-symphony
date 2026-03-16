using Symphony.Domain.Issues;

namespace Symphony.Abstractions.Orchestration;

public interface IOrchestratorDispatchQueue
{
    ValueTask<DispatchEnqueueResult> QueueAsync(
        Issue issue,
        int? attempt = null,
        CancellationToken cancellationToken = default);
}
