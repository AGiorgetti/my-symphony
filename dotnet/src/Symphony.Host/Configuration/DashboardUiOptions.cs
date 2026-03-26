namespace Symphony.Host.Configuration;

public sealed class DashboardUiOptions
{
    public const string SectionName = "Dashboard";

    public bool DebugMode { get; set; }

    public bool TrackAgentMessageDeltas { get; set; }
}
