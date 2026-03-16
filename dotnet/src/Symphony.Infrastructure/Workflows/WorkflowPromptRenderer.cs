using System.Diagnostics;
using System.Globalization;
using System.Text;
using Symphony.Domain.Issues;
using Symphony.Domain.Workflows;

namespace Symphony.Infrastructure.Workflows;

internal sealed class WorkflowPromptRenderer
{
    private const string DefaultPrompt = "You are working on an issue from Linear.";

    public string Render(WorkflowDefinition workflowDefinition, Issue issue, int? attempt)
    {
        ArgumentNullException.ThrowIfNull(workflowDefinition);
        ArgumentNullException.ThrowIfNull(issue);

        var template = string.IsNullOrWhiteSpace(workflowDefinition.PromptTemplate)
            ? DefaultPrompt
            : workflowDefinition.PromptTemplate;
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["issue"] = BuildIssueModel(issue),
            ["attempt"] = attempt
        };

        var tokens = Tokenize(template);
        var index = 0;
        var (nodes, stopToken) = ParseBlock(tokens, ref index, allowElse: false);
        if (stopToken != StopToken.None)
        {
            throw WorkflowPromptException.Parse("Unexpected block terminator in workflow prompt template.");
        }

        var builder = new StringBuilder(template.Length);
        foreach (var node in nodes)
        {
            node.Render(builder, variables);
        }

