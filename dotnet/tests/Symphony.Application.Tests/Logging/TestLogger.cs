using Microsoft.Extensions.Logging;

namespace Symphony.Application.Tests.Logging;

internal sealed class TestLogger<T> : ILogger<T>
{
    public List<TestLogEntry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        var structuredState = state as IEnumerable<KeyValuePair<string, object?>>;
        var values = structuredState?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, object?>(StringComparer.Ordinal);

        Entries.Add(new TestLogEntry(logLevel, formatter(state, exception), values, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

internal sealed record TestLogEntry(
    LogLevel LogLevel,
    string Message,
    IReadOnlyDictionary<string, object?> State,
    Exception? Exception);
