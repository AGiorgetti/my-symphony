using System.Collections;
using System.Globalization;
using Symphony.Abstractions.Workflows;
using Symphony.Domain.Workflows;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Symphony.Infrastructure.Workflows;

public sealed class YamlWorkflowLoader : IWorkflowLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    public async Task<WorkflowDefinition> LoadAsync(string workflowPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);

        if (!File.Exists(workflowPath))
        {
            throw new MissingWorkflowFileException(workflowPath);
        }

        var content = await File.ReadAllTextAsync(workflowPath, cancellationToken).ConfigureAwait(false);
        content = RemoveUtf8Bom(content);

        if (!StartsWithFrontMatter(content))
        {
            return new WorkflowDefinition(
                config: new Dictionary<string, object?>(),
                promptTemplate: content);
        }

        var (frontMatter, promptBody) = SplitFrontMatter(content, workflowPath);
        var config = ParseFrontMatter(frontMatter, workflowPath);

        return new WorkflowDefinition(config, promptBody);
    }

    private static IReadOnlyDictionary<string, object?> ParseFrontMatter(string frontMatter, string workflowPath)
    {
        object? yamlObject;

        try
        {
            yamlObject = Deserializer.Deserialize<object?>(frontMatter);
        }
        catch (YamlException exception)
        {
            throw new WorkflowParseException(
                workflowPath,
                $"Workflow file '{workflowPath}' contains invalid YAML front matter: {exception.Message}",
                exception);
        }

        if (yamlObject is not IDictionary dictionary)
        {
            throw new WorkflowFrontMatterNotMapException(workflowPath);
        }

        return NormalizeDictionary(dictionary, workflowPath);
    }

    private static IReadOnlyDictionary<string, object?> NormalizeDictionary(IDictionary dictionary, string workflowPath)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (DictionaryEntry entry in dictionary)
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new WorkflowParseException(
                    workflowPath,
                    $"Workflow file '{workflowPath}' contains a YAML mapping entry with an empty key.");
            }

            normalized[key] = NormalizeValue(entry.Value, workflowPath);
        }

        return normalized;
    }

    private static object? NormalizeValue(object? value, string workflowPath)
    {
        return value switch
        {
            null => null,
            IDictionary dictionary => NormalizeDictionary(dictionary, workflowPath),
            IList list => list.Cast<object?>().Select(item => NormalizeValue(item, workflowPath)).ToArray(),
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal or bool
                => value,
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static (string FrontMatter, string PromptBody) SplitFrontMatter(string content, string workflowPath)
    {
        using var reader = new StringReader(content);
        _ = reader.ReadLine();

        var frontMatterLines = new List<string>();

        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                throw new WorkflowParseException(
                    workflowPath,
                    $"Workflow file '{workflowPath}' starts with YAML front matter but is missing the closing '---' delimiter.");
            }

            if (line == "---")
            {
                break;
            }

            frontMatterLines.Add(line);
        }

        return (string.Join(Environment.NewLine, frontMatterLines), reader.ReadToEnd());
    }

    private static bool StartsWithFrontMatter(string content)
    {
        return content.StartsWith("---\r\n", StringComparison.Ordinal)
            || content.StartsWith("---\n", StringComparison.Ordinal)
            || string.Equals(content, "---", StringComparison.Ordinal);
    }

    private static string RemoveUtf8Bom(string content)
    {
        return content.Length > 0 && content[0] == '\uFEFF'
            ? content[1..]
            : content;
    }
}
