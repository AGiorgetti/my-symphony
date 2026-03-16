namespace Symphony.Abstractions.Orchestration;

public interface IOrchestratorDispatchStatusReader
{
    DispatchQueueSnapshot GetSnapshot();
}
