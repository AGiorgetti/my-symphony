using Symphony.Domain.Workspaces;

namespace Symphony.Abstractions.Workspaces;

public interface IWorkspaceManager
{
    Task<Workspace> CreateForIssueAsync(string issueIdentifier, CancellationToken cancellationToken = default);

    Task DeleteForIssueAsync(string issueIdentifier, CancellationToken cancellationToken = default);
}
