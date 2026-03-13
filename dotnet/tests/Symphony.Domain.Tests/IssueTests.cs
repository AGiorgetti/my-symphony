using Symphony.Domain.Issues;

namespace Symphony.Domain.Tests;

public sealed class IssueTests
{
    [Fact]
    public void Constructor_NormalizesLabelsAndOptionalValues()
    {
        var createdAt = new DateTimeOffset(2026, 3, 13, 15, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(5);

        var issue = new Issue(
            id: " issue-id ",
            identifier: " SYM-101 ",
            title: " Add contracts ",
            description: "  Domain work  ",
            priority: 1,
            state: " In Progress ",
            branchName: " feature/contracts ",
            url: "https://github.com/AGiorgetti/my-symphony/issues/11",
            labels: ["Priority:P0", "priority:p0", " Area:Domain "],
            blockedBy:
            [
                new IssueBlocker(" parent-id ", " sym-100 ", " Todo ")
            ],
            createdAt: createdAt,
            updatedAt: updatedAt);

        Assert.Equal("issue-id", issue.Id);
        Assert.Equal("SYM-101", issue.Identifier);
        Assert.Equal("Add contracts", issue.Title);
        Assert.Equal("Domain work", issue.Description);
        Assert.Equal("In Progress", issue.State);
        Assert.Equal("in progress", issue.NormalizedState);
        Assert.Equal("feature/contracts", issue.BranchName);
        Assert.Equal(["priority:p0", "area:domain"], issue.Labels);
        Assert.Single(issue.BlockedBy);
        Assert.Equal("parent-id", issue.BlockedBy[0].Id);
        Assert.Equal("sym-100", issue.BlockedBy[0].Identifier);
        Assert.Equal("Todo", issue.BlockedBy[0].State);
        Assert.Equal(createdAt, issue.CreatedAt);
        Assert.Equal(updatedAt, issue.UpdatedAt);
    }

    [Fact]
    public void Constructor_RejectsUpdatedBeforeCreated()
    {
        var createdAt = new DateTimeOffset(2026, 3, 13, 15, 0, 0, TimeSpan.Zero);

        var exception = Assert.Throws<ArgumentException>(() => new Issue(
            id: "issue-id",
            identifier: "SYM-101",
            title: "Add contracts",
            state: "Todo",
            createdAt: createdAt,
            updatedAt: createdAt.AddSeconds(-1)));

        Assert.Equal("updatedAt", exception.ParamName);
    }
}
