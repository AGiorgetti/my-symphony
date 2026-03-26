namespace Symphony.Infrastructure.Tests;

internal static class CurrentDirectoryTestScope
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static Task WaitAsync()
    {
        return Gate.WaitAsync();
    }

    public static void Release()
    {
        Gate.Release();
    }
}
