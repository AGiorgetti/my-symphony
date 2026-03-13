namespace Symphony.Abstractions.Processes;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default);
}
