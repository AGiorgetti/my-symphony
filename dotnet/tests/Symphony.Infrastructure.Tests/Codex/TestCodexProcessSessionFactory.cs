using System.Text.Json;
using System.Threading.Channels;
using Symphony.Infrastructure.Codex;

namespace Symphony.Infrastructure.Tests.Codex;

internal sealed class TestCodexProcessSessionFactory(Func<string, TestCodexProcessSession, Task>? onSendAsync = null)
    : ICodexProcessSessionFactory
{
    public List<CodexProcessStartRequest> Requests { get; } = [];

    public TestCodexProcessSession? Session { get; private set; }

    public Task<ICodexProcessSession> StartAsync(CodexProcessStartRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Session = new TestCodexProcessSession(onSendAsync);
        return Task.FromResult<ICodexProcessSession>(Session);
    }
}

internal sealed class TestCodexProcessSession(Func<string, TestCodexProcessSession, Task>? onSendAsync) : ICodexProcessSession
{
    private readonly Channel<string?> _stdout = Channel.CreateUnbounded<string?>();
    private readonly Channel<string?> _stderr = Channel.CreateUnbounded<string?>();
    private readonly TaskCompletionSource _exitCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string> SentLines { get; } = [];

    public int? ProcessId { get; init; } = 42_123;

    public int? ExitCode { get; private set; }

    public bool HasExited { get; private set; }

    public bool WasKilled { get; private set; }

    public async Task SendAsync(string line, CancellationToken cancellationToken)
    {
        SentLines.Add(line);

        if (onSendAsync is not null)
        {
            await onSendAsync(line, this).ConfigureAwait(false);
        }
    }

    public Task<string?> ReadStandardOutputLineAsync(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        return ReadAsync(_stdout.Reader, timeout, cancellationToken);
    }

    public Task<string?> ReadStandardErrorLineAsync(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        return ReadAsync(_stderr.Reader, timeout, cancellationToken);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return _exitCompletionSource.Task.WaitAsync(cancellationToken);
    }

    public void Kill()
    {
        Exit(0);
        WasKilled = true;
    }

    public ValueTask DisposeAsync()
    {
        Exit(ExitCode ?? 0);
        return ValueTask.CompletedTask;
    }

    public void EnqueueStdout(object payload)
    {
        _stdout.Writer.TryWrite(JsonSerializer.Serialize(payload));
    }

    public void EnqueueStdoutRaw(string line)
    {
        _stdout.Writer.TryWrite(line);
    }

    public void EnqueueStderr(string line)
    {
        _stderr.Writer.TryWrite(line);
    }

    public void CompleteStdout()
    {
        _stdout.Writer.TryComplete();
    }

    public void CompleteStderr()
    {
        _stderr.Writer.TryComplete();
    }

    public void Exit(int exitCode)
    {
        if (HasExited)
        {
            return;
        }

        HasExited = true;
        ExitCode = exitCode;
        CompleteStdout();
        CompleteStderr();
        _exitCompletionSource.TrySetResult();
    }

    private static async Task<string?> ReadAsync(
        ChannelReader<string?> reader,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        if (timeout is null)
        {
            return await ReadCoreAsync(reader, cancellationToken).ConfigureAwait(false);
        }

        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(timeout.Value);
        return await ReadCoreAsync(reader, timeoutCancellationTokenSource.Token).ConfigureAwait(false);
    }

    private static async Task<string?> ReadCoreAsync(ChannelReader<string?> reader, CancellationToken cancellationToken)
    {
        if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }
}
