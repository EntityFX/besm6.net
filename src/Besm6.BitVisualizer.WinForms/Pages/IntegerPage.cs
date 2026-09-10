using System.Numerics;
using Besm6.BitVisualizer;
using Besm6.BitVisualizer.WinForms.Controls;

namespace Besm6.BitVisualizer.WinForms.Pages;

/// <summary>Вкладка современных целых чисел шириной 8/16/32/64 бит.</summary>
public sealed class IntegerPage : CalculationPageBase
{
    private readonly BitGridControl _grid = new();
    private readonly ComboBox _width = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        AutoSize = true,
        Width = 96,
    };
    private readonly CheckBox _signed = new()
    {
        Text = "Знаковое число (дополнительный код)",
        AutoSize = true,
    };

    public IntegerPage()
        : base(
            "Целые числа",
            "Выберите разрядность и режим. При знаковом режиме старший бит — знак (дополнительный код).")
    {
        _width.Items.Add(8);
        _width.Items.Add(16);
        _width.Items.Add(32);
        _width.Items.Add(64);
        _width.SelectedIndexChanged += (_, _) => SetWidth((int)_width.SelectedItem!);
        _width.SelectedIndex = 0;

        _signed.CheckedChanged += (_, _) => _grid.ApplyFields(FieldsFor(Width, _signed.Checked));

        var options = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        options.Controls.Add(new Label { Text = "Разрядность:", AutoSize = true, Margin = new Padding(0, 5, 6, 0) });
        options.Controls.Add(_width);
        options.Controls.Add(_signed);

        InputArea.Controls.Add(options);
        InputArea.Controls.Add(_grid);

        var button = new Button { Text = "Преобразовать", AutoSize = true };
        button.Click += (_, _) => Calculate();
        ActionArea.Controls.Add(button);
    }

    /// <summary>Текущая разрядность сетки.</summary>
    public new int Width { get; private set; } = 8;

    /// <summary>Режим знаковости: true — дополнительный код, false — беззнаковое число.</summary>
    public bool Signed
    {
        get => _signed.Checked;
        set => _signed.Checked = value;
    }

    /// <summary>Семантическая категория старшего бита в текущем режиме.</summary>
    public BitFieldKind MostSignificantBitKind
        => FieldsFor(Width, Signed).Single(field => field.Contains(Width - 1)).Kind;

    public override string PageName => "Целые числа";

    public override void RunDefaultCalculation() => Calculate();

    /// <summary>Перестраивает сетку под новую разрядность, сбрасывая биты и сохраняя режим.</summary>
    public void SetWidth(int width)
    {
        Width = width;
        _grid.Configure(width, FieldsFor(width, Signed));
    }

    /// <summary>Устанавливает состояние битов по беззнаковому значению.</summary>
    public void SetBits(BigInteger value) => _grid.SetValue(value);

    /// <summary>Декодирует сетку в выбранном режиме и публикует отчёт.</summary>
    public void Calculate()
    {
        HandleUserAction(() =>
        {
            var result = IntegerDecoder.Decode(
                _grid.ToBitPattern(),
                Signed ? IntegerInterpretation.Signed : IntegerInterpretation.Unsigned);
            ShowResult(result.Value.ToString());
            RaiseReport(result.Report);
        });
    }

    protected override string CurrentInput
        => $"{_grid.ToBitPattern().ToBinaryString()}₂ (разрядность {Width}, {(Signed ? "знаковое" : "беззнаковое")})";

    private static BitField[] FieldsFor(int width, bool signed) => signed
        ?
        [
            new BitField("Знак", width - 1, width - 1, BitFieldKind.Sign),
            new BitField("Значение", width - 2, 0, BitFieldKind.Value),
        ]
        :
        [
            new BitField("Значение", width - 1, 0, BitFieldKind.Value),
        ];
}