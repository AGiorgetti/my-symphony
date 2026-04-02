using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Symphony.Host.Configuration;

namespace Symphony.Host.Dashboard;

public enum DashboardDataMode
{
    Live,
    Fake
}

public sealed record DashboardPageMode(
    DashboardDataMode DataMode,
    string? ExplicitMode = null)
{
    public bool IsFake => DataMode == DashboardDataMode.Fake;

    public string? QueryValue => ExplicitMode;
}

public interface IDashboardPageModeResolver
{
    DashboardPageMode Resolve(string? requestedMode);
}

public sealed class DashboardPageModeResolver(IOptions<DashboardUiOptions> options) : IDashboardPageModeResolver
{
    public DashboardPageMode Resolve(string? requestedMode)
    {
        var normalizedMode = requestedMode?.Trim().ToLowerInvariant();
        if (!options.Value.EnableFakeDataMode)
        {
            return new DashboardPageMode(DashboardDataMode.Live);
        }

        return normalizedMode switch
        {
            "fake" => new DashboardPageMode(DashboardDataMode.Fake, "fake"),
            "live" => new DashboardPageMode(DashboardDataMode.Live, "live"),
            _ => new DashboardPageMode(DashboardDataMode.Live)
        };
    }
}

public static class DashboardPageLinks
{
    public static string WithMode(string path, DashboardPageMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return string.IsNullOrWhiteSpace(mode.QueryValue)
            ? path
            : QueryHelpers.AddQueryString(path, "mode", mode.QueryValue);
    }
}
