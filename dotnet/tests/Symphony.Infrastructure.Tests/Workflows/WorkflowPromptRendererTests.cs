using Symphony.Domain.Issues;
using Symphony.Domain.Workflows;
using Symphony.Infrastructure.Workflows;

namespace Symphony.Infrastructure.Tests.Workflows;

public sealed class WorkflowPromptRendererTests
{
    private readonly WorkflowPromptRenderer _renderer = new();

    [Fact]
    public void Render_supports_strict_interpolation_and_if_else_blocks()
    {
        var definition = new WorkflowDefinition(
            config: null,
            promptTemplate: """
                Issue {{ issue.identifier }}
                {% if attempt %}
                Attempt {{ attempt }}
                {% endif %}
                {% if issue.description %}
                {{ issue.description }}
                {% else %}
                No description provided.
                {% endif %}
                Labels: {{ issue.labels }}
                """);

        var result = _renderer.Render(
            definition,
            new Issue(
                id: "1",
                identifier: "#42",
                title: "Runner test",
                description: "Implement the agent runner",
                state: "Todo",
                labels: ["priority:p0", "status:in-progress"]),
            attempt: 2);

        Assert.Contains("Issue #42", result, StringComparison.Ordinal);
        Assert.Contains("Attempt 2", result, StringComparison.Ordinal);
        Assert.Contains("Implement the agent runner", result, StringComparison.Ordinal);
        Assert.Contains("priority:p0, status:in-progress", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderTurn_uses_continuation_guidance_after_first_turn()
    {
        var definition = new WorkflowDefinition(
            config: null,
            promptTemplate: "Original prompt for {{ issue.identifier }}");

        var result = _renderer.RenderTurn(
            definition,
            CreateIssue(),
            attempt: 2,
            turnNumber: 2,
            maxTurns: 5);

        Assert.Contains("existing Codex thread", result, StringComparison.Ordinal);
        Assert.Contains("#1", result, StringComparison.Ordinal);
        Assert.Contains("continuation turn 2 of 5", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Original prompt", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_throws_for_unknown_variables()
    {
        var definition = new WorkflowDefinition(
            config: null,
            promptTemplate: "Hello {{ issue.unknown_field }}");

        var exception = Assert.Throws<WorkflowPromptRenderer.WorkflowPromptException>(
            () => _renderer.Render(definition, CreateIssue(), attempt: null));

        Assert.Equal("template_render_error", exception.Code);
    }

    [Fact]
    public void Render_throws_for_unterminated_if_block()
    {
        var definition = new WorkflowDefinition(
            config: null,
            promptTemplate: "{% if issue.description %}broken");

        var exception = Assert.Throws<WorkflowPromptRenderer.WorkflowPromptException>(
            () => _renderer.Render(definition, CreateIssue(), attempt: null));

        Assert.Equal("template_parse_error", exception.Code);
    }

    private static Issue CreateIssue()
    {
        return new Issue(
            id: "1",
            identifier: "#1",
            title: "Title",
            description: "Description",
            state: "Todo");
    }
}
