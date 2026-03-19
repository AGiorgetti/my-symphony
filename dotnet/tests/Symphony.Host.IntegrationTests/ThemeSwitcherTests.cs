using Bunit;
using Flowbite.Components;
using Flowbite.Services;
using Microsoft.Extensions.DependencyInjection;
using Symphony.Host.Components.Shell;
using Symphony.Host.Theming;

namespace Symphony.Host.IntegrationTests;

public sealed class ThemeSwitcherTests : BunitContext
{
    public ThemeSwitcherTests()
    {
        Services.AddFlowbite();
        Services.AddSingleton<IFloatingService, TestFloatingService>();
    }

    [Fact]
    public void ThemeSwitcher_renders_current_theme_label_and_available_options()
    {
        var themeService = new TestThemeService();

        var cut = Render<ThemeSwitcher>(parameters => parameters.Add(component => component.ThemeService, themeService));

        cut.Find("[data-testid=\"theme-switcher\"] button").Click();

        Assert.Contains("Dark Yellow", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Dark Blue", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Light Blue", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThemeSwitcher_selecting_theme_calls_theme_service()
    {
        var themeService = new TestThemeService();
        var cut = Render<ThemeSwitcher>(parameters => parameters.Add(component => component.ThemeService, themeService));

        cut.Find("[data-testid=\"theme-switcher\"] button").Click();
        var dropdownItems = cut.FindComponents<DropdownItem>();

        Assert.Equal(3, dropdownItems.Count);

        await cut.InvokeAsync(() => dropdownItems[1].Instance.OnClick.InvokeAsync());

        Assert.Equal("dark-blue", Assert.Single(themeService.SelectedThemes));
        Assert.Equal("Dark Blue", cut.Find("[data-testid=\"theme-switcher\"] button").TextContent.Trim());
    }

    [Fact]
    public void ThemeSwitcher_updates_when_theme_service_raises_change_event()
    {
        var themeService = new TestThemeService();
        var cut = Render<ThemeSwitcher>(parameters => parameters.Add(component => component.ThemeService, themeService));

        themeService.ChangeTheme("light-blue");

        cut.WaitForAssertion(() =>
            Assert.Equal("Light Blue", cut.Find("[data-testid=\"theme-switcher\"] button").TextContent.Trim()));
    }

    private sealed class TestThemeService : IThemeService
    {
        private static readonly ThemeDescriptor[] Themes =
        [
            new("dark-yellow", "Dark Yellow", true),
            new("dark-blue", "Dark Blue", true),
            new("light-blue", "Light Blue", false)
        ];

        public string CurrentTheme { get; private set; } = "dark-yellow";

        public IReadOnlyList<ThemeDescriptor> AvailableThemes => Themes;

        public event Action? OnThemeChanged;

        public List<string> SelectedThemes { get; } = [];

        public Task SetThemeAsync(string key)
        {
            SelectedThemes.Add(key);
            ChangeTheme(key);
            return Task.CompletedTask;
        }

        public void ChangeTheme(string key)
        {
            CurrentTheme = key;
            OnThemeChanged?.Invoke();
        }
    }

    private sealed class TestFloatingService : IFloatingService, IDisposable
    {
        public Task<string?> InitializeAsync(string id, FloatingOptions? options = null)
        {
            return Task.FromResult<string?>("top");
        }

        public Task UpdatePositionAsync(string id)
        {
            return Task.CompletedTask;
        }

        public Task DestroyAsync(string id)
        {
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string id)
        {
            return Task.FromResult(true);
        }

        public Task<string?> GetPlacementAsync(string id)
        {
            return Task.FromResult<string?>("top");
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
