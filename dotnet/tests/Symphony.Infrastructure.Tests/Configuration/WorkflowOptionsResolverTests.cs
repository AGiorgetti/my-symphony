using Symphony.Application.Configuration;
using Symphony.Domain.Workflows;
using Symphony.Infrastructure.Configuration;

namespace Symphony.Infrastructure.Tests.Configuration;

public sealed class WorkflowOptionsResolverTests
{
    private readonly WorkflowOptionsResolver _resolver = new();

    [Fact]
    public void Resolve_applies_defaults_for_minimal_github_configuration()
    {
        var definition = CreateDefinition(new Dictionary<string, object?>
        {
            ["tracker"] = new Dictionary<string, object?>
            {
                ["kind"] = "github",
                ["api_key"] = "gh-token",
                ["repository"] = "AGiorgetti/my-symphony"
            }
        });

        var options = _resolver.Resolve(definition);

        Assert.Equal("github", options.Tracker.Kind);
        Assert.Equal("https://api.github.com", options.Tracker.Endpoint);
        Assert.Equal("gh-token", options.Tracker.ApiKey);
        Assert.Equal(new[] { "Todo", "In Progress" }, options.Tracker.ActiveStates);
        Assert.Equal(new[] { "Closed", "Cancelled", "Canceled", "Duplicate", "Done" }, options.Tracker.TerminalStates);
        Assert.Equal(30_000, options.Polling.IntervalMs);
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "symphony_workspaces")), options.Workspace.Root);
        Assert.Equal(60_000, options.Hooks.TimeoutMs);
        Assert.Equal(10, options.Agent.MaxConcurrentAgents);
        Assert.Equal(20, options.Agent.MaxTurns);
        Assert.Equal(300_000, options.Agent.MaxRetryBackoffMs);
        Assert.Empty(options.Agent.MaxConcurrentAgentsByState);
        Assert.Equal("codex app-server", options.Codex.Command);
        Assert.Equal(3_600_000, options.Codex.TurnTimeoutMs);
        Assert.Equal(5_000, options.Codex.ReadTimeoutMs);
        Assert.Equal(300_000, options.Codex.StallTimeoutMs);
    }

    [Fact]
    public void Resolve_expands_environment_values_and_normalizes_state_caps()
    {
        const string apiKeyVariable = "SYMPHONY_TEST_API_KEY";
        const string workspaceRootVariable = "SYMPHONY_TEST_WORKSPACE_ROOT";
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "resolver-root", Guid.NewGuid().ToString("N"));

        using var apiKeyScope = new EnvironmentVariableScope(apiKeyVariable, "resolved-api-key");
        using var workspaceRootScope = new EnvironmentVariableScope(workspaceRootVariable, workspaceRoot);

        var definition = CreateDefinition(new Dictionary<string, object?>
        {
            ["tracker"] = new Dictionary<string, object?>
            {
                ["kind"] = "github",
                ["api_key"] = $"${apiKeyVariable}",
                ["repository"] = "AGiorgetti/my-symphony"
            },
            ["workspace"] = new Dictionary<string, object?>
            {
                ["root"] = $"${workspaceRootVariable}"
            },
            ["hooks"] = new Dictionary<string, object?>
            {
                ["timeout_ms"] = 0
            },
            ["agent"] = new Dictionary<string, object?>
            {
                ["max_concurrent_agents_by_state"] = new Dictionary<string, object?>
                {
                    ["Todo"] = 2,
                    ["In Progress"] = 3,
                    ["Ignored"] = 0,
                    ["Broken"] = "oops"
                }
            },
            ["codex"] = new Dictionary<string, object?>
            {
                ["approval_policy"] = "on-request",
                ["thread_sandbox"] = "workspace-write",
                ["turn_sandbox_policy"] = "read-only",
                ["stall_timeout_ms"] = 0
            }
        });

        var options = _resolver.Resolve(definition);

        Assert.Equal("resolved-api-key", options.Tracker.ApiKey);
        Assert.Equal(Path.GetFullPath(workspaceRoot), options.Workspace.Root);
        Assert.Equal(60_000, options.Hooks.TimeoutMs);
        Assert.Equal(2, options.Agent.MaxConcurrentAgentsByState["todo"]);
        Assert.Equal(3, options.Agent.MaxConcurrentAgentsByState["in progress"]);
        Assert.DoesNotContain("ignored", options.Agent.MaxConcurrentAgentsByState.Keys);
        Assert.DoesNotContain("broken", options.Agent.MaxConcurrentAgentsByState.Keys);
        Assert.Equal("on-request", options.Codex.ApprovalPolicy);
        Assert.Equal("workspace-write", options.Codex.ThreadSandbox);
        Assert.Equal("read-only", options.Codex.TurnSandboxPolicy);
        Assert.Equal(0, options.Codex.StallTimeoutMs);
    }

    [Fact]
    public void Resolve_rejects_missing_repository_for_github_tracker()
    {
        var definition = CreateDefinition(new Dictionary<string, object?>
        {
            ["tracker"] = new Dictionary<string, object?>
            {
                ["kind"] = "github",
                ["api_key"] = "gh-token"
            }
        });

        var exception = Assert.Throws<WorkflowConfigurationException>(() => _resolver.Resolve(definition));

        Assert.Equal("missing_tracker_repository", exception.Code);
    }

    [Fact]
    public void Resolve_rejects_empty_api_key_environment_reference()
    {
        const string apiKeyVariable = "SYMPHONY_EMPTY_API_KEY";
        using var apiKeyScope = new EnvironmentVariableScope(apiKeyVariable, null);

        var definition = CreateDefinition(new Dictionary<string, object?>
        {
            ["tracker"] = new Dictionary<string, object?>
            {
                ["kind"] = "github",
                ["api_key"] = $"${apiKeyVariable}",
                ["repository"] = "AGiorgetti/my-symphony"
            }
        });

        var exception = Assert.Throws<WorkflowConfigurationException>(() => _resolver.Resolve(definition));

        Assert.Equal("missing_tracker_api_key", exception.Code);
    }

    [Fact]
    public void Resolve_rejects_overlapping_active_and_terminal_states()
    {
        var definition = CreateDefinition(new Dictionary<string, object?>
        {
            ["tracker"] = new Dictionary<string, object?>
            {
                ["kind"] = "github",
                ["api_key"] = "gh-token",
                ["repository"] = "AGiorgetti/my-symphony",
                ["active_states"] = new object[] { "Todo", "Done" },
                ["terminal_states"] = new object[] { "Done" }
            }
        });

        var exception = Assert.Throws<WorkflowConfigurationException>(() => _resolver.Resolve(definition));

        Assert.Equal("invalid_workflow_config", exception.Code);
        Assert.Contains("cannot overlap", exception.Message);
    }

    [Fact]
    public void Resolve_rejects_non_positive_agent_concurrency_limit()
    {
        var definition = CreateDefinition(new Dictionary<string, object?>
        {
            ["tracker"] = new Dictionary<string, object?>
            {
                ["kind"] = "github",
                ["api_key"] = "gh-token",
                ["repository"] = "AGiorgetti/my-symphony"
            },
            ["agent"] = new Dictionary<string, object?>
            {
                ["max_concurrent_agents"] = 0
            }
        });

        var exception = Assert.Throws<WorkflowConfigurationException>(() => _resolver.Resolve(definition));

        Assert.Equal("invalid_workflow_config", exception.Code);
        Assert.Contains("agent.max_concurrent_agents", exception.Message);
    }

    private static WorkflowDefinition CreateDefinition(IReadOnlyDictionary<string, object?> config)
    {
        return new WorkflowDefinition(config, "Prompt body");
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _variableName;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string variableName, string? value)
        {
            _variableName = variableName;
            _originalValue = Environment.GetEnvironmentVariable(variableName);
            Environment.SetEnvironmentVariable(variableName, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_variableName, _originalValue);
        }
    }
}
