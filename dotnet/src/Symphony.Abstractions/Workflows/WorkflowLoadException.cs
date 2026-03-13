namespace Symphony.Abstractions.Workflows;

public abstract class WorkflowLoadException : Exception
{
    protected WorkflowLoadException(string code, string workflowPath, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        WorkflowPath = workflowPath;
    }

    public string Code { get; }

    public string WorkflowPath { get; }
}
