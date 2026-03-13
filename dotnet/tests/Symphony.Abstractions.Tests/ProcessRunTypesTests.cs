using Symphony.Abstractions.Processes;

namespace Symphony.Abstractions.Tests;

public sealed class ProcessRunTypesTests
{
    [Fact]
    public void ProcessRunRequest_RequiresFileName()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ProcessRunRequest(
            fileName: " ",
            arguments: [],
            workingDirectory: "C:\\workspaces\\SYM-101"));

        Assert.Equal("fileName", exception.ParamName);
    }

    [Fact]
    public void ProcessRunRequest_RejectsNonPositiveTimeout()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessRunRequest(
            fileName: "dotnet",
            arguments: ["test"],
            workingDirectory: "C:\\workspaces\\SYM-101",
            timeout: TimeSpan.Zero));

        Assert.Equal("timeout", exception.ParamName);
    }

    [Fact]
    public void ProcessRunResult_ComputesDuration()
    {
        var startedAt = new DateTimeOffset(2026, 3, 13, 15, 0, 0, TimeSpan.Zero);
        var finishedAt = startedAt.AddSeconds(12);

        var result = new ProcessRunResult(
            exitCode: 0,
            standardOutput: "ok",
            standardError: string.Empty,
            startedAt: startedAt,
            finishedAt: finishedAt);

        Assert.Equal(TimeSpan.FromSeconds(12), result.Duration);
    }
}
