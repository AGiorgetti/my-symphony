using System.Collections.ObjectModel;

namespace Symphony.Domain.Workflows;

public sealed record WorkflowDefinition
{
    public WorkflowDefinition(IReadOnlyDictionary<string, object?>? config, string? promptTemplate)
    {
        Config = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(config ?? new Dictionary<string, object?>(), StringComparer.Ordinal));
        PromptTemplate = promptTemplate?.Trim() ?? string.Empty;
    }

    public IReadOnlyDictionary<string, object?> Config { get; }

    public string PromptTemplate { get; }
}
