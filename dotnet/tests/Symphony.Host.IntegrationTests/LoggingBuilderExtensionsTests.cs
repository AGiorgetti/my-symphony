using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Symphony.Host.Composition;

namespace Symphony.Host.IntegrationTests;

public sealed class LoggingBuilderExtensionsTests
{
    [Fact]
    public void AddSymphonyLogging_registers_json_console_and_binds_formatter_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Logging:Console:FormatterOptions:IncludeScopes"] = "true",
                    ["Logging:Console:FormatterOptions:UseUtcTimestamp"] = "true",
                    ["Logging:Console:FormatterOptions:TimestampFormat"] = "O"
                })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSymphonyLogging(configuration));

        using var serviceProvider = services.BuildServiceProvider();
        var consoleOptions = serviceProvider.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;
        var formatterOptions = serviceProvider.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>().CurrentValue;

        Assert.Equal(ConsoleFormatterNames.Json, consoleOptions.FormatterName);
        Assert.True(formatterOptions.IncludeScopes);
        Assert.True(formatterOptions.UseUtcTimestamp);
        Assert.Equal("O", formatterOptions.TimestampFormat);
    }
}
