namespace Symphony.Abstractions.Orchestration;

public interface IOrchestratorControl
{
    Task RequestRefreshAsync(CancellationToken cancellationToken = default);

    Task PauseAsync(CancellationToken cancellationToken = default);

    Task ResumeAsync(CancellationToken cancellationToken = default);
}
