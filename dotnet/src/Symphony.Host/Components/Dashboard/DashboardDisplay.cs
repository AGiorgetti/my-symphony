using Symphony.Abstractions.Orchestration;
using System.Globalization;
using MudBlazor;
using Symphony.Host.Dashboard;

namespace Symphony.Host.Components.Dashboard;

internal static class DashboardDisplay
{
    internal static string FormatTimestamp(DateTimeOffset? timestamp)
    {
        return timestamp?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
            ?? "Waiting for first tick";
    }

    internal static string FormatDuration(double durationSeconds)
    {
        return durationSeconds >= 60d
            ? $"{durationSeconds / 60d:0.#} min"
            : $"{durationSeconds:0.#} s";
    }

    internal static string FormatElapsed(DateTimeOffset startedAt, DateTimeOffset generatedAt)
    {
        return FormatDuration(Math.Max((generatedAt - startedAt).TotalSeconds, 0d));
    }

    internal static string FormatRetryEta(DateTimeOffset dueAt, DateTimeOffset generatedAt)
    {
        var remaining = dueAt - generatedAt;
        if (remaining <= TimeSpan.Zero)
        {
            return "due now";
        }

        return remaining.TotalMinutes >= 1d
            ? $"in {remaining.TotalMinutes:0.#} min"
            : $"in {remaining.TotalSeconds:0}s";
    }

    internal static Color GetHealthBadgeColor(string serviceHealth)
    {
        return serviceHealth.ToLowerInvariant() switch
        {
            "healthy" => Color.Success,
            "degraded" => Color.Warning,
            "paused" => Color.Default,
            _ => Color.Default
        };
    }

    internal static Color GetWorkflowBadgeColor(string workflowLoadStatus)
    {
        return workflowLoadStatus switch
        {
            "Loaded" => Color.Success,
            "ReloadFailedUsingLastKnownGood" => Color.Warning,
            _ => Color.Default
        };
    }

    internal static Color GetSessionStateBadgeColor(string state)
    {
        return state.ToLowerInvariant() switch
        {
            "in progress" => Color.Info,
            "streaming" => Color.Info,
            "finishing" => Color.Info,
            "retrying" => Color.Warning,
            "needs attention" => Color.Warning,
            "blocked_error" => Color.Warning,
            "succeeded" => Color.Success,
            "failed" => Color.Error,
            "timedout" => Color.Error,
            "stalled" => Color.Error,
            "canceled" => Color.Default,
            _ => Color.Primary
        };
    }

    internal static Color GetOutcomeBadgeColor(string outcome)
    {
        return outcome.ToLowerInvariant() switch
        {
            "succeeded" => Color.Success,
            "retrying" => Color.Warning,
            "blockederror" => Color.Warning,
            "failed" => Color.Error,
            "timedout" => Color.Error,
            "stalled" => Color.Error,
            "canceled" => Color.Default,
            _ => Color.Primary
        };
    }

    internal static string GetHealthMessage(DashboardSnapshot snapshot)
    {
        if (snapshot.OrchestratorState == OrchestratorControlState.Stopped)
        {
            return "Polling and new issue assignment are paused until the orchestrator is started.";
        }

        if (string.Equals(snapshot.WorkflowLoadStatus, "ReloadFailedUsingLastKnownGood", StringComparison.Ordinal))
        {
            return "Workflow reload failed; the service is using the last-known-good configuration.";
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            return "Latest poll attempt failed; the orchestrator is still running.";
        }

        if (string.Equals(snapshot.ServiceHealth, "Degraded", StringComparison.Ordinal)
            && snapshot.LastSuccessfulPollAgeSeconds is double ageSeconds)
        {
            return $"Last successful poll is {FormatDuration(ageSeconds)} old.";
        }

        return snapshot.ServiceHealth switch
        {
            "Healthy" => "Workflow loaded and recent poll ticks are healthy.",
            "Degraded" => "Health indicators are degraded; inspect logs for details.",
            _ => "Waiting for the first successful workflow load and poll tick."
        };
    }

    internal static string GetPollMessage(DashboardSnapshot snapshot)
    {
        if (snapshot.OrchestratorState == OrchestratorControlState.Stopped && snapshot.LastSuccessfulPollAt is null)
        {
            return "Polling is paused before the first orchestrator tick.";
        }

        if (snapshot.OrchestratorState == OrchestratorControlState.Stopped)
        {
            return $"Paused after {FormatTimestamp(snapshot.LastSuccessfulPollAt)}.";
        }

        if (snapshot.LastSuccessfulPollAt is null)
        {
            return "Waiting for the first successful orchestrator poll.";
        }

        var age = snapshot.LastSuccessfulPollAgeSeconds is double ageSeconds
            ? FormatDuration(ageSeconds)
            : "unknown age";
        return $"Last success {FormatTimestamp(snapshot.LastSuccessfulPollAt)} ({age} ago).";
    }

    internal static string GetWorkflowMessage(DashboardSnapshot snapshot)
    {
        if (string.Equals(snapshot.WorkflowLoadStatus, "ReloadFailedUsingLastKnownGood", StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(snapshot.WorkflowLastError)
                ? "Last workflow reload failed; continuing with the cached definition."
                : $"Reload failed: {snapshot.WorkflowLastError}";
        }

        return snapshot.WorkflowLastLoadedAt is null
            ? "Waiting for the initial workflow load."
            : $"Last loaded {FormatTimestamp(snapshot.WorkflowLastLoadedAt)}.";
    }

    internal static IReadOnlyList<DashboardAlertMessage> GetAlerts(DashboardSnapshot snapshot)
    {
        var alerts = new List<DashboardAlertMessage>();

        if (!string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            alerts.Add(new DashboardAlertMessage(
                Severity.Error,
                "Polling failure:",
                snapshot.LastError));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.WorkflowLastError))
        {
            alerts.Add(new DashboardAlertMessage(
                Severity.Warning,
                "Workflow reload warning:",
                snapshot.WorkflowLastError));
        }
        else if (snapshot.BlockedCount > 0)
        {
            alerts.Add(new DashboardAlertMessage(
                Severity.Warning,
                "Needs attention:",
                $"{snapshot.BlockedCount} blocked session(s) require explicit operator resolution."));
        }
        else if (string.Equals(snapshot.ServiceHealth, "Degraded", StringComparison.Ordinal))
        {
            alerts.Add(new DashboardAlertMessage(
                Severity.Warning,
                "Service health degraded:",
                GetHealthMessage(snapshot)));
        }

        return alerts;
    }
}

internal sealed record DashboardAlertMessage(
    Severity Color,
    string TextEmphasis,
    string Text);
