namespace Symphony.Application.Configuration;

public sealed class WorkflowConfigurationException : Exception
{
    public WorkflowConfigurationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public WorkflowConfigurationException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
