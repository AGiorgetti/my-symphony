using Symphony.Infrastructure.Processes;

namespace Symphony.Infrastructure.Tests.Processes;

public sealed class ShellCommandRequestFactoryTests
{
    [Fact]
    public void Create_returns_windows_powershell_request()
    {
        var request = ShellCommandRequestFactory.Create(
            isWindows: true,
            command: "Write-Host hello",
            workingDirectory: "C:\\workspace",
            timeoutMs: 12_345);

        Assert.Equal("powershell", request.FileName);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-Command", "Write-Host hello"], request.Arguments);
        Assert.Equal("C:\\workspace", request.WorkingDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(12_345), request.Timeout);
    }

    [Fact]
    public void Create_returns_posix_sh_request()
    {
        var request = ShellCommandRequestFactory.Create(
            isWindows: false,
            command: "echo hello",
            workingDirectory: "/tmp/workspace",
            timeoutMs: 54_321);

        Assert.Equal("sh", request.FileName);
        Assert.Equal(["-lc", "echo hello"], request.Arguments);
        Assert.Equal("/tmp/workspace", request.WorkingDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(54_321), request.Timeout);
    }
}
