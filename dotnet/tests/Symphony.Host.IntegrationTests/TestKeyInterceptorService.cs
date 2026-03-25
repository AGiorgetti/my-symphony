using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using MudBlazor.Services;

namespace Symphony.Host.IntegrationTests;

internal sealed class TestKeyInterceptorService : IKeyInterceptorService, IDisposable
{
    public Task SubscribeAsync(IKeyInterceptorObserver observer, KeyInterceptorOptions options)
    {
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(string elementId, KeyInterceptorOptions options, IKeyDownObserver? keyDown = null, IKeyUpObserver? keyUp = null)
    {
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(string elementId, KeyInterceptorOptions options, Action<KeyboardEventArgs>? keyDown = null, Action<KeyboardEventArgs>? keyUp = null)
    {
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(string elementId, KeyInterceptorOptions options, Func<KeyboardEventArgs, Task>? keyDown = null, Func<KeyboardEventArgs, Task>? keyUp = null)
    {
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(string elementId, KeyInterceptorOptions options, Action<KeyMapBuilder> configure)
    {
        return Task.CompletedTask;
    }

    public Task UpdateKeyAsync(IKeyInterceptorObserver observer, KeyOptions option)
    {
        return Task.CompletedTask;
    }

    public Task UpdateKeyAsync(string elementId, KeyOptions option)
    {
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(IKeyInterceptorObserver observer)
    {
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string elementId)
    {
        return Task.CompletedTask;
    }

    public Task DispatchAsync(string elementId, KeyEventKind keyEventKind, KeyboardEventArgs args)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
