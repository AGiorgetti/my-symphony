using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Symphony.Abstractions.Workflows;
using Symphony.Domain.Workflows;
using Symphony.Infrastructure.Workflows;

namespace Symphony.Host.Configuration;

internal static class HttpServerPortBindingExtensions
{
    public static void ApplyConfiguredHttpServerPort(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configuredPort = TryResolveCliPort(builder.Configuration);
        if (configuredPort is null)
        {
            configuredPort = TryResolveWorkflowPort(Path.Combine(Directory.GetCurrentDirectory(), "WORKFLOW.md"));
        }

        if (configuredPort is int port)
        {
            builder.WebHost.UseUrls($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static int? TryResolveCliPort(IConfiguration configuration)
    {
        var rawPort = configuration["port"];
        if (string.IsNullOrWhiteSpace(rawPort))
        {
            return null;
        }

        return ParsePort(
            rawPort,
            "invalid_cli_port",
            "--port must be an integer greater than or equal to 0.");
    }

    private static int? TryResolveWorkflowPort(string workflowPath)
    {
        if (!File.Exists(workflowPath))
        {
            return null;
        }

        WorkflowDefinition workflowDefinition;
        try
        {
            workflowDefinition = new YamlWorkflowLoader().LoadAsync(workflowPath).GetAwaiter().GetResult();
        }
        catch (MissingWorkflowFileException)
        {
            return null;
        }
        catch (WorkflowLoadException)
        {
            return null;
        }

        if (!workflowDefinition.Config.TryGetValue("server", out var rawServerSection) || rawServerSection is null)
        {
            return null;
        }

        var serverSection = rawServerSection switch
        {
            IReadOnlyDictionary<string, object?> readOnlyDictionary => readOnlyDictionary,
            IDictionary<string, object?> dictionary => new ReadOnlyDictionary<string, object?>(dictionary),
            _ => throw CreatePortConfigurationException(
                "invalid_server_port",
                $"server in '{workflowPath}' must be an object when configuring server.port.")
        };

        if (!serverSection.TryGetValue("port", out var rawPort) || rawPort is null)
        {
            return null;
        }

        return ParsePort(
            rawPort,
            "invalid_server_port",
            $"server.port in '{workflowPath}' must be an integer greater than or equal to 0.");
    }

    private static int ParsePort(object rawValue, string errorCode, string errorMessage)
    {
        if (TryConvertToInt(rawValue, out var parsedPort) && parsedPort >= 0)
        {
            return parsedPort;
        }

        throw CreatePortConfigurationException(errorCode, errorMessage);
    }

    private static bool TryConvertToInt(object rawValue, out int parsedValue)
    {
        switch (rawValue)
        {
            case byte byteValue:
                parsedValue = byteValue;
                return true;
            case sbyte sbyteValue:
                parsedValue = sbyteValue;
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
            case string stringValue when int.TryParse(stringValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedString):
                parsedValue = parsedString;
                return true;
            default:
                parsedValue = default;
                return false;
        }
    }

    private static InvalidOperationException CreatePortConfigurationException(string errorCode, string detail)
    {
        return new InvalidOperationException(
            $"Failed to configure HTTP server port ({errorCode}). {detail}");
    }
}
