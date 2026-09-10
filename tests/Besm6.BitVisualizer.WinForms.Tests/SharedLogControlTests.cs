using Besm6.Architecture.Visualization;
using Besm6.BitVisualizer.WinForms.Controls;

namespace Besm6.BitVisualizer.WinForms.Tests;

[TestClass]
public sealed class SharedLogControlTests
{
    [STATestMethod]
    public void Append_AccumulatesLabeledReports()
    {
        using var log = new SharedLogControl();
        log.Append(new CalculationReport("Первая", "0", "0", ["Шаг"]), new DateTime(2026, 9, 10, 12, 0, 0));
        log.Append(new CalculationReport("Вторая", "1", "1", ["Шаг"]), new DateTime(2026, 9, 10, 12, 1, 0));

        StringAssert.Contains(log.Text, "[12:00:00] Первая");
        StringAssert.Contains(log.Text, "[12:01:00] Вторая");
        StringAssert.Contains(log.Text, "Вход: 0");
        StringAssert.Contains(log.Text, "Результат: 1");
        Assert.AreEqual(2, log.EntryCount);
    }

    [STATestMethod]
    public void AppendError_RecordsExplanationWithoutLosingHistory()
    {
        using var log = new SharedLogControl();
        log.Append(new CalculationReport("Первая", "0", "0", ["Шаг"]), new DateTime(2026, 9, 10, 12, 0, 0));
        log.AppendError("Целые числа", "102", "Цифра «2» недопустима при основании 2.", new DateTime(2026, 9, 10, 12, 5, 0));

        StringAssert.Contains(log.Text, "[12:05:00] Целые числа — ошибка");
        StringAssert.Contains(log.Text, "Цифра «2» недопустима при основании 2.");
        StringAssert.Contains(log.Text, "Первая");
        Assert.AreEqual(2, log.EntryCount);
    }

    [STATestMethod]
    public void ClearLog_EmptiesHistoryAndCount()
    {
        using var log = new SharedLogControl();
        log.Append(new CalculationReport("Первая", "0", "0", ["Шаг"]), DateTime.Now);

        log.ClearLog();

        Assert.AreEqual(string.Empty, log.Text);
        Assert.AreEqual(0, log.EntryCount);
    }

    [STATestMethod]
    public void ExposesCopyAndClearButtons()
    {
        using var log = new SharedLogControl();

        StringAssert.Contains(log.CopyButton.Text, "Копировать");
        StringAssert.Contains(log.ClearButton.Text, "Очистить");
    }
}