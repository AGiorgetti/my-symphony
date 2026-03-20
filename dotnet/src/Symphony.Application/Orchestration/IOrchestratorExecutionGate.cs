namespace Symphony.Application.Orchestration;

public interface IOrchestratorExecutionGate
{
    Task WaitUntilStartedAsync(CancellationToken cancellationToken = default);
}
