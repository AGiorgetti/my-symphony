using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Symphony.Application.Runtime;
using Symphony.Host.Configuration;

namespace Symphony.Host.Dashboard;

public sealed class FakeDashboardDataLoader(
    IOptions<DashboardUiOptions> dashboardUiOptions,
    ILogger<FakeDashboardDataLoader> logger) : IFakeDashboardDataLoader
{
    public (FakeDashboardDataSet DataSet, FakeDashboardDataStatus Status) LoadConfigured(FakeDashboardDataSet builtInDataSet)
    {
        var configuredPath = dashboardUiOptions.Value.FakeDataJsonPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return (builtInDataSet, new FakeDashboardDataStatus(false, false, "built-in", "Using the built-in fake dataset."));
        }

        var absolutePath = Path.GetFullPath(configuredPath);
        if (!File.Exists(absolutePath))
        {
            return (
                builtInDataSet,
                new FakeDashboardDataStatus(
                    false,
                    true,
                    "file",
                    $"Configured fake data file '{absolutePath}' was not found."));
        }

        try
        {
            using var stream = File.OpenRead(absolutePath);
            var loaded = LoadFromStream(stream, Path.GetFileName(absolutePath), builtInDataSet);
            return loaded;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Failed to open configured fake dashboard data file {Path}.", absolutePath);
            return (
                builtInDataSet,
                new FakeDashboardDataStatus(
                    false,
                    true,
                    "file",
                    $"Configured fake data file '{absolutePath}' could not be read."));
        }
    }

    public async Task<(FakeDashboardDataSet DataSet, FakeDashboardDataStatus Status)> LoadFromStreamAsync(
        Stream jsonStream,
        string? sourceName,
        FakeDashboardDataSet builtInDataSet,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await jsonStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        return LoadFromStream(buffer, sourceName, builtInDataSet);
    }

    private static (FakeDashboardDataSet DataSet, FakeDashboardDataStatus Status) LoadFromStream(
        Stream jsonStream,
        string? sourceName,
        FakeDashboardDataSet builtInDataSet)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<DashboardDataExportEnvelope>(jsonStream, DashboardDataJsonSerializer.Options);
            if (envelope is null)
            {
                return Invalid(builtInDataSet, "upload", "The JSON file did not contain an export envelope.");
            }

            if (!string.Equals(envelope.SchemaVersion, DashboardDataExportSchema.CurrentVersion, StringComparison.Ordinal))
            {
                return Invalid(
                    builtInDataSet,
                    "upload",
                    $"Unsupported export schema version '{envelope.SchemaVersion}'.");
            }

            return envelope.ExportKind switch
            {
                DashboardDataExportSchema.FullBundleKind => LoadFullBundle(envelope, sourceName, builtInDataSet),
                DashboardDataExportSchema.SingleSessionKind => LoadSingleSession(envelope, sourceName, builtInDataSet),
                _ => Invalid(builtInDataSet, "upload", $"Unsupported export kind '{envelope.ExportKind}'.")
            };
        }
        catch (JsonException exception)
        {
            return Invalid(builtInDataSet, "upload", $"The JSON file is malformed or incompatible: {exception.Message}");
        }
    }

    private static (FakeDashboardDataSet DataSet, FakeDashboardDataStatus Status) LoadFullBundle(
        DashboardDataExportEnvelope envelope,
        string? sourceName,
        FakeDashboardDataSet builtInDataSet)
    {
        if (envelope.Bundle is null || envelope.SingleSession is not null)
        {
            return Invalid(builtInDataSet, "upload", "The full-bundle export payload is missing or inconsistent.");
        }

        var normalizedSessions = NormalizeImportedSessions(envelope.Bundle.Sessions);
        var dataSet = new FakeDashboardDataSet(
            envelope.Bundle.DashboardSnapshot,
            normalizedSessions,
            envelope.Bundle.IssueSnapshots.ToDictionary(snapshot => snapshot.IssueIdentifier, StringComparer.OrdinalIgnoreCase));
        var name = string.IsNullOrWhiteSpace(sourceName) ? "imported bundle" : sourceName;
        return (
            dataSet,
            new FakeDashboardDataStatus(true, false, "file", $"Loaded fake dashboard data from '{name}'."));
    }

    private static (FakeDashboardDataSet DataSet, FakeDashboardDataStatus Status) LoadSingleSession(
        DashboardDataExportEnvelope envelope,
        string? sourceName,
        FakeDashboardDataSet builtInDataSet)
    {
        var singleSession = envelope.SingleSession;
        if (singleSession is null || envelope.Bundle is not null)
        {
            return Invalid(builtInDataSet, "upload", "The single-session export payload is missing or inconsistent.");
        }

        var importedHistory = NormalizeImportedHistory(
            singleSession.History ?? new DashboardSessionHistorySnapshot(singleSession.Session, singleSession.Activities));
        var issueIdentifier = importedHistory.Session.IssueIdentifier;
        var sessions = builtInDataSet.Sessions
            .Where(history => !string.Equals(history.Session.IssueIdentifier, issueIdentifier, StringComparison.OrdinalIgnoreCase))
            .Append(importedHistory)
            .OrderByDescending(history => history.Session.StartedAt)
            .ToArray();
        var issueSnapshots = new Dictionary<string, OrchestratorIssueSnapshot>(builtInDataSet.IssueSnapshots, StringComparer.OrdinalIgnoreCase);
        if (singleSession.IssueSnapshot is null)
        {
            issueSnapshots.Remove(issueIdentifier);
        }
        else
        {
            issueSnapshots[issueIdentifier] = singleSession.IssueSnapshot;
        }

        var mergedSnapshot = MergeSingleSessionIntoDashboardSnapshot(builtInDataSet.DashboardSnapshot, singleSession, importedHistory, issueSnapshots.Values);
        var name = string.IsNullOrWhiteSpace(sourceName) ? issueIdentifier : sourceName;
        return (
            new FakeDashboardDataSet(mergedSnapshot, sessions, issueSnapshots),
            new FakeDashboardDataStatus(true, false, "upload", $"Merged imported session '{issueIdentifier}' from '{name}' into fake mode."));
    }

    private static IReadOnlyList<DashboardSessionHistorySnapshot> NormalizeImportedSessions(
        IReadOnlyList<DashboardSessionHistorySnapshot> sessions)
    {
        return sessions.Select(NormalizeImportedHistory).ToArray();
    }

    private static DashboardSessionHistorySnapshot NormalizeImportedHistory(DashboardSessionHistorySnapshot history)
    {
        var normalizedActivities = history.Activities.Select(SessionActivityTokenAnnotator.Normalize).ToArray();
        return history with
        {
            Activities = normalizedActivities
        };
    }

    private static DashboardSnapshot MergeSingleSessionIntoDashboardSnapshot(
        DashboardSnapshot baseSnapshot,
        DashboardDataSessionExport singleSession,
        DashboardSessionHistorySnapshot importedHistory,
        IEnumerable<OrchestratorIssueSnapshot> issueSnapshots)
    {
        var issueIdentifier = importedHistory.Session.IssueIdentifier;
        var activeSessions = baseSnapshot.ActiveSessions
            .Where(candidate => !string.Equals(candidate.IssueIdentifier, issueIdentifier, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (singleSession.ActiveSession is not null)
        {
            activeSessions.Add(singleSession.ActiveSession);
        }

        var retryQueue = baseSnapshot.RetryQueue
            .Where(candidate => !string.Equals(candidate.IssueIdentifier, issueIdentifier, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (singleSession.RetryEntry is not null)
        {
            retryQueue.Add(singleSession.RetryEntry);
        }

        var recentAttempts = baseSnapshot.RecentAttempts
            .Where(candidate => !string.Equals(candidate.IssueIdentifier, issueIdentifier, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (singleSession.RecentAttempt is not null)
        {
            recentAttempts.Insert(0, singleSession.RecentAttempt);
        }

        var blockedSessions = (baseSnapshot.BlockedSessions ?? [])
            .Where(candidate => !string.Equals(candidate.IssueIdentifier, issueIdentifier, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (singleSession.BlockedSession is not null)
        {
            blockedSessions.Add(singleSession.BlockedSession);
        }

        var followUpActions = (baseSnapshot.FollowUpActions ?? [])
            .Where(candidate => !string.Equals(candidate.IssueIdentifier, issueIdentifier, StringComparison.OrdinalIgnoreCase))
            .Concat(singleSession.FollowUpActions)
            .ToArray();

        var runningSnapshots = issueSnapshots
            .Where(snapshot => snapshot.Running is not null)
            .Select(snapshot => snapshot.Running!)
            .ToArray();

        return baseSnapshot with
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            RunningCount = activeSessions.Count,
            RetryingCount = retryQueue.Count,
            BlockedCount = blockedSessions.Count,
            InputTokens = runningSnapshots.Sum(snapshot => snapshot.InputTokens),
            OutputTokens = runningSnapshots.Sum(snapshot => snapshot.OutputTokens),
            TotalTokens = runningSnapshots.Sum(snapshot => snapshot.TotalTokens),
            SecondsRunning = runningSnapshots.Sum(snapshot => 0d),
            ActiveSessions = activeSessions.OrderByDescending(snapshot => snapshot.StartedAt).ToArray(),
            RetryQueue = retryQueue.OrderBy(snapshot => snapshot.DueAt).ToArray(),
            RecentAttempts = recentAttempts.OrderByDescending(snapshot => snapshot.CompletedAt).ToArray(),
            BlockedSessions = blockedSessions.OrderByDescending(snapshot => snapshot.BlockedAt).ToArray(),
            FollowUpActions = followUpActions
        };
    }

    private static (FakeDashboardDataSet DataSet, FakeDashboardDataStatus Status) Invalid(
        FakeDashboardDataSet builtInDataSet,
        string source,
        string message)
    {
        return (builtInDataSet, new FakeDashboardDataStatus(false, true, source, message));
    }
}
