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
}
