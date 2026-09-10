using System.Numerics;
using Besm6.Architecture;
using Besm6.BitVisualizer;
using Besm6.BitVisualizer.WinForms;
using Besm6.BitVisualizer.WinForms.Pages;

namespace Besm6.BitVisualizer.WinForms.Tests;

[TestClass]
public sealed class PageContractTests
{
    [STATestMethod]
    public void MainForm_ContainsFiveFunctionalTabsAndOneSharedLog()
    {
        using var form = new MainForm();

        CollectionAssert.AreEqual(
            new[] { "Число БЭСМ-6", "Целые числа", "IEEE-754", "Команда БЭСМ-6", "Системы счисления" },
            form.TabNames);
        Assert.IsNotNull(form.SharedLog);
        Assert.AreEqual(5, form.CalculationPages.Count);
    }

    [STATestMethod]
    public void IntegerPage_SignedCheckBoxChangesResultAndSignColoring()
    {
        using var page = new IntegerPage();
        page.SetWidth(8);
        page.SetBits(new BigInteger(0xFF));
        page.Signed = false;
        page.Calculate();
        Assert.AreEqual("255", page.ResultText);

        page.Signed = true;
        page.Calculate();
        Assert.AreEqual("-1", page.ResultText);
        Assert.AreEqual(BitFieldKind.Sign, page.MostSignificantBitKind);
    }

    [STATestMethod]
    public void EachPage_PublishesOneReportForOneCalculation()
    {
        using var form = new MainForm();
        foreach (CalculationPageBase page in form.CalculationPages)
        {
            int before = form.LogEntryCount;
            page.RunDefaultCalculation();
            Assert.AreEqual(before + 1, form.LogEntryCount, page.PageName);
        }
    }

    [STATestMethod]
    public void RadixPage_InvalidInputRaisesErrorAndKeepsInput()
    {
        using var page = new RadixConverterPage();
        List<PageErrorEventArgs> errors = [];
        page.ErrorCreated += (_, error) => errors.Add(error);

        page.SetInput("102");
        page.SetBases(2, 10);
        page.Convert();

        Assert.AreEqual(1, errors.Count);
        StringAssert.Contains(errors[0].Message, "2");
        Assert.AreEqual("102", page.InputText);
        StringAssert.Contains(page.ErrorText, "2");
    }

    [STATestMethod]
    public void RadixPage_ValidConversionShowsResultAndPublishesReport()
    {
        using var page = new RadixConverterPage();
        List<CalculationReport> reports = [];
        page.ReportCreated += (_, report) => reports.Add(report);

        page.SetInput("7FFF");
        page.SetBases(16, 10);
        page.Convert();

        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual("32767", page.ResultText);
        Assert.AreEqual(string.Empty, page.ErrorText);
    }

    [STATestMethod]
    public void Besm6FloatPage_CalculatesFromBits()
    {
        using var page = new Besm6FloatPage();
        page.SetBits(Word48.FromDouble(1.0).Value);
        page.Calculate();

        StringAssert.Contains(page.ResultText, "1");
        Assert.AreEqual(48, page.BitCount);
    }

    [STATestMethod]
    public void InstructionPage_FormatBitSwitchesLayoutInstantly()
    {
        using var page = new InstructionPage();

        Assert.AreEqual(false, page.FormatBit);
        Assert.IsTrue(page.HasShortLayout);

        page.SetFormatBit(true);
        Assert.AreEqual(true, page.FormatBit);
        Assert.IsFalse(page.HasShortLayout);

        page.SetFormatBit(false);
        Assert.IsTrue(page.HasShortLayout);
    }

    [STATestMethod]
    public void TallContent_StaysReachableThroughScrolling()
    {
        using var viewport = new Panel { Size = new Size(500, 240) };
        using var page = new Besm6FloatPage();
        viewport.Controls.Add(page);
        page.PerformLayout();
        viewport.PerformLayout();

        Panel? host = FindAutoScrollHost(page);
        Assert.IsNotNull(host, "страница должна содержать прокручиваемый контейнер");

        host!.PerformLayout();
        Assert.IsTrue(
            host.VerticalScroll.Visible || host.AutoScrollMinSize.Height > host.ClientSize.Height,
            "контент выше вкладки должен давать прокрутку, а не обрезание результата");
    }

    private static Panel? FindAutoScrollHost(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Panel { AutoScroll: true } panel)
            {
                return panel;
            }

            if (child is Panel nested && FindAutoScrollHost(nested) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}