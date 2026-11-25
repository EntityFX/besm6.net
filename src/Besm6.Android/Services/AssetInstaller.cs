using global::Android.Content;
using global::Android.Content.PM;
using global::Android.Content.Res;

namespace Besm6.Android.Services;

/// <summary>Extracts packaged tapes and .dub examples into app-private storage.</summary>
public sealed class AssetInstaller
{
    private const string AssetRevision = "1";

    private readonly Context _context;
    private readonly AssetManager _assets;

    public AssetInstaller(Context context)
    {
        _context = context;
        _assets = context.Assets!;
        RootDirectory = Path.Combine(context.FilesDir!.AbsolutePath, "besm6");
        TapesDirectory = Path.Combine(RootDirectory, "tapes");
        ExamplesDirectory = Path.Combine(RootDirectory, "examples");
        WorkDirectory = Path.Combine(RootDirectory, "work");
    }

    public string RootDirectory { get; }
    public string TapesDirectory { get; }
    public string ExamplesDirectory { get; }
    public string WorkDirectory { get; }

    public void EnsureInstalled()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(TapesDirectory);
        Directory.CreateDirectory(ExamplesDirectory);
        Directory.CreateDirectory(WorkDirectory);

        string markerPath = Path.Combine(RootDirectory, "assets.version");
        string expected = $"{AssetRevision}:{PackageVersionCode()}";
        if (File.Exists(markerPath) && string.Equals(File.ReadAllText(markerPath).Trim(), expected, StringComparison.Ordinal)
            && Directory.EnumerateFiles(TapesDirectory).Any()
            && Directory.EnumerateFiles(ExamplesDirectory, "*.dub", SearchOption.AllDirectories).Any())
            return;

        CopyAssetTree("tapes", TapesDirectory);
        CopyAssetTree("examples", ExamplesDirectory);
        File.WriteAllText(markerPath, expected);
    }

    private long PackageVersionCode()
    {
        try
        {
            var info = _context.PackageManager!.GetPackageInfo(
                _context.PackageName!, PackageInfoFlags.MatchDefaultOnly);
            return info?.LongVersionCode ?? 0;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return 0;
        }
    }

    private void CopyAssetTree(string assetPath, string destination)
    {
        string[] children;
        try
        {
            children = _assets.List(assetPath) ?? [];
        }
        catch (IOException)
        {
            children = [];
        }

        if (children.Length == 0)
        {
            CopyAssetFile(assetPath, destination);
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (string child in children)
            CopyAssetTree(assetPath + "/" + child, Path.Combine(destination, child));
    }

    private void CopyAssetFile(string assetPath, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using Stream source = _assets.Open(assetPath);
        using FileStream target = File.Create(destination);
        source.CopyTo(target);
    }
}
