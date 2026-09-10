using Besm6.BitVisualizer;
using Besm6.BitVisualizer.WinForms.Controls;

namespace Besm6.BitVisualizer.WinForms.Pages;

/// <summary>Вкладка перевода целых чисел между системами счисления (основания 2–36).</summary>
public sealed class RadixConverterPage : CalculationPageBase
{
    private readonly TextBox _input = new() { Text = "255", Width = 240 };
    private readonly NumericUpDown _sourceBase = new() { Minimum = 2, Maximum = 36, Value = 10, Width = 64 };
    private readonly NumericUpDown _targetBase = new() { Minimum = 2, Maximum = 36, Value = 2, Width = 64 };

    public RadixConverterPage()
        : base(
            "Системы счисления",
            "Введите целое число и выберите основания от 2 до 36. Разделители разрядов, дробная и экспоненциальная записи не принимаются.")
    {
        var inputRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        inputRow.Controls.Add(new Label { Text = "Число:", AutoSize = true, Margin = new Padding(0, 5, 6, 0) });
        inputRow.Controls.Add(_input);

        var sourceRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        sourceRow.Controls.Add(new Label { Text = "Исходное основание:", AutoSize = true, Margin = new Padding(0, 5, 6, 0) });
        sourceRow.Controls.Add(_sourceBase);
        sourceRow.Controls.Add(QuickBaseButton(_sourceBase));
        sourceRow.Controls.Add(QuickBaseButton(_sourceBase, 8));
        sourceRow.Controls.Add(QuickBaseButton(_sourceBase, 10));
        sourceRow.Controls.Add(QuickBaseButton(_sourceBase, 16));

        var targetRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        targetRow.Controls.Add(new Label { Text = "Целевое основание:", AutoSize = true, Margin = new Padding(0, 5, 6, 0) });
        targetRow.Controls.Add(_targetBase);
        targetRow.Controls.Add(QuickBaseButton(_targetBase));
        targetRow.Controls.Add(QuickBaseButton(_targetBase, 8));
        targetRow.Controls.Add(QuickBaseButton(_targetBase, 10));
        targetRow.Controls.Add(QuickBaseButton(_targetBase, 16));

        var swap = new Button { Text = "Поменять основания", AutoSize = true, Margin = new Padding(12, 4, 0, 0) };
        swap.Click += (_, _) =>
        {
            decimal source = _sourceBase.Value;
            _sourceBase.Value = _targetBase.Value;
            _targetBase.Value = source;
        };
        targetRow.Controls.Add(swap);

        InputArea.Controls.Add(inputRow);
        InputArea.Controls.Add(sourceRow);
        InputArea.Controls.Add(targetRow);

        var button = new Button { Text = "Преобразовать", AutoSize = true };
        button.Click += (_, _) => Convert();
        ActionArea.Controls.Add(button);
    }

    public override string PageName => "Системы счисления";

    /// <summary>Текущий текст вводимого числа.</summary>
    public string InputText => _input.Text;

    public override void RunDefaultCalculation() => Convert();

    /// <summary>Устанавливает входное число (для теста и быстрого набора).</summary>
    public void SetInput(string text) => _input.Text = text;

    /// <summary>Устанавливает исходное и целевое основания.</summary>
    public void SetBases(int sourceBase, int targetBase)
    {
        _sourceBase.Value = sourceBase;
        _targetBase.Value = targetBase;
    }

    /// <summary>Выполняет перевод через ядро и публикует отчёт; при ошибке вход сохраняется.</summary>
    public void Convert()
    {
        HandleUserAction(() =>
        {
            var result = RadixConverter.Convert(_input.Text, (int)_sourceBase.Value, (int)_targetBase.Value);
            ShowResult(result.Output);
            RaiseReport(result.Report);
        });
    }

    protected override string CurrentInput
        => $@"«{_input.Text}» ({(int)_sourceBase.Value} → {(int)_targetBase.Value})";

    private Button QuickBaseButton(NumericUpDown target, int radix = 2)
    {
        var button = new Button { Text = radix.ToString(), AutoSize = true, Margin = new Padding(2, 3, 2, 0) };
        button.Click += (_, _) => target.Value = radix;
        return button;
    }
}