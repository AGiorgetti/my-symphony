using Symphony.Abstractions.Orchestration;

namespace Symphony.Application.Orchestration;

public sealed class FollowUpActionRegistry(TimeProvider timeProvider)
{
    private readonly Lock _stateLock = new();
    private readonly Dictionary<string, FollowUpActionSnapshot> _actionsById = new(StringComparer.Ordinal);

    public FollowUpActionSnapshot CreatePending(
        string issueId,
        string issueIdentifier,
        string sessionId,
        BlockingReasonCode reasonCode,
        string errorMessage,
        string requiredUserAction,
        IReadOnlyList<FollowUpActionOptionSnapshot>? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredUserAction);

        var action = new FollowUpActionSnapshot(
            Guid.NewGuid().ToString("N"),
            issueId.Trim(),
            issueIdentifier.Trim(),
            sessionId.Trim(),
            timeProvider.GetUtcNow(),
            reasonCode,
            errorMessage.Trim(),
            requiredUserAction.Trim(),
            options ?? Array.Empty<FollowUpActionOptionSnapshot>(),
            FollowUpActionStatus.Pending,
            ResolvedBy: null,
            ResolvedAt: null,
            SelectedOptionId: null,
            Notes: null);

        lock (_stateLock)
        {
            _actionsById[action.FollowUpActionId] = action;
        }

        return action;
    }

    public IReadOnlyList<FollowUpActionSnapshot> GetAll()
    {
        lock (_stateLock)
        {
            return _actionsById.Values
                .OrderByDescending(action => action.CreatedAt)
                .ToArray();
        }
    }

    public IReadOnlyList<FollowUpActionSnapshot> GetByIssueIdentifier(string issueIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);

        lock (_stateLock)
        {
            return _actionsById.Values
                .Where(action => string.Equals(action.IssueIdentifier, issueIdentifier.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(action => action.CreatedAt)
                .ToArray();
        }
    }

    public FollowUpActionSnapshot? GetById(string followUpActionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(followUpActionId);

        lock (_stateLock)
        {
            return _actionsById.GetValueOrDefault(followUpActionId.Trim());
        }
    }

    public FollowUpActionSnapshot? Resolve(
        string followUpActionId,
        string resolvedBy,
        string? selectedOptionId,
        string? notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(followUpActionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedBy);

        lock (_stateLock)
        {
            if (!_actionsById.TryGetValue(followUpActionId.Trim(), out var existing)
                || existing.Status != FollowUpActionStatus.Pending)
            {
                return null;
            }

            var resolved = existing with
            {
                Status = FollowUpActionStatus.Resolved,
                ResolvedBy = resolvedBy.Trim(),
                ResolvedAt = timeProvider.GetUtcNow(),
                SelectedOptionId = string.IsNullOrWhiteSpace(selectedOptionId) ? null : selectedOptionId.Trim(),
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
            };

            _actionsById[resolved.FollowUpActionId] = resolved;
            return resolved;
        }
    }
}
