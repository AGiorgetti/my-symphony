namespace Symphony.Domain.Issues;

public sealed record IssueBlocker
{
    public IssueBlocker(string? id, string? identifier, string? state)
    {
        Id = Guard.Optional(id);
        Identifier = Guard.Optional(identifier);
        State = Guard.Optional(state);
    }

    public string? Id { get; }

    public string? Identifier { get; }

    public string? State { get; }

    public string? NormalizedState => State?.ToLowerInvariant();
}
