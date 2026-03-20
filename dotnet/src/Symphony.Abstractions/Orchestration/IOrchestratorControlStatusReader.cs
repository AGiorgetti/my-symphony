namespace Symphony.Abstractions.Orchestration;

public interface IOrchestratorControlStatusReader
{
    OrchestratorControlSnapshot GetSnapshot();
}
