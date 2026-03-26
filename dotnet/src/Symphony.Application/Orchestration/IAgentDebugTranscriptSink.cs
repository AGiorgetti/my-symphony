namespace Symphony.Application.Orchestration;

public interface IAgentDebugTranscriptSink
{
    bool TrackAgentMessageDeltas { get; }

    void RecordOutbound(string issueIdentifier, DateTimeOffset timestamp, string title, string payload);

    void RecordInbound(string issueIdentifier, DateTimeOffset timestamp, string title, string payload);

    void RecordDiagnostic(string issueIdentifier, DateTimeOffset timestamp, string title, string detail);
}
