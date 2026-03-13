using Symphony.Domain.Runs;

namespace Symphony.Domain.Tests;

public sealed class RunAttemptTests
{
    [Fact]
    public void Constructor_AllowsNullAttemptForFirstRun()
    {
        var attempt = new RunAttempt(
            issueId: "issue-id",
            issueIdentifier: "SYM-101",
            attempt: null,
            workspacePath: "C:\\workspaces\\SYM-101",
            startedAt: new DateTimeOffset(2026, 3, 13, 15, 0, 0, TimeSpan.Zero),
            status: RunAttemptStatus.PreparingWorkspace);

        Assert.Null(attempt.Attempt);
        Assert.Equal(RunAttemptStatus.PreparingWorkspace, attempt.Status);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveRetryAttempt()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new RunAttempt(
            issueId: "issue-id",
            issueIdentifier: "SYM-101",
            attempt: 0,
            workspacePath: "C:\\workspaces\\SYM-101",
            startedAt: new DateTimeOffset(2026, 3, 13, 15, 0, 0, TimeSpan.Zero),
            status: RunAttemptStatus.Failed));

        Assert.Equal("attempt", exception.ParamName);
    }
}
