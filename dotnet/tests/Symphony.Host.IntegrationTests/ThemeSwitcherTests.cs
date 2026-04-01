using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using MudBlazor;
using Symphony.Abstractions.Orchestration;
using Symphony.Host.Components.Shell;
using Symphony.Host.Components.Layout;
using Symphony.Host.Dashboard;
using Symphony.Host.Theming;

namespace Symphony.Host.IntegrationTests;

public sealed class ThemeSwitcherTests : BunitContext
{
    public ThemeSwitcherTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IKeyInterceptorService, TestKeyInterceptorService>();
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
        var dropdownItems = cut.FindAll("[data-testid=\"theme-switcher\"] button")
            .Skip(1)
            .ToArray();

        Assert.Equal(3, dropdownItems.Length);

        await cut.InvokeAsync(() => dropdownItems[1].Click());

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

    [Fact]
    public void MainLayout_initializes_theme_service_in_the_same_circuit_as_the_theme_switcher()
    {
        var themeService = new TestThemeService
        {
            StoredTheme = "light-blue"
        };

        Services.AddSingleton<IThemeService>(themeService);
        Services.AddDashboardPageDataServices(new StaticDashboardStateService());
        Services.AddSingleton<IOrchestratorControl>(new TestOrchestratorControl());

        var cut = Render<MainLayout>(
            parameters => parameters.Add(
                component => component.Body,
                (RenderFragment)(builder => builder.AddMarkupContent(0, "<div>Body</div>"))));

        cut.WaitForAssertion(() =>
            Assert.Equal("Light Blue", cut.Find("[data-testid=\"theme-switcher\"] button").TextContent.Trim()));
    }

    [Fact]
    public async Task MainLayout_start_and_stop_controls_call_orchestrator_control()
    {
        var themeService = new TestThemeService();
        var orchestratorControl = new TestOrchestratorControl();
        var dashboardStateService = new MutableDashboardStateService(() => orchestratorControl.State);

        Services.AddSingleton<IThemeService>(themeService);
        Services.AddDashboardPageDataServices(dashboardStateService);
        Services.AddSingleton<IOrchestratorControl>(orchestratorControl);

        var cut = Render<MainLayout>(
            parameters => parameters.Add(
                component => component.Body,
                (RenderFragment)(builder => builder.AddMarkupContent(0, "<div>Body</div>"))));

        await cut.InvokeAsync(() => cut.Find("[data-testid=\"orchestrator-start-button\"]").Click());
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"orchestrator-stop-button\"]").Click());

        Assert.Equal(1, orchestratorControl.ResumeCalls);
        Assert.Equal(1, orchestratorControl.PauseCalls);
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

        public string? StoredTheme { get; init; }

        public List<string> SelectedThemes { get; } = [];

        public Task InitializeAsync()
        {
            if (!string.IsNullOrWhiteSpace(StoredTheme))
            {
                ChangeTheme(StoredTheme);
            }

            return Task.CompletedTask;
        }

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

    private sealed class StaticDashboardStateService(
        OrchestratorControlState orchestratorState = OrchestratorControlState.Started) : IDashboardStateService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new DashboardSnapshot(
                    DateTimeOffset.UtcNow,
                    "Healthy",
                    "Single-process in-memory",
                    orchestratorState,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    0d,
                    "Loaded",
                    DateTimeOffset.UtcNow,
                    RunningCount: 0,
                    RetryingCount: 0,
                    InputTokens: 0,
                    OutputTokens: 0,
                    TotalTokens: 0,
                    SecondsRunning: 0d,
                    ActiveSessions: [],
                    RetryQueue: [],
                    RecentAttempts: [],
                    LastError: null,
                    WorkflowLastError: null));
        }
    }

    private sealed class TestOrchestratorControl : IOrchestratorControl
    {
        public int PauseCalls { get; private set; }

        public int ResumeCalls { get; private set; }

        public OrchestratorControlState State { get; private set; } = OrchestratorControlState.Stopped;

        public Task RequestRefreshAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            PauseCalls++;
            State = OrchestratorControlState.Stopped;
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            ResumeCalls++;
            State = OrchestratorControlState.Started;
            return Task.CompletedTask;
        }
    }

    private sealed class MutableDashboardStateService(Func<OrchestratorControlState> getState) : IDashboardStateService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new DashboardSnapshot(
                    DateTimeOffset.UtcNow,
                    "Healthy",
                    "Single-process in-memory",
                    getState(),
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    0d,
                    "Loaded",
                    DateTimeOffset.UtcNow,
                    RunningCount: 0,
                    RetryingCount: 0,
                    InputTokens: 0,
                    OutputTokens: 0,
                    TotalTokens: 0,
                    SecondsRunning: 0d,
                    ActiveSessions: [],
                    RetryQueue: [],
                    RecentAttempts: [],
                    LastError: null,
                    WorkflowLastError: null));
        }
    }
}
