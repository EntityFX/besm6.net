using Besm6.Architecture.Visualization;
using Besm6.BitVisualizer.WinForms.Controls;

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

        _split.Panel1.Controls.Add(_tabs);
        _split.Panel2.Controls.Add(_log);
        Controls.Add(_split);

        // Временные заглушки вкладок до подключения рабочих страниц (Task 7).
        foreach (string title in TabTitles)
            _tabs.TabPages.Add(new TabPage(title));

        Shown += (_, _) =>
        {
            int logHeight = Math.Max(220, _split.Panel2MinSize);
            _split.SplitterDistance = Math.Max(_split.Panel1MinSize, _split.Height - logHeight);
        };
    }

    private static readonly string[] TabTitles =
    [
        "Число БЭСМ-6",
        "Целые числа",
        "IEEE-754",
        "Команда БЭСМ-6",
        "Системы счисления",
    ];

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