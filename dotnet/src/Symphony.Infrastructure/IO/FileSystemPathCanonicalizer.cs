namespace Symphony.Infrastructure.IO;

internal static class FileSystemPathCanonicalizer
{
    public static string Canonicalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows())
        {
            return fullPath;
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            return fullPath;
        }

        var canonicalPath = NormalizeWindowsRoot(root);
        var remainingPath = fullPath[root.Length..];
        if (remainingPath.Length == 0)
        {
            return canonicalPath;
        }

        foreach (var segment in remainingPath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var canonicalSegment = TryGetCanonicalChildName(canonicalPath, segment) ?? segment;
            canonicalPath = Path.Combine(canonicalPath, canonicalSegment);
        }

        return canonicalPath;
    }

    private static string NormalizeWindowsRoot(string root)
    {
        if (root.Length >= 2 && root[1] == ':')
        {
            return char.ToUpperInvariant(root[0]) + root[1..];
        }

        return root;
    }

    private static string? TryGetCanonicalChildName(string parentPath, string childName)
    {
        if (!Directory.Exists(parentPath))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFileSystemEntries(parentPath, childName)
                .Select(Path.GetFileName)
                .FirstOrDefault(static name => !string.IsNullOrEmpty(name));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
