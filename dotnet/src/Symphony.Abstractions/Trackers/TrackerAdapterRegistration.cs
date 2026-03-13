namespace Symphony.Abstractions.Trackers;

public sealed record TrackerAdapterRegistration(string Kind);

public static class TrackerAdapterKinds
{
    public const string GitHub = "github";
    public const string AzureDevOps = "azure_devops";
    public const string Linear = "linear";

    public static IReadOnlyList<string> All { get; } = [GitHub, AzureDevOps, Linear];

    public static bool TryNormalize(string? configuredKind, out string normalizedKind)
    {
        normalizedKind = configuredKind?.Trim().ToLowerInvariant() ?? string.Empty;

        return normalizedKind is GitHub or AzureDevOps or Linear;
    }
}
