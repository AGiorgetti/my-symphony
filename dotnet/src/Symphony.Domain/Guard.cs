namespace Symphony.Domain;

internal static class Guard
{
    public static string Required(string? value, string paramName)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed)
            ? trimmed
            : throw new ArgumentException("Value cannot be null or whitespace.", paramName);
    }

    public static string? Optional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    public static int NonNegative(int value, string paramName)
    {
        return value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(paramName, value, "Value must be non-negative.");
    }
}
