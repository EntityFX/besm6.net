using Besm6.BitVisualizer;

namespace Besm6.BitVisualizer.WinForms.Pages;

/// <summary>Аргументы события ошибки ввода на рабочей вкладке.</summary>
public sealed record PageErrorEventArgs(string Input, string Message);

/// <summary>
/// Общий каркас вкладки: заголовок, пояснение, область ввода, inline-ошибка,
/// действия и карточка результата. Не содержит формул — только поток событий.
/// </summary>
public abstract class CalculationPageBase : UserControl
{
    private readonly Label _resultLabel;
    private readonly Label _errorLabel;

    protected CalculationPageBase(string title, string helpText)
    {
        Dock = DockStyle.Fill;

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 2),
        };
        var helpLabel = new Label
        {
            Text = helpText,
            AutoSize = true,
            ForeColor = Color.DimGray,
            MaximumSize = new Size(900, 0),
        };
        InputArea = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 6, 0, 0) };
        _errorLabel = new Label
        {
            Text = string.Empty,
            AutoSize = true,
            ForeColor = Color.FromArgb(0xB0, 0x1A, 0x1A),
            MaximumSize = new Size(900, 0),
        };
        ActionArea = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
        _resultLabel = new Label
        {
            Text = "—",
            AutoSize = true,
            Font = new Font("Consolas", 11f, FontStyle.Bold),
        };
        var resultCard = new Panel
        {
            AutoSize = true,
            BackColor = Color.FromArgb(0xF3, 0xF4, 0xF8),
            Padding = new Padding(10),
        };
        var resultCaption = new Label
        {
            Text = "Результат",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 2),
        };
        resultCard.Controls.Add(resultCaption);
        resultCard.Controls.Add(_resultLabel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(14),
        };
        Control[] rows = [titleLabel, helpLabel, InputArea, _errorLabel, ActionArea, resultCard];
        foreach (Control row in rows)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(row, 0, layout.RowCount++);
        }

        // Хост со скроллом: когда контент выше вкладки, появляется прокрутка,
        // а действия и карточка результата остаются достижимыми, а не обрезанными.
        var host = new Panel { AutoScroll = true, Dock = DockStyle.Fill };
        host.Controls.Add(layout);
        Controls.Add(host);
    }

    /// <summary>Название категории вкладки для журнала.</summary>
    public abstract string PageName { get; }

    /// <summary>Область, куда вкладка размещает ввод (сетку битов, строки, переключатели).</summary>
    public Panel InputArea { get; }

    /// <summary>Область кнопок действий.</summary>
    public FlowLayoutPanel ActionArea { get; }

    /// <summary>Метка inline-ошибки ввода.</summary>
    public Label ErrorLabel => _errorLabel;

    /// <summary>Текст inline-ошибки (пусто, если ошибок нет).</summary>
    public string ErrorText => _errorLabel.Text;

    /// <summary>Текст карточки результата.</summary>
    public string ResultText => _resultLabel.Text;

    /// <summary>Событие успешного вычисления: полный учебный отчёт.</summary>
    public event EventHandler<CalculationReport>? ReportCreated;

    /// <summary>Событие ошибки ввода или неожиданного сбоя.</summary>
    public event EventHandler<PageErrorEventArgs>? ErrorCreated;

    /// <summary>Представление входа для блока ошибки в общем журнале.</summary>
    protected virtual string CurrentInput => string.Empty;

    /// <summary>Выполняет расчёт по умолчанию (используется кнопкой и тестами).</summary>
    public abstract void RunDefaultCalculation();

    /// <summary>Заполняет карточку результата.</summary>
    protected void ShowResult(string text)
        => _resultLabel.Text = text;

    /// <summary>Передаёт отчёт подписчику (общий журнал).</summary>
    protected void RaiseReport(CalculationReport report)
        => ReportCreated?.Invoke(this, report);

    /// <summary>
    /// Выполняет действие пользователя: ожидаемые ошибки ввода показывают inline и
    /// уходят в журнал с объяснением, неожиданные — как нейтральный диагностический блок.
    /// </summary>
    protected void HandleUserAction(Action action)
    {
        try
        {
            action();
            _errorLabel.Text = string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            _errorLabel.Text = ex.Message;
            ErrorCreated?.Invoke(this, new PageErrorEventArgs(CurrentInput, ex.Message));
        }
        catch (Exception ex)
        {
#if DEBUG
            string diagnostic = $"{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
#else
            string diagnostic = $"{ex.GetType().Name}: {ex.Message}";
#endif
            _errorLabel.Text = "Непредвиденная ошибка. Подробности записаны в общий журнал.";
            ErrorCreated?.Invoke(this, new PageErrorEventArgs(CurrentInput, $"Непредвиденная ошибка: {diagnostic}"));
        }
    }
}