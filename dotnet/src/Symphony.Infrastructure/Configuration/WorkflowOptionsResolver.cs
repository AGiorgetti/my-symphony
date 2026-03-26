using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Symphony.Abstractions.Trackers;
using Symphony.Application.Configuration;
using Symphony.Domain.Workflows;

namespace Symphony.Infrastructure.Configuration;

public sealed class WorkflowOptionsResolver : IWorkflowOptionsResolver
{
    private const int DefaultPollingIntervalMs = 30_000;
    private const int DefaultHooksTimeoutMs = 60_000;
    private const int DefaultMaxConcurrentAgents = 10;
    private const int DefaultMaxTurns = 20;
    private const int DefaultMaxRetryBackoffMs = 300_000;
    private const string DefaultExecMarker = "exec:agent";
    private const int DefaultTurnTimeoutMs = 3_600_000;
    private const int DefaultReadTimeoutMs = 5_000;
    private const int DefaultStallTimeoutMs = 300_000;
    private const string DefaultCodexCommand = "codex app-server";
    private static readonly IReadOnlyDictionary<string, object?> EmptySection = new ReadOnlyDictionary<string, object?>(
        new Dictionary<string, object?>(StringComparer.Ordinal));
    private static readonly string[] DefaultActiveStates = ["Todo", "In Progress"];
    private static readonly string[] DefaultTerminalStates = ["Closed", "Cancelled", "Canceled", "Duplicate", "Done"];

    public WorkflowServiceOptions Resolve(WorkflowDefinition workflowDefinition)
    {
        ArgumentNullException.ThrowIfNull(workflowDefinition);

        var trackerSection = GetSection(workflowDefinition.Config, "tracker");
        var pollingSection = GetSection(workflowDefinition.Config, "polling");
        var workspaceSection = GetSection(workflowDefinition.Config, "workspace");
        var hooksSection = GetSection(workflowDefinition.Config, "hooks");
        var agentSection = GetSection(workflowDefinition.Config, "agent");
        var codexSection = GetSection(workflowDefinition.Config, "codex");

        return new WorkflowServiceOptions(
            ResolveTrackerOptions(trackerSection),
            new WorkflowPollingOptions(
                GetPositiveIntOrDefault(pollingSection, "interval_ms", DefaultPollingIntervalMs, "polling.interval_ms")),
            new WorkflowWorkspaceOptions(
                ResolveWorkspaceRoot(workspaceSection)),
            new WorkflowHookOptions(
                GetOptionalString(hooksSection, "after_create", "hooks.after_create"),
                GetOptionalString(hooksSection, "before_run", "hooks.before_run"),
                GetOptionalString(hooksSection, "after_run", "hooks.after_run"),
                GetOptionalString(hooksSection, "before_remove", "hooks.before_remove"),
                GetHookTimeout(hooksSection)),
            new WorkflowAgentOptions(
                GetPositiveIntOrDefault(agentSection, "max_concurrent_agents", DefaultMaxConcurrentAgents, "agent.max_concurrent_agents"),
                GetPositiveIntOrDefault(agentSection, "max_turns", DefaultMaxTurns, "agent.max_turns"),
                GetPositiveIntOrDefault(agentSection, "max_retry_backoff_ms", DefaultMaxRetryBackoffMs, "agent.max_retry_backoff_ms"),
                GetStateCapMap(agentSection, "max_concurrent_agents_by_state", "agent.max_concurrent_agents_by_state"),
                GetBoolOrDefault(agentSection, "require_exec_marker", defaultValue: false, "agent.require_exec_marker"),
                GetRequiredNormalizedLabelOrDefault(agentSection, "exec_marker", DefaultExecMarker, "agent.exec_marker")),
            ResolveCodexOptions(codexSection));
    }

    private static WorkflowTrackerOptions ResolveTrackerOptions(IReadOnlyDictionary<string, object?> trackerSection)
    {
        var configuredKind = GetOptionalString(trackerSection, "kind", "tracker.kind");
        if (!TrackerAdapterKinds.TryNormalize(configuredKind, out var trackerKind))
        {
            throw new WorkflowConfigurationException(
                "unsupported_tracker_kind",
                $"tracker.kind must be one of: {string.Join(", ", TrackerAdapterKinds.All)}.");
        }

        var endpoint = GetOptionalString(trackerSection, "endpoint", "tracker.endpoint")
            ?? trackerKind switch
            {
                TrackerAdapterKinds.GitHub => "https://api.github.com",
                TrackerAdapterKinds.AzureDevOps => "https://dev.azure.com",
                TrackerAdapterKinds.Linear => "https://api.linear.app/graphql",
                _ => throw new UnreachableException()
            };

        var apiKey = ResolveRequiredApiKey(trackerSection);
        var projectSlug = GetOptionalString(trackerSection, "project_slug", "tracker.project_slug");
        var repository = GetOptionalString(trackerSection, "repository", "tracker.repository");
        var organization = GetOptionalString(trackerSection, "organization", "tracker.organization");
        var project = GetOptionalString(trackerSection, "project", "tracker.project");
        var activeStates = GetStringListOrDefault(trackerSection, "active_states", DefaultActiveStates, "tracker.active_states");
        var terminalStates = GetStringListOrDefault(trackerSection, "terminal_states", DefaultTerminalStates, "tracker.terminal_states");
        var dispatchBlockLabels = GetNormalizedLabelListOrDefault(
            trackerSection,
            "dispatch_block_labels",
            Array.Empty<string>(),
            "tracker.dispatch_block_labels");

        ValidateTrackerFields(trackerKind, repository, projectSlug, organization, project, activeStates, terminalStates);

        return new WorkflowTrackerOptions(
            trackerKind,
            endpoint,
            apiKey,
            projectSlug,
            repository,
            organization,
            project,
            activeStates,
            terminalStates)
        {
            DispatchBlockLabels = dispatchBlockLabels
        };
    }

    private static WorkflowCodexOptions ResolveCodexOptions(IReadOnlyDictionary<string, object?> codexSection)
    {
        if (codexSection.TryGetValue("command", out var rawCommandValue)
            && rawCommandValue is string rawCommand
            && string.IsNullOrWhiteSpace(rawCommand))
        {
            throw InvalidConfiguration("codex.command must not be empty.");
        }

        var command = GetOptionalString(codexSection, "command", "codex.command") ?? DefaultCodexCommand;
        if (string.IsNullOrWhiteSpace(command))
        {
            throw InvalidConfiguration("codex.command must not be empty.");
        }

        return new WorkflowCodexOptions(
            command,
            GetOptionalString(codexSection, "approval_policy", "codex.approval_policy"),
            GetOptionalString(codexSection, "thread_sandbox", "codex.thread_sandbox"),
            GetOptionalObjectMap(codexSection, "turn_sandbox_policy", "codex.turn_sandbox_policy"),
            GetPositiveIntOrDefault(codexSection, "turn_timeout_ms", DefaultTurnTimeoutMs, "codex.turn_timeout_ms"),
            GetPositiveIntOrDefault(codexSection, "read_timeout_ms", DefaultReadTimeoutMs, "codex.read_timeout_ms"),
            GetIntOrDefault(codexSection, "stall_timeout_ms", DefaultStallTimeoutMs, "codex.stall_timeout_ms"));
    }

    private static void ValidateTrackerFields(
        string trackerKind,
        string? repository,
        string? projectSlug,
        string? organization,
        string? project,
        IReadOnlyList<string> activeStates,
        IReadOnlyList<string> terminalStates)
    {
        switch (trackerKind)
        {
            case TrackerAdapterKinds.GitHub:
                if (repository is null)
                {
                    throw new WorkflowConfigurationException(
                        "missing_tracker_repository",
                        "tracker.repository is required when tracker.kind is github.");
                }

                if (!IsRepositoryName(repository))
                {
                    throw InvalidConfiguration("tracker.repository must use the format '<owner>/<repo>'.");
                }

                break;
            case TrackerAdapterKinds.Linear:
                if (projectSlug is null)
                {
                    throw new WorkflowConfigurationException(
                        "missing_tracker_project_slug",
                        "tracker.project_slug is required when tracker.kind is linear.");
                }

                break;
            case TrackerAdapterKinds.AzureDevOps:
                if (organization is null)
                {
                    throw new WorkflowConfigurationException(
                        "missing_tracker_organization",
                        "tracker.organization is required when tracker.kind is azure_devops.");
                }

                if (project is null)
                {
                    throw new WorkflowConfigurationException(
                        "missing_tracker_project",
                        "tracker.project is required when tracker.kind is azure_devops.");
                }

                break;
        }

        var activeStateSet = new HashSet<string>(activeStates.Select(static state => state.ToLowerInvariant()), StringComparer.Ordinal);
        activeStateSet.IntersectWith(terminalStates.Select(static state => state.ToLowerInvariant()));
        if (activeStateSet.Count > 0)
        {
            throw InvalidConfiguration("tracker.active_states and tracker.terminal_states cannot overlap.");
        }
    }

    private static string ResolveRequiredApiKey(IReadOnlyDictionary<string, object?> trackerSection)
    {
        var rawApiKey = GetOptionalString(trackerSection, "api_key", "tracker.api_key");
        var apiKey = rawApiKey is not null && rawApiKey.StartsWith('$')
            ? ResolveEnvironmentToken(rawApiKey)
            : rawApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new WorkflowConfigurationException(
                "missing_tracker_api_key",
                "tracker.api_key must be configured directly or via a non-empty $ENV_VAR reference.");
        }

