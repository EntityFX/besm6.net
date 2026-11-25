namespace Besm6.Android.Services;

public sealed record SearchHit(string RelativePath, string Snippet, string FullPath);

/// <summary>Filename-and-content search over the extracted .dub examples.</summary>
public sealed class CodeSearchService
{
    private const int ResultLimit = 200;
    private const int MaxSnippetLength = 180;

    private readonly string _root;

    public CodeSearchService(string examplesDirectory) => _root = examplesDirectory;

    public IReadOnlyList<SearchHit> Search(string query)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0 || !Directory.Exists(_root)) return [];

        var results = new List<SearchHit>();
        foreach (string path in Directory.EnumerateFiles(_root, "*.dub", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/');
            bool nameMatch = relative.Contains(query, StringComparison.OrdinalIgnoreCase);
            string? snippet = nameMatch ? "совпадение в имени файла" : FindSnippet(path, query);
            if (!nameMatch && snippet is null) continue;
            results.Add(new SearchHit(relative, snippet ?? string.Empty, path));
            if (results.Count >= ResultLimit) break;
        }

        return results
            .OrderByDescending(hit => hit.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(hit => hit.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FindSnippet(string path, string query)
    {
        try
        {
            using var reader = new StreamReader(path);
            int lineNumber = 0;
            while (reader.ReadLine() is { } line)
            {
                lineNumber++;
                int index = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;
                string text = line.Trim();
                if (text.Length > MaxSnippetLength)
                {
                    int start = Math.Max(0, index - 40);
                    text = "…" + text.Substring(start, Math.Min(MaxSnippetLength, text.Length - start)) + "…";
                }
                return $"{lineNumber}: {text}";
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        return null;
    }
}