        return builder.ToString().Trim();
    }

    private static IReadOnlyDictionary<string, object?> BuildIssueModel(Issue issue)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = issue.Id,
            ["identifier"] = issue.Identifier,
            ["title"] = issue.Title,
            ["description"] = issue.Description,
            ["priority"] = issue.Priority,
            ["state"] = issue.State,
            ["branch_name"] = issue.BranchName,
            ["url"] = issue.Url,
            ["labels"] = issue.Labels.ToArray(),
            ["blocked_by"] = issue.BlockedBy
                .Select(
                    blocker => (object)new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["id"] = blocker.Id,
                        ["identifier"] = blocker.Identifier,
                        ["state"] = blocker.State
                    })
                .ToArray(),
            ["created_at"] = issue.CreatedAt?.ToString("O", CultureInfo.InvariantCulture),
            ["updated_at"] = issue.UpdatedAt?.ToString("O", CultureInfo.InvariantCulture)
        };
    }

    private static IReadOnlyList<Token> Tokenize(string template)
    {
        var tokens = new List<Token>();
        var cursor = 0;

        while (cursor < template.Length)
        {
            var outputStart = template.IndexOf("{{", cursor, StringComparison.Ordinal);
            var tagStart = template.IndexOf("{%", cursor, StringComparison.Ordinal);
            var nextStart = MinPositive(outputStart, tagStart);

            if (nextStart < 0)
            {
                tokens.Add(new Token(TokenType.Text, template[cursor..], cursor));
                break;
            }

            if (nextStart > cursor)
            {
                tokens.Add(new Token(TokenType.Text, template[cursor..nextStart], cursor));
            }

            var isOutput = nextStart == outputStart;
            var closeDelimiter = isOutput ? "}}" : "%}";
            var contentStart = nextStart + 2;
            var contentEnd = template.IndexOf(closeDelimiter, contentStart, StringComparison.Ordinal);
            if (contentEnd < 0)
            {
                throw WorkflowPromptException.Parse($"Unterminated {(isOutput ? "output" : "tag")} block in workflow prompt template.");
            }

            var content = template[contentStart..contentEnd].Trim();
            tokens.Add(new Token(isOutput ? TokenType.Output : TokenType.Tag, content, nextStart));
            cursor = contentEnd + 2;
        }

        return tokens;
    }

    private static (IReadOnlyList<TemplateNode> Nodes, StopToken StopToken) ParseBlock(
        IReadOnlyList<Token> tokens,
        ref int index,
        bool allowElse)
    {
        var nodes = new List<TemplateNode>();

        while (index < tokens.Count)
        {
            var token = tokens[index++];

            switch (token.Type)
            {
                case TokenType.Text:
                    nodes.Add(new TextNode(token.Content));
                    break;
                case TokenType.Output:
                    nodes.Add(new OutputNode(token.Content));
                    break;
                case TokenType.Tag:
                    if (string.Equals(token.Content, "endif", StringComparison.Ordinal))
                    {
                        return (nodes, StopToken.EndIf);
                    }

                    if (string.Equals(token.Content, "else", StringComparison.Ordinal))
                    {
                        if (!allowElse)
                        {
                            throw WorkflowPromptException.Parse("Unexpected else block in workflow prompt template.");
                        }

                        return (nodes, StopToken.Else);
                    }

                    if (!token.Content.StartsWith("if ", StringComparison.Ordinal))
                    {
                        throw WorkflowPromptException.Parse($"Unsupported tag '{token.Content}' in workflow prompt template.");
                    }

                    var condition = token.Content[3..].Trim();
                    if (condition.Length == 0)
                    {
                        throw WorkflowPromptException.Parse("Workflow prompt if-blocks must declare a condition.");
                    }

                    var (thenNodes, stopToken) = ParseBlock(tokens, ref index, allowElse: true);
                    IReadOnlyList<TemplateNode> elseNodes = Array.Empty<TemplateNode>();

                    if (stopToken == StopToken.Else)
                    {
                        (elseNodes, stopToken) = ParseBlock(tokens, ref index, allowElse: false);
                    }

                    if (stopToken != StopToken.EndIf)
                    {
                        throw WorkflowPromptException.Parse("Workflow prompt if-blocks must end with endif.");
                    }

                    nodes.Add(new IfNode(condition, thenNodes, elseNodes));
                    break;
                default:
                    throw new UnreachableException();
            }
        }

        return (nodes, StopToken.None);
    }

    private static int MinPositive(int left, int right)
    {
        if (left < 0)
        {
            return right;
        }

        if (right < 0)
        {
            return left;
        }

        return Math.Min(left, right);
    }

    private abstract class TemplateNode
    {
        public abstract void Render(StringBuilder builder, IReadOnlyDictionary<string, object?> variables);
    }

    private sealed class TextNode(string content) : TemplateNode
    {
        public override void Render(StringBuilder builder, IReadOnlyDictionary<string, object?> variables)
        {
            builder.Append(content);
        }
    }

    private sealed class OutputNode(string expression) : TemplateNode
    {
        public override void Render(StringBuilder builder, IReadOnlyDictionary<string, object?> variables)
        {
            var value = ResolveExpression(expression, variables);
            builder.Append(FormatValue(value));
        }
    }

    private sealed class IfNode(
        string condition,
        IReadOnlyList<TemplateNode> thenNodes,
        IReadOnlyList<TemplateNode> elseNodes) : TemplateNode
    {
        public override void Render(StringBuilder builder, IReadOnlyDictionary<string, object?> variables)
        {
            var conditionValue = ResolveExpression(condition, variables);
            var selectedNodes = IsTruthy(conditionValue)
                ? thenNodes
                : elseNodes;

            foreach (var node in selectedNodes)
            {
                node.Render(builder, variables);
            }
        }
    }

    private static object? ResolveExpression(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        var normalizedExpression = expression.Trim();
        if (normalizedExpression.Length == 0)
        {
            throw WorkflowPromptException.Render("Workflow prompt expressions must not be empty.");
        }

        if (normalizedExpression.Contains('|', StringComparison.Ordinal))
        {
            throw WorkflowPromptException.Render($"Unsupported filter expression '{normalizedExpression}'.");
        }

        object? current = variables;
        foreach (var segment in normalizedExpression.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            current = current switch
            {
                IReadOnlyDictionary<string, object?> dictionary when dictionary.TryGetValue(segment, out var value) => value,
                IDictionary<string, object?> dictionary when dictionary.TryGetValue(segment, out var value) => value,
                _ => throw WorkflowPromptException.Render($"Unknown workflow prompt variable '{normalizedExpression}'.")
            };
        }

        return current;
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool boolean => boolean,
            string text => !string.IsNullOrEmpty(text),
            Array array => array.Length > 0,
            IEnumerable<object?> enumerable => enumerable.Any(),
            int integer => integer != 0,
            long integer => integer != 0,
            short integer => integer != 0,
            byte integer => integer != 0,
            sbyte integer => integer != 0,
            uint integer => integer != 0,
            ulong integer => integer != 0,
            ushort integer => integer != 0,
            float number => number != 0,
            double number => number != 0,
            decimal number => number != 0,
            _ => true
        };
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            IReadOnlyDictionary<string, object?> dictionary => string.Join(", ", dictionary.Select(pair => $"{pair.Key}={FormatValue(pair.Value)}")),
            IDictionary<string, object?> dictionary => string.Join(", ", dictionary.Select(pair => $"{pair.Key}={FormatValue(pair.Value)}")),
            IEnumerable<object?> enumerable => string.Join(", ", enumerable.Select(FormatValue)),
            Array array => string.Join(", ", array.Cast<object?>().Select(FormatValue)),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private sealed record Token(TokenType Type, string Content, int Position);

    private enum TokenType
    {
        Text,
        Output,
        Tag
    }

    private enum StopToken
    {
        None,
        Else,
        EndIf
    }

    internal sealed class WorkflowPromptException(string code, string message) : InvalidOperationException(message)
    {
        public string Code { get; } = code;

        public static WorkflowPromptException Parse(string message)
        {
            return new WorkflowPromptException("template_parse_error", message);
        }

        public static WorkflowPromptException Render(string message)
        {
            return new WorkflowPromptException("template_render_error", message);
        }
    }
}
