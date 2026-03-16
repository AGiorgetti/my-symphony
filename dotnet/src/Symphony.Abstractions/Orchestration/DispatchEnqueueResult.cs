namespace Symphony.Abstractions.Orchestration;

public enum DispatchEnqueueResult
{
    Enqueued,
    AlreadyClaimed,
    NoCapacity
}
