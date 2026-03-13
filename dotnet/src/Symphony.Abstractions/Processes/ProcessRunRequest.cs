using System.Collections.ObjectModel;

namespace Symphony.Abstractions.Processes;

public sealed record ProcessRunRequest
{
    public ProcessRunRequest(
        string fileName,
        IReadOnlyList<string>? arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? standardInput = null,
        TimeSpan? timeout = null)
    {
        FileName = Require(fileName, nameof(fileName));
        WorkingDirectory = Require(workingDirectory, nameof(workingDirectory));
        Arguments = arguments?.ToArray() ?? Array.Empty<string>();
        EnvironmentVariables = new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(environmentVariables ?? new Dictionary<string, string?>(), StringComparer.Ordinal));
        StandardInput = string.IsNullOrWhiteSpace(standardInput) ? null : standardInput;

        if (timeout is not null && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be greater than zero when provided.");
        }

        Timeout = timeout;
    }

    public string FileName { get; }

    public IReadOnlyList<string> Arguments { get; }

    public string WorkingDirectory { get; }

    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; }

    public string? StandardInput { get; }

    public TimeSpan? Timeout { get; }

    private static string Require(string? value, string paramName)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed)
            ? trimmed
            : throw new ArgumentException("Value cannot be null or whitespace.", paramName);
    }
}
