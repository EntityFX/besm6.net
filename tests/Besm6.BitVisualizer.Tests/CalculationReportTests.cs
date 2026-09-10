using Besm6.BitVisualizer;

namespace Besm6.BitVisualizer.Tests;

[TestClass]
public sealed class CalculationReportTests
{
    [TestMethod]
    public void Render_ProducesReadableNumberedExplanation()
    {
        var report = new CalculationReport(
            "Проверка",
            "101₂",
            "5₁₀",
            ["Берём старший бит.", "Складываем веса."]);

        string rendered = report.Render();

        StringAssert.StartsWith(rendered, "Проверка");
        StringAssert.Contains(rendered, "Вход: 101₂");
        StringAssert.Contains(rendered, "1. Берём старший бит.");
        StringAssert.Contains(rendered, "2. Складываем веса.");
        StringAssert.EndsWith(rendered, "Результат: 5₁₀");
    }

    [TestMethod]
    public void Constructor_SnapshotsSteps()
    {
        var steps = new List<string> { "Первый шаг" };
        var report = new CalculationReport("Операция", "0", "0", steps);

        steps.Add("Не должен появиться");

        Assert.HasCount(1, report.Steps);
    }
}