        return apiKey;
    }

    private static string ResolveWorkspaceRoot(IReadOnlyDictionary<string, object?> workspaceSection)
    {
        var rawRoot = GetOptionalString(workspaceSection, "root", "workspace.root");
        if (rawRoot is null)
        {
            return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "symphony_workspaces"));
        }

        var resolvedRoot = ResolveEnvironmentToken(rawRoot);
        if (resolvedRoot is null)
        {
            throw InvalidConfiguration("workspace.root resolved to an empty path.");
        }

        resolvedRoot = ExpandHomeDirectory(resolvedRoot);

        if (ContainsDirectorySeparator(resolvedRoot) || Path.IsPathRooted(resolvedRoot))
        {
            return Path.GetFullPath(resolvedRoot);
        }

        return resolvedRoot;
    }

    private static IReadOnlyDictionary<string, int> GetStateCapMap(
        IReadOnlyDictionary<string, object?> section,
        string key,
        string fieldName)
    {
        if (!section.TryGetValue(key, out var value) || value is null)
        {
            return new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal));
        }

        var childSection = value switch
        {
            IReadOnlyDictionary<string, object?> readOnlyDictionary => readOnlyDictionary,
            IDictionary<string, object?> dictionary => new ReadOnlyDictionary<string, object?>(dictionary),
            _ => throw InvalidConfiguration($"{fieldName} must be an object.")
        };

        var normalized = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (state, rawValue) in childSection)
        {
            var normalizedState = state.Trim().ToLowerInvariant();
            if (normalizedState.Length == 0 || !TryGetInt(rawValue, out var parsedValue) || parsedValue <= 0)
            {
                continue;
            }

            normalized[normalizedState] = parsedValue;
        }

        return new ReadOnlyDictionary<string, int>(normalized);
    }

    private static IReadOnlyList<string> GetStringListOrDefault(
        IReadOnlyDictionary<string, object?> section,
        string key,
        IReadOnlyList<string> defaultValue,
        string fieldName)
    {
        if (!section.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue.ToArray();
        }

        if (value is string)
        {
            throw InvalidConfiguration($"{fieldName} must be a list of strings.");
        }

        if (value is not IEnumerable<object?> enumerable)
        {
            throw InvalidConfiguration($"{fieldName} must be a list of strings.");
        }

        return enumerable.Select(item => GetRequiredStringValue(item, fieldName)).ToArray();
    }

    private static IReadOnlyList<string> GetNormalizedLabelListOrDefault(
        IReadOnlyDictionary<string, object?> section,
        string key,
        IReadOnlyList<string> defaultValue,
        string fieldName)
    {
        return GetStringListOrDefault(section, key, defaultValue, fieldName)
            .Select(static label => label.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static int GetHookTimeout(IReadOnlyDictionary<string, object?> section)
    {
        var timeout = GetOptionalInt(section, "timeout_ms", "hooks.timeout_ms");
        if (timeout is null || timeout <= 0)
        {
            return DefaultHooksTimeoutMs;
        }

        return timeout.Value;
    }

    private static int GetPositiveIntOrDefault(
        IReadOnlyDictionary<string, object?> section,
        string key,
        int defaultValue,
        string fieldName)
    {
        var value = GetOptionalInt(section, key, fieldName);
        if (value is null)
        {
            return defaultValue;
        }

        if (value <= 0)
        {
            throw InvalidConfiguration($"{fieldName} must be greater than zero.");
        }

        return value.Value;
    }

    private static int GetIntOrDefault(
        IReadOnlyDictionary<string, object?> section,
        string key,
        int defaultValue,
        string fieldName)
    {
        return GetOptionalInt(section, key, fieldName) ?? defaultValue;
    }

    private static bool GetBoolOrDefault(
        IReadOnlyDictionary<string, object?> section,
        string key,
        bool defaultValue,
        string fieldName)
    {
        if (!section.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        if (!TryGetBool(value, out var parsedValue))
        {
            throw InvalidConfiguration($"{fieldName} must be a boolean.");
        }

        return parsedValue;
    }

    private static int? GetOptionalInt(
        IReadOnlyDictionary<string, object?> section,
        string key,
        string fieldName)
    {
        if (!section.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (!TryGetInt(value, out var parsedValue))
        {
            throw InvalidConfiguration($"{fieldName} must be an integer.");
        }

        return parsedValue;
    }

    private static bool TryGetInt(object? value, out int parsedValue)
    {
        switch (value)
        {
            case sbyte sbyteValue:
                parsedValue = sbyteValue;
                return true;
            case byte byteValue:
                parsedValue = byteValue;
                return true;
            case short shortValue:
                parsedValue = shortValue;
                return true;
            case ushort ushortValue:
                parsedValue = ushortValue;
                return true;
            case int intValue:
                parsedValue = intValue;
                return true;
            case uint uintValue when uintValue <= int.MaxValue:
                parsedValue = (int)uintValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                parsedValue = (int)longValue;
                return true;
            case ulong ulongValue when ulongValue <= int.MaxValue:
                parsedValue = (int)ulongValue;
                return true;
            case string stringValue when int.TryParse(stringValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringInt):
                parsedValue = stringInt;
                return true;
            default:
                parsedValue = default;
                return false;
        }
    }

    private static bool TryGetBool(object? value, out bool parsedValue)
    {
        switch (value)
        {
            case bool boolValue:
                parsedValue = boolValue;
                return true;
            case string stringValue when bool.TryParse(stringValue.Trim(), out var stringBool):
                parsedValue = stringBool;
                return true;
            default:
                parsedValue = default;
                return false;
        }
    }

    private static string? GetOptionalString(
        IReadOnlyDictionary<string, object?> section,
        string key,
        string fieldName)
    {
        if (!section.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is not string stringValue)
        {
            throw InvalidConfiguration($"{fieldName} must be a string.");
        }

        var trimmed = stringValue.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string GetRequiredNormalizedLabelOrDefault(
        IReadOnlyDictionary<string, object?> section,
        string key,
        string defaultValue,
        string fieldName)
    {
        if (section.TryGetValue(key, out var rawValue)
            && rawValue is string rawString
            && string.IsNullOrWhiteSpace(rawString))
        {
            throw InvalidConfiguration($"{fieldName} must not be empty.");
        }

        var value = GetOptionalString(section, key, fieldName);
        if (value is null)
        {
            return defaultValue;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            throw InvalidConfiguration($"{fieldName} must not be empty.");
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, object?>? GetOptionalObjectMap(
        IReadOnlyDictionary<string, object?> section,
        string key,
        string fieldName)
    {
        if (!section.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            IReadOnlyDictionary<string, object?> readOnlyDictionary => CloneObjectMap(readOnlyDictionary, fieldName),
            IDictionary<string, object?> dictionary => CloneObjectMap((IReadOnlyDictionary<string, object?>)dictionary, fieldName),
            IDictionary<object, object?> dictionary => CloneObjectMap(dictionary, fieldName),
            _ => throw InvalidConfiguration($"{fieldName} must be an object.")
        };
    }

    private static IReadOnlyDictionary<string, object?> CloneObjectMap(
        IReadOnlyDictionary<string, object?> source,
        string fieldName)
    {
        return new ReadOnlyDictionary<string, object?>(
            source.ToDictionary(
                pair => pair.Key,
                pair => NormalizeObjectValue(pair.Value, fieldName),
                StringComparer.Ordinal));
    }

    private static IReadOnlyDictionary<string, object?> CloneObjectMap(
        IDictionary<object, object?> source,
        string fieldName)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (rawKey, rawValue) in source)
        {
            if (rawKey is not string key || string.IsNullOrWhiteSpace(key))
            {
                throw InvalidConfiguration($"{fieldName} must contain only string keys.");
            }

            normalized[key.Trim()] = NormalizeObjectValue(rawValue, fieldName);
        }

        return new ReadOnlyDictionary<string, object?>(normalized);
    }

    private static object? NormalizeObjectValue(object? value, string fieldName)
    {
        return value switch
        {
            null => null,
            string or bool or char or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            IReadOnlyDictionary<string, object?> readOnlyDictionary => CloneObjectMap(readOnlyDictionary, fieldName),
            IDictionary<string, object?> dictionary => CloneObjectMap((IReadOnlyDictionary<string, object?>)dictionary, fieldName),
            IDictionary<object, object?> dictionary => CloneObjectMap(dictionary, fieldName),
            IEnumerable<object?> enumerable => enumerable.Select(item => NormalizeObjectValue(item, fieldName)).ToArray(),
            _ => throw InvalidConfiguration($"{fieldName} contains unsupported value type '{value.GetType().Name}'.")
        };
    }

    private static string GetRequiredStringValue(object? value, string fieldName)
    {
        if (value is not string stringValue)
        {
            throw InvalidConfiguration($"{fieldName} must contain only strings.");
        }

        var trimmed = stringValue.Trim();
        if (trimmed.Length == 0)
        {
            throw InvalidConfiguration($"{fieldName} must not contain empty values.");
        }

        return trimmed;
    }

    private static IReadOnlyDictionary<string, object?> GetSection(
        IReadOnlyDictionary<string, object?> root,
        string key)
    {
        if (!root.TryGetValue(key, out var value) || value is null)
        {
            return EmptySection;
        }

        return value switch
        {
            IReadOnlyDictionary<string, object?> readOnlyDictionary => readOnlyDictionary,
            IDictionary<string, object?> dictionary => new ReadOnlyDictionary<string, object?>(dictionary),
            _ => throw InvalidConfiguration($"{key} must be an object.")
        };
    }

    private static string? ResolveEnvironmentToken(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (!value.StartsWith('$'))
        {
            return value;
        }

        var variableName = value[1..].Trim();
        if (variableName.Length == 0)
        {
            return null;
        }

        var resolved = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(resolved) ? null : resolved.Trim();
    }

    private static string ExpandHomeDirectory(string value)
    {
        if (value == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal))
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDirectory, value[2..]);
        }

        return value;
    }

    private static bool ContainsDirectorySeparator(string value)
    {
        return value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar);
    }

    private static bool IsRepositoryName(string value)
    {
        var segments = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2;
    }

    private static WorkflowConfigurationException InvalidConfiguration(string message)
    {
        return new WorkflowConfigurationException("invalid_workflow_config", message);
    }
}
