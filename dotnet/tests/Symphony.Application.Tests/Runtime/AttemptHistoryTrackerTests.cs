using Symphony.Application.Runtime;

namespace Symphony.Application.Tests.Runtime;

public sealed class AttemptHistoryTrackerTests
{
    [Fact]
    public void Record_keeps_most_recent_entries_first_and_trims_history()
    {
        var tracker = new AttemptHistoryTracker();
        var startedAt = new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero);

        for (var index = 0; index < 25; index++)
        {
            tracker.Record(
                $"issue-{index}",
                $"ABC-{index}",
                attempt: index == 0 ? null : index,
                outcome: index % 2 == 0 ? "Succeeded" : "Retrying",
                startedAt.AddMinutes(index),
                startedAt.AddMinutes(index).AddSeconds(15),
                error: index % 2 == 0 ? null : "retry later");
        }

        var history = tracker.GetRecentAttempts();

        Assert.Equal(20, history.Count);
        Assert.Equal("ABC-24", history[0].IssueIdentifier);
        Assert.Equal("ABC-5", history[^1].IssueIdentifier);
        Assert.Equal(15d, history[0].DurationSeconds);
    }
}
