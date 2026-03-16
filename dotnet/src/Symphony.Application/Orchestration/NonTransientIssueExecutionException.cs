namespace Symphony.Application.Orchestration;

public sealed class NonTransientIssueExecutionException : Exception
{
    public NonTransientIssueExecutionException(string message)
        : base(message)
    {
    }

    public NonTransientIssueExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
