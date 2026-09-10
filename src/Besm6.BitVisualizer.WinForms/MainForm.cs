using Besm6.BitVisualizer;
using Besm6.BitVisualizer.WinForms.Controls;
using Besm6.BitVisualizer.WinForms.Pages;

namespace Besm6.BitVisualizer.WinForms;

/// <summary>Главное окно: пять рабочих вкладок сверху и постоянно видимый общий журнал снизу.</summary>
public sealed class MainForm : Form
{
    private readonly SplitContainer _split = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        Panel1MinSize = 220,
        Panel2MinSize = 140,
    };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly SharedLogControl _log = new() { Dock = DockStyle.Fill };

    public MainForm()
    {
        Text = "Визуализатор чисел и команд БЭСМ-6";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        ClientSize = new Size(1280, 800);

        CalculationPages =
        [
            new Besm6FloatPage(),
            new IntegerPage(),
            new Ieee754Page(),
            new InstructionPage(),
            new RadixConverterPage(),
        ];
        foreach (CalculationPageBase page in CalculationPages)
        {
            page.Dock = DockStyle.Fill;
            page.ReportCreated += (_, report) => Publish(report);
            page.ErrorCreated += (_, error) => PublishError(page.PageName, error.Input, error.Message);
        }

        _tabs.TabPages.Add(new TabPage("Число БЭСМ-6") { Controls = { CalculationPages[0] } });
        _tabs.TabPages.Add(new TabPage("Целые числа") { Controls = { CalculationPages[1] } });
        _tabs.TabPages.Add(new TabPage("IEEE-754") { Controls = { CalculationPages[2] } });
        _tabs.TabPages.Add(new TabPage("Команда БЭСМ-6") { Controls = { CalculationPages[3] } });
        _tabs.TabPages.Add(new TabPage("Системы счисления") { Controls = { CalculationPages[4] } });

        _split.Panel1.Controls.Add(_tabs);
        _split.Panel2.Controls.Add(_log);
        Controls.Add(_split);

        Shown += (_, _) =>
        {
            int logHeight = Math.Max(220, _split.Panel2MinSize);
            _split.SplitterDistance = Math.Max(_split.Panel1MinSize, _split.Height - logHeight);
        };
    }

    /// <summary>Рабочие вкладки в порядке слева направо.</summary>
    public IReadOnlyList<CalculationPageBase> CalculationPages { get; }

    /// <summary>Имена вкладок в порядке слева направо (для тестов).</summary>
    public string[] TabNames => _tabs.TabPages.Cast<TabPage>().Select(page => page.Text).ToArray();

    /// <summary>Общий журнал внизу окна.</summary>
    public SharedLogControl SharedLog => _log;

    /// <summary>Число блоков в общем журнале (для тестов).</summary>
    public int LogEntryCount => _log.EntryCount;

    /// <summary>Добавляет отчёт вычисления в общий журнал с текущим временем.</summary>
    public void Publish(CalculationReport report)
        => _log.Append(report, DateTime.Now);

    /// <summary>Добавляет блок ошибки ввода в общий журнал с текущим временем.</summary>
    public void PublishError(string category, string input, string message)
        => _log.AppendError(category, input, message, DateTime.Now);
}