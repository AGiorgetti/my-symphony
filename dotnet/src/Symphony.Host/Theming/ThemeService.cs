using Microsoft.JSInterop;

namespace Symphony.Host.Theming;

public sealed class ThemeService(IJSRuntime jsRuntime) : IThemeService
{
    private static readonly ThemeDescriptor[] BuiltInThemes =
    [
        new("dark-yellow", "Dark Yellow", true),
        new("dark-blue", "Dark Blue", true),
        new("light-blue", "Light Blue", false)
    ];

    private readonly Dictionary<string, ThemeDescriptor> _themesByKey = BuiltInThemes
        .ToDictionary(theme => theme.Key, StringComparer.OrdinalIgnoreCase);

    private bool _initialized;

    public string CurrentTheme { get; private set; } = "dark-yellow";

    public IReadOnlyList<ThemeDescriptor> AvailableThemes => BuiltInThemes;

    public event Action? OnThemeChanged;

    public async Task SetThemeAsync(string key)
    {
        if (!_themesByKey.TryGetValue(key, out var theme))
        {
            return;
        }

        await ApplyThemeAsync(theme, persist: true).ConfigureAwait(false);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        var storedTheme = await jsRuntime.InvokeAsync<string?>("symphonyTheme.getStoredTheme").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(storedTheme) || !_themesByKey.TryGetValue(storedTheme, out var theme))
        {
            return;
        }

        await ApplyThemeAsync(theme, persist: false).ConfigureAwait(false);
    }

    private async Task ApplyThemeAsync(ThemeDescriptor theme, bool persist)
    {
        CurrentTheme = theme.Key;

        await jsRuntime.InvokeVoidAsync("symphonyTheme.setDarkClass", theme.IsDark).ConfigureAwait(false);
        await jsRuntime.InvokeVoidAsync("symphonyTheme.setThemeAttribute", theme.Key).ConfigureAwait(false);

        if (persist)
        {
            await jsRuntime.InvokeVoidAsync("symphonyTheme.storeTheme", theme.Key).ConfigureAwait(false);
        }

        OnThemeChanged?.Invoke();
    }
}
