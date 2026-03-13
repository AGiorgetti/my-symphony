namespace Symphony.Abstractions.Workflows;

public sealed class WorkflowParseException : WorkflowLoadException
{
    public const string ErrorCode = "workflow_parse_error";

    public WorkflowParseException(string workflowPath, string message, Exception? innerException = null)
        : base(ErrorCode, workflowPath, message, innerException)
    {
    }
}
