namespace Symphony.Application.Orchestration;

public sealed class IssueExecutionStalledException : TimeoutException
{
    public IssueExecutionStalledException(string message)
        : base(message)
    {
    }
}
