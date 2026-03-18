namespace Symphony.Tracker.AzureDevOps;

public sealed class AzureDevOpsTrackerException : Exception
{
    public AzureDevOpsTrackerException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
