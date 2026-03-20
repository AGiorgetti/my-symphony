namespace Symphony.Abstractions.Orchestration;

public sealed record OrchestratorControlSnapshot(
    OrchestratorControlState State,
    DateTimeOffset ChangedAt);

public enum OrchestratorControlState
{
    Started = 0,
    Stopped = 1
}
