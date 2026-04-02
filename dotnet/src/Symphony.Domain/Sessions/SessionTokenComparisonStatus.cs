namespace Symphony.Domain.Sessions;

public enum SessionTokenComparisonStatus
{
    None,
    EstimatedOnly,
    ReportedOnly,
    Match,
    Mismatch
}
