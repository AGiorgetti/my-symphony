namespace Symphony.Application.Orchestration;

public sealed class IssueExecutionTimedOutException : TimeoutException
{
    public IssueExecutionTimedOutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
