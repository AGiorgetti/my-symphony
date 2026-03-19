namespace Symphony.Host.Theming;

public interface IThemeService
{
    string CurrentTheme { get; }

    IReadOnlyList<ThemeDescriptor> AvailableThemes { get; }

    event Action? OnThemeChanged;

    Task SetThemeAsync(string key);
}
