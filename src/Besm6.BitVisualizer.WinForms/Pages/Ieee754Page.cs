using System.Globalization;
using Besm6.Architecture.Visualization;
using Besm6.BitVisualizer.WinForms.Controls;

namespace Besm6.BitVisualizer.WinForms.Pages;

/// <summary>Вкладка IEEE-754: binary16, binary32 и binary64.</summary>
public sealed class Ieee754Page : CalculationPageBase
{
    private readonly BitGridControl _grid = new();
    private readonly ComboBox _format = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        AutoSize = true,
        Width = 140,
    };

    public Ieee754Page()
        : base(
            "IEEE-754",
            "Выберите формат: знак, смещённый порядок и дробная часть подсвечены по выбранному формату.")
    {
        _format.Items.Add("binary16 (Half)");
        _format.Items.Add("binary32 (float)");
        _format.Items.Add("binary64 (double)");
        _format.SelectedIndexChanged += (_, _) => SetFormat(WidthForIndex(_format.SelectedIndex));
        _format.SelectedIndex = 0;

        var options = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        options.Controls.Add(new Label { Text = "Формат:", AutoSize = true, Margin = new Padding(0, 5, 6, 0) });
        options.Controls.Add(_format);

        InputArea.Controls.Add(options);
        InputArea.Controls.Add(_grid);

        var button = new Button { Text = "Декодировать", AutoSize = true };
        button.Click += (_, _) => Decode();
        ActionArea.Controls.Add(button);
    }

    /// <summary>Текущая ширина формата в битах (16, 32 или 64).</summary>
    public int FormatWidth { get; private set; } = 16;

    public override string PageName => "IEEE-754";

    public override void RunDefaultCalculation() => Decode();

    /// <summary>Перестраивает сетку под формат, сбрасывая биты.</summary>
    public void SetFormat(int width)
    {
        if (width is not (16 or 32 or 64))
            throw new ArgumentOutOfRangeException(nameof(width), "Поддерживаются только форматы 16, 32 и 64 бита.");
        FormatWidth = width;
        _grid.Configure(width, FieldsFor(width));
    }

    /// <summary>Декодирует биты и публикует отчёт.</summary>
    public void Decode()
    {
        HandleUserAction(() =>
        {
            var result = Ieee754Decoder.Decode(_grid.ToBitPattern());
            ShowResult($"{result.Value.ToString("R", CultureInfo.InvariantCulture)} — {result.Classification}");
            RaiseReport(result.Report);
        });
    }

    protected override string CurrentInput
        => $"{_grid.ToBitPattern().ToBinaryString()}₂ (формат {FormatWidth} бит)";

    private static int WidthForIndex(int index) => index switch
    {
        0 => 16,
        1 => 32,
        2 => 64,
        _ => 16,
    };

    private static BitField[] FieldsFor(int width)
    {
        int exponentBits = width is 16 ? 5 : width is 32 ? 8 : 11;
        return
        [
            new BitField("Знак", width - 1, width - 1, BitFieldKind.Sign),
            new BitField("Порядок", width - 2, width - 1 - exponentBits, BitFieldKind.Exponent),
            new BitField("Дробная часть", width - 2 - exponentBits, 0, BitFieldKind.Fraction),
        ];
    }
}