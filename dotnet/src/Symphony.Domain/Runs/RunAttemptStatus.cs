namespace Symphony.Domain.Runs;

public enum RunAttemptStatus
{
    PreparingWorkspace,
    BuildingPrompt,
    LaunchingAgentProcess,
    InitializingSession,
    StreamingTurn,
    Finishing,
    Succeeded,
    BlockedError,
    Failed,
    TimedOut,
    Stalled,
    CanceledByReconciliation
}
