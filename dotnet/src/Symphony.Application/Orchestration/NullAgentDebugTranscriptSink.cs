using Symphony.Domain.Sessions;

namespace Symphony.Application.Orchestration;

internal sealed class NullAgentDebugTranscriptSink : IAgentDebugTranscriptSink
{
    public bool TrackAgentMessageDeltas => false;

    public void RecordOutbound(string issueIdentifier, DateTimeOffset timestamp, string title, string payload)
    {
    }

    public void RecordInbound(string issueIdentifier, DateTimeOffset timestamp, string title, string payload)
    {
    }

    public void RecordDiagnostic(string issueIdentifier, DateTimeOffset timestamp, string title, string detail)
    {
    }

    public void RecordSessionMetadata(
        string issueIdentifier,
        DateTimeOffset timestamp,
        LiveSessionMetadata session,
        int? attempt,
        string orchestratorSessionId)
    {
    }
}
