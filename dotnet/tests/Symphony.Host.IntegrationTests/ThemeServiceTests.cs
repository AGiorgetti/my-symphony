using System.Collections.Concurrent;
using Microsoft.JSInterop;
using Symphony.Host.Theming;

namespace Symphony.Host.IntegrationTests;

public sealed class ThemeServiceTests
{
    [Theory]
    [InlineData("dark-yellow", true)]
    [InlineData("dark-blue", true)]
    [InlineData("light-blue", false)]
    public async Task SetThemeAsync_applies_each_built_in_theme_and_raises_change_event(string themeKey, bool isDark)
    {
        var jsRuntime = new RecordingJsRuntime();
        var service = new ThemeService(jsRuntime);
        var changeNotifications = 0;
        service.OnThemeChanged += () => changeNotifications++;

        await service.SetThemeAsync(themeKey);

        Assert.Equal(themeKey, service.CurrentTheme);
        Assert.Equal(1, changeNotifications);
        Assert.Equal(3, jsRuntime.Invocations.Count);
        Assert.Equal("symphonyTheme.setDarkClass", jsRuntime.Invocations[0].Identifier);
        Assert.Equal(isDark, Assert.Single(jsRuntime.Invocations[0].Arguments));
        Assert.Equal("symphonyTheme.setThemeAttribute", jsRuntime.Invocations[1].Identifier);
        Assert.Equal(themeKey, Assert.Single(jsRuntime.Invocations[1].Arguments));
        Assert.Equal("symphonyTheme.storeTheme", jsRuntime.Invocations[2].Identifier);
        Assert.Equal(themeKey, Assert.Single(jsRuntime.Invocations[2].Arguments));
    }

    [Fact]
    public async Task SetThemeAsync_invalid_key_is_ignored()
    {
        var jsRuntime = new RecordingJsRuntime();
        var service = new ThemeService(jsRuntime);
        var changeNotifications = 0;
        service.OnThemeChanged += () => changeNotifications++;

        await service.SetThemeAsync("unknown-theme");

        Assert.Equal("dark-yellow", service.CurrentTheme);
        Assert.Equal(0, changeNotifications);
        Assert.Empty(jsRuntime.Invocations);
    }

    [Fact]
    public async Task InitializeAsync_loads_valid_stored_theme_without_persisting_it_again()
    {
        var jsRuntime = new RecordingJsRuntime
        {
            Results =
            {
                ["symphonyTheme.getStoredTheme"] = "light-blue"
            }
        };
        var service = new ThemeService(jsRuntime);

        await service.InitializeAsync();

        Assert.Equal("light-blue", service.CurrentTheme);
        Assert.Collection(
            jsRuntime.Invocations,
            invocation => Assert.Equal("symphonyTheme.getStoredTheme", invocation.Identifier),
            invocation => Assert.Equal("symphonyTheme.setDarkClass", invocation.Identifier),
            invocation => Assert.Equal("symphonyTheme.setThemeAttribute", invocation.Identifier));
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<JsInvocation> Invocations { get; } = [];

        public ConcurrentDictionary<string, object?> Results { get; } = new(StringComparer.Ordinal);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Invocations.Add(new JsInvocation(identifier, args ?? []));

            if (Results.TryGetValue(identifier, out var value))
            {
                return ValueTask.FromResult((TValue?)value)!;
            }

            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private sealed record JsInvocation(string Identifier, IReadOnlyList<object?> Arguments);
}
