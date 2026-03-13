using Symphony.Domain.Sessions;
using Symphony.Domain.Workspaces;

namespace Symphony.Domain.Tests;

public sealed class WorkspaceAndSessionTests
{
    [Fact]
    public void Workspace_RejectsInvalidWorkspaceKey()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Workspace(
            path: "C:\\workspaces\\SYM-101",
            workspaceKey: "SYM/101",
            createdNow: true));

        Assert.Equal("workspaceKey", exception.ParamName);
    }

    [Fact]
    public void LiveSession_ComposesSessionIdAndTracksTokens()
    {
        var session = new LiveSessionMetadata(
            threadId: "thread-1",
            turnId: "turn-2",
            lastCodexEvent: "turn_completed",
            lastCodexMessage: "Applied patch",
            codexInputTokens: 1200,
            codexOutputTokens: 800,
            codexTotalTokens: 2000,
            lastReportedInputTokens: 600,
            lastReportedOutputTokens: 400,
            lastReportedTotalTokens: 1000,
            turnCount: 2);

        Assert.Equal("thread-1-turn-2", session.SessionId);
        Assert.Equal("turn_completed", session.LastCodexEvent);
        Assert.Equal("Applied patch", session.LastCodexMessage);
        Assert.Equal(2, session.TurnCount);
    }

    [Fact]
    public void LiveSession_RejectsNegativeTokenCounts()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionMetadata(
            threadId: "thread-1",
            turnId: "turn-2",
            codexInputTokens: -1));

        Assert.Equal("codexInputTokens", exception.ParamName);
    }
}
