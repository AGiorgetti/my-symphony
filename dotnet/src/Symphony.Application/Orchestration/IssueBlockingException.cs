using Symphony.Abstractions.Orchestration;

namespace Symphony.Application.Orchestration;

public sealed class IssueBlockingException : Exception
{
    public IssueBlockingException(
        BlockingReasonCode reasonCode,
        string message,
        string requiredUserAction,
        IReadOnlyList<FollowUpActionOptionSnapshot>? options = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(requiredUserAction))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(requiredUserAction));
        }

        ReasonCode = reasonCode;
        RequiredUserAction = requiredUserAction.Trim();
        Options = options ?? Array.Empty<FollowUpActionOptionSnapshot>();
    }

    public BlockingReasonCode ReasonCode { get; }

    public string RequiredUserAction { get; }

    public IReadOnlyList<FollowUpActionOptionSnapshot> Options { get; }
}
