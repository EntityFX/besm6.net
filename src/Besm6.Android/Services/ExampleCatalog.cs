namespace Besm6.Android.Services;

public sealed record ExampleEntry(string DisplayName, string RelativePath, string FullPath);

/// <summary>Builds the selectable list from the extracted examples directory.</summary>
public sealed class ExampleCatalog
{
    private readonly string _examplesDirectory;

    public ExampleCatalog(string examplesDirectory) => _examplesDirectory = examplesDirectory;

    public IReadOnlyList<ExampleEntry> Load()
    {
        if (!Directory.Exists(_examplesDirectory)) return [];
        return Directory.EnumerateFiles(_examplesDirectory, "*.dub", SearchOption.AllDirectories)
            .Select(path =>
            {
                string relative = Path.GetRelativePath(_examplesDirectory, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                string display = relative.EndsWith(".dub", StringComparison.OrdinalIgnoreCase)
                    ? relative[..^4]
                    : relative;
                return new ExampleEntry(display, relative, path);
            })
            .OrderBy(entry => entry.RelativePath.Contains('/') ? 1 : 0)
            .ThenBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static ExampleEntry? FindPreferred(IReadOnlyList<ExampleEntry> entries) =>
        entries.FirstOrDefault(entry => string.Equals(entry.RelativePath, "fortran.dub", StringComparison.OrdinalIgnoreCase))
        ?? entries.FirstOrDefault();
}
