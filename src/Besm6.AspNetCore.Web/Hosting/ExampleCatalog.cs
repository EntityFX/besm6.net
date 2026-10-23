namespace Besm6.AspNetCore.Web.Hosting;

public sealed record ExampleEntry(string Id, string Name, string Group);

public sealed class ExampleCatalog
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    public IReadOnlyList<ExampleEntry> Entries { get; }

    public ExampleCatalog(string? root = null)
    {
        root = Path.GetFullPath(root ?? Path.Combine(AppContext.BaseDirectory, "examples"));
        var entries = new List<ExampleEntry>();
        if (Directory.Exists(root))
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => Path.GetExtension(file) is ".dub" or ".json").OrderBy(x => x, StringComparer.Ordinal))
            {
                if (new FileInfo(file).Length > 10_000_000) continue;
                string id = Path.GetRelativePath(root, file).Replace('\\', '/');
                _files.Add(id, file);
                entries.Add(new(id, Path.GetFileName(file), id.Contains('/') ? id[..id.LastIndexOf('/')] : "Examples"));
            }
        Entries = entries;
    }

    public bool TryGetPath(string id, out string path) => _files.TryGetValue(id, out path!);
}
