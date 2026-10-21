using Besm6.Runtime.Storage;

namespace Besm6.AspNetCore.Web.Hosting;

public sealed record MediaEntry(string Id, string Name, string Volume, int Words);

/// <summary>Only known runtime assets are exposed, never caller-supplied host paths.</summary>
public static class MediaCatalog
{
    private static readonly (string Name, long Volume)[] Known =
    [ ("monsys.9", TapeImage.TapeMonsys), ("librar.12", TapeImage.TapeLibrar12),
      ("librar.37", TapeImage.TapeLibrar37), ("bemsh.739", TapeImage.TapeBemsh), ("b.7", TapeImage.TapeB) ];

    public static IEnumerable<MediaEntry> Entries => Known.Select(item => (item, path: Path.Combine(AppContext.BaseDirectory, "tapes", item.Name)))
        .Where(x => File.Exists(x.path))
        .Select(x => new MediaEntry("builtin:" + x.item.Name, x.item.Name, x.item.Volume.ToString("X12"), checked((int)(new FileInfo(x.path).Length / 6))));

    public static bool TryGet(string id, out string path, out long volume)
    {
        foreach (var item in Known)
            if (id == "builtin:" + item.Name)
            {
                path = Path.Combine(AppContext.BaseDirectory, "tapes", item.Name);
                volume = item.Volume;
                return File.Exists(path);
            }
        path = ""; volume = 0; return false;
    }
}
