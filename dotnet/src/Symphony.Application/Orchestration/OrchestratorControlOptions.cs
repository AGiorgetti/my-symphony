using Symphony.Abstractions.Orchestration;

namespace Symphony.Application.Orchestration;

public sealed class OrchestratorControlOptions
{
    public const string SectionName = "Orchestration";

    public string InitialState { get; set; } = nameof(OrchestratorControlState.Started);
}
