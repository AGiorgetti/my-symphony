using Symphony.Abstractions.Workflows;
using Symphony.Infrastructure.Workflows;

namespace Symphony.Infrastructure.Tests.Workflows;

public sealed class YamlWorkflowLoaderTests
{
    private readonly YamlWorkflowLoader _loader = new();

    [Fact]
    public async Task LoadAsync_parses_front_matter_and_trims_prompt_body()
    {
        var workflowPath = await CreateWorkflowFileAsync(
            """
            ---
            tracker:
              kind: github
              repository: AGiorgetti/my-symphony
            polling:
              interval_ms: 5000
            ---

            You are working on issue {{ issue.identifier }}.
            """);

        var definition = await _loader.LoadAsync(workflowPath);

        var tracker = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(definition.Config["tracker"]);
        Assert.Equal("github", tracker["kind"]);
        Assert.Equal("AGiorgetti/my-symphony", tracker["repository"]);

        var polling = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(definition.Config["polling"]);
        Assert.Equal(5000, Convert.ToInt32(polling["interval_ms"]));
        Assert.Equal("You are working on issue {{ issue.identifier }}.", definition.PromptTemplate);
    }

    [Fact]
    public async Task LoadAsync_without_front_matter_uses_empty_config()
    {
        var workflowPath = await CreateWorkflowFileAsync(
            """

            Plain workflow body

            """);

        var definition = await _loader.LoadAsync(workflowPath);

        Assert.Empty(definition.Config);
        Assert.Equal("Plain workflow body", definition.PromptTemplate);
    }

    [Fact]
    public async Task LoadAsync_missing_file_throws_typed_error()
    {
        var workflowPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");

        var exception = await Assert.ThrowsAsync<MissingWorkflowFileException>(() => _loader.LoadAsync(workflowPath));

        Assert.Equal(MissingWorkflowFileException.ErrorCode, exception.Code);
    }

    [Fact]
    public async Task LoadAsync_invalid_yaml_throws_parse_error()
    {
        var workflowPath = await CreateWorkflowFileAsync(
            """
            ---
            tracker:
              kind: [github
            ---
            Prompt
            """);

        var exception = await Assert.ThrowsAsync<WorkflowParseException>(() => _loader.LoadAsync(workflowPath));

        Assert.Equal(WorkflowParseException.ErrorCode, exception.Code);
    }

    [Fact]
    public async Task LoadAsync_non_map_front_matter_throws_specific_error()
    {
        var workflowPath = await CreateWorkflowFileAsync(
            """
            ---
            - tracker
            - github
            ---
            Prompt
            """);

        var exception = await Assert.ThrowsAsync<WorkflowFrontMatterNotMapException>(() => _loader.LoadAsync(workflowPath));

        Assert.Equal(WorkflowFrontMatterNotMapException.ErrorCode, exception.Code);
    }

    private static async Task<string> CreateWorkflowFileAsync(string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"symphony-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var workflowPath = Path.Combine(directory, "WORKFLOW.md");
        await File.WriteAllTextAsync(workflowPath, content);
        return workflowPath;
    }
}
