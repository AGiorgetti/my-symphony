namespace Symphony.Tracker.Linear;

public sealed class LinearTrackerException : Exception
{
    public LinearTrackerException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
