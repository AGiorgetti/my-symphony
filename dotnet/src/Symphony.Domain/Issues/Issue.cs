namespace Symphony.Domain.Issues;

public sealed record Issue
{
    public Issue(
        string id,
        string identifier,
        string title,
        string? description = null,
        int? priority = null,
        string state = "",
        string? branchName = null,
        string? url = null,
        IEnumerable<string>? labels = null,
        IEnumerable<IssueBlocker>? blockedBy = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
    {
        Id = Guard.Required(id, nameof(id));
        Identifier = Guard.Required(identifier, nameof(identifier));
        Title = Guard.Required(title, nameof(title));
        Description = Guard.Optional(description);
        Priority = priority;
        State = Guard.Required(state, nameof(state));
        BranchName = Guard.Optional(branchName);
        Url = NormalizeUrl(url);
        Labels = NormalizeLabels(labels);
        BlockedBy = NormalizeBlockers(blockedBy);

        if (createdAt is not null && updatedAt is not null && updatedAt < createdAt)
        {
            throw new ArgumentException("UpdatedAt cannot be earlier than CreatedAt.", nameof(updatedAt));
        }

        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public string Id { get; }

    public string Identifier { get; }

    public string Title { get; }

    public string? Description { get; }

    public int? Priority { get; }

    public string State { get; }

    public string NormalizedState => State.ToLowerInvariant();

    public string? BranchName { get; }

    public string? Url { get; }

    public IReadOnlyList<string> Labels { get; }

    public IReadOnlyList<IssueBlocker> BlockedBy { get; }

    public DateTimeOffset? CreatedAt { get; }

    public DateTimeOffset? UpdatedAt { get; }

    private static IReadOnlyList<IssueBlocker> NormalizeBlockers(IEnumerable<IssueBlocker>? blockedBy)
    {
        if (blockedBy is null)
        {
            return Array.Empty<IssueBlocker>();
        }

        return blockedBy
            .Select(blocker => blocker ?? throw new ArgumentException("Blocker entries cannot be null.", nameof(blockedBy)))
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeLabels(IEnumerable<string>? labels)
    {
        if (labels is null)
        {
            return Array.Empty<string>();
        }

        return labels
            .Select(label => label?.Trim())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? NormalizeUrl(string? url)
    {
        var normalized = Guard.Optional(url);
        if (normalized is null)
        {
            return null;
        }

        return Uri.IsWellFormedUriString(normalized, UriKind.Absolute)
            ? normalized
            : throw new ArgumentException("URL must be an absolute URI when provided.", nameof(url));
    }
}
