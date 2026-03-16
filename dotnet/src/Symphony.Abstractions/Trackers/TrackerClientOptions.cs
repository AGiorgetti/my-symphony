namespace Symphony.Abstractions.Trackers;

public sealed record TrackerClientOptions(
    string Kind,
    string Endpoint,
    string ApiKey,
    string? Repository,
    string? ProjectSlug,
    string? Organization,
    string? Project,
    IReadOnlyList<string> ActiveStates,
    IReadOnlyList<string> TerminalStates);
