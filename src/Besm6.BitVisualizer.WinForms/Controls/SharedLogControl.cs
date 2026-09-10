using System.Text;
using Besm6.BitVisualizer;

namespace Besm6.BitVisualizer.WinForms.Controls;

/// <summary>Общий журнал операций: накопление подписанных блоков, «Копировать» и «Очистить».</summary>
public sealed class SharedLogControl : UserControl
{
    private readonly RichTextBox _log;
    private readonly Button _copyButton;
    private readonly Button _clearButton;
    private int _entryCount;

    public SharedLogControl()
    {
        _log = new RichTextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.5f),
            WordWrap = false,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
        };
        _copyButton = new Button { Text = "Копировать", AutoSize = true, Margin = new Padding(0, 4, 8, 4) };
        _clearButton = new Button { Text = "Очистить", AutoSize = true, Margin = new Padding(0, 4, 4, 4) };
        var toolbar = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
        };
        toolbar.Controls.Add(_copyButton);
        toolbar.Controls.Add(_clearButton);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_log, 0, 1);
        Controls.Add(layout);

        _copyButton.Click += (_, _) =>
        {
            if (_log.TextLength > 0)
                Clipboard.SetText(_log.Text);
        };
        _clearButton.Click += (_, _) => ClearLog();
    }

    /// <summary>Кнопка «Копировать».</summary>
    public Button CopyButton => _copyButton;

    /// <summary>Кнопка «Очистить».</summary>
    public Button ClearButton => _clearButton;

    /// <summary>Полный текст журнала.</summary>
    public new string Text => _log.Text;

    /// <summary>Число накопленных блоков (операции и ошибки).</summary>
    public int EntryCount => _entryCount;

    /// <summary>Добавляет в журнал подписанный учебный блок вычисления.</summary>
    public void Append(CalculationReport report, DateTime timestamp)
    {
        ArgumentNullException.ThrowIfNull(report);
        var builder = new StringBuilder();
        builder.Append('[').Append(timestamp.ToString("HH:mm:ss")).Append("] ").AppendLine(report.Operation);
        builder.Append("Вход: ").AppendLine(report.Input);
        for (int index = 0; index < report.Steps.Count; index++)
            builder.Append(index + 1).Append(". ").AppendLine(report.Steps[index]);
        builder.Append("Результат: ").Append(report.Result).AppendLine();
        builder.AppendLine();
        AppendBlock(builder.ToString());
    }

    /// <summary>Добавляет в журнал блок ошибки ввода с объяснением, не удаляя историю.</summary>
    public void AppendError(string category, string input, string message, DateTime timestamp)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Категория ошибки не может быть пустой.", nameof(category));
        ArgumentNullException.ThrowIfNull(message);

        var builder = new StringBuilder();
        builder.Append('[').Append(timestamp.ToString("HH:mm:ss")).Append("] ").Append(category).AppendLine(" — ошибка");
        if (!string.IsNullOrWhiteSpace(input))
            builder.Append("Вход: ").AppendLine(input);
        builder.Append("Объяснение: ").Append(message).AppendLine();
        builder.AppendLine();
        AppendBlock(builder.ToString());
    }

    /// <summary>Очищает журнал и счётчик блоков.</summary>
    public void ClearLog()
    {
        _log.Clear();
        _entryCount = 0;
    }

    private void AppendBlock(string block)
    {
        _log.AppendText(block);
        _entryCount++;
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}