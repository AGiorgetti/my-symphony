namespace Symphony.Abstractions.Workflows;

public sealed class MissingWorkflowFileException : WorkflowLoadException
{
    public const string ErrorCode = "missing_workflow_file";

    public MissingWorkflowFileException(string workflowPath)
        : base(
            ErrorCode,
            workflowPath,
            $"Workflow file '{workflowPath}' was not found. Provide a valid WORKFLOW.md path or add WORKFLOW.md to the working directory.")
    {
    }
}
