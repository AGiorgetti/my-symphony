using System.Text.RegularExpressions;

namespace Symphony.Domain.Workspaces;

public sealed record Workspace
{
    private static readonly Regex WorkspaceKeyPattern =
        new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Workspace(string path, string workspaceKey, bool createdNow)
    {
        Path = Guard.Required(path, nameof(path));
        WorkspaceKey = Guard.Required(workspaceKey, nameof(workspaceKey));

        if (!WorkspaceKeyPattern.IsMatch(WorkspaceKey))
        {
            throw new ArgumentException(
                "Workspace key must use only letters, digits, '.', '_' or '-'.",
                nameof(workspaceKey));
        }

        CreatedNow = createdNow;
    }

    public string Path { get; }

    public string WorkspaceKey { get; }

    public bool CreatedNow { get; }
}
