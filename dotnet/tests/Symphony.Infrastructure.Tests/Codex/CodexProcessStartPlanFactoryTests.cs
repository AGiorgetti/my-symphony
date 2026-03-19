using Symphony.Infrastructure.Codex;

namespace Symphony.Infrastructure.Tests.Codex;

public sealed class CodexProcessStartPlanFactoryTests
{
    [Fact]
    public void Create_returns_windows_shell_candidates_in_priority_order()
    {
        var plans = CodexProcessStartPlanFactory.Create(
            isWindows: true,
            new CodexProcessStartRequest("codex app-server", "C:\\work"));

        Assert.Collection(
            plans,
            first =>
            {
                Assert.Equal("pwsh", first.FileName);
                Assert.Equal(["-NoProfile", "-NonInteractive", "-Command", "codex app-server"], first.Arguments);
                Assert.Equal("C:\\work", first.WorkingDirectory);
            },
            second =>
            {
                Assert.Equal("powershell", second.FileName);
                Assert.Equal(["-NoProfile", "-NonInteractive", "-Command", "codex app-server"], second.Arguments);
                Assert.Equal("C:\\work", second.WorkingDirectory);
            });
    }

    [Fact]
    public void Create_returns_posix_shell_candidate()
    {
        var plans = CodexProcessStartPlanFactory.Create(
            isWindows: false,
            new CodexProcessStartRequest("codex app-server", "/tmp/work"));

        var plan = Assert.Single(plans);
        Assert.Equal("sh", plan.FileName);
        Assert.Equal(["-lc", "codex app-server"], plan.Arguments);
        Assert.Equal("/tmp/work", plan.WorkingDirectory);
    }
}
