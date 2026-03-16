using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Symphony.Host.Composition;

public static class LoggingBuilderExtensions
{
    public static ILoggingBuilder AddSymphonyLogging(this ILoggingBuilder loggingBuilder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(loggingBuilder);
        ArgumentNullException.ThrowIfNull(configuration);

        loggingBuilder.ClearProviders();
        loggingBuilder.AddConfiguration(configuration.GetSection("Logging"));
        loggingBuilder.AddJsonConsole();
        loggingBuilder.Services.Configure<ConsoleLoggerOptions>(
            options => options.FormatterName = ConsoleFormatterNames.Json);

        return loggingBuilder;
    }
}
