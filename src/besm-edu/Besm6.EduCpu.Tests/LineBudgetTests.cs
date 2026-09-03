namespace Besm6.EduCpu.Tests;

[TestClass]
public class LineBudgetTests
{
    private const int MaxTotalLines = 1000;

    [TestMethod]
    public void MainProject_StaysUnder1000Lines()
    {
        string dir = FindMainProjectDir();
        long lines = 0;
        int files = 0;
        foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            string[] parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Contains("bin") || parts.Contains("obj"))
            {
                continue;
            }

            lines += File.ReadAllLines(file).Length;
            files++;
        }

        Assert.IsTrue(files > 0, "Не найдено ни одного .cs файла основного проекта.");
        Assert.IsTrue(lines <= MaxTotalLines,
            $"Основной проект занимает {lines} строк (лимит {MaxTotalLines}).");
    }

    private static string FindMainProjectDir()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Besm6.EduCpu", "Besm6.EduCpu.csproj");
            if (File.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "Besm6.EduCpu");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Не удалось найти каталог проекта Besm6.EduCpu.");
    }
}
