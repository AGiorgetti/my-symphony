namespace Symphony.Abstractions.Workflows;

public sealed class WorkflowFrontMatterNotMapException : WorkflowLoadException
{
    public const string ErrorCode = "workflow_front_matter_not_a_map";

    public WorkflowFrontMatterNotMapException(string workflowPath)
        : base(
            ErrorCode,
            workflowPath,
            $"Workflow file '{workflowPath}' has YAML front matter that does not decode to a map/object. Use top-level keys such as 'tracker' or 'workspace'.")
    {
    }
}
