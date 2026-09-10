using System.Globalization;
using System.Numerics;
using Besm6.Architecture;
using Besm6.Architecture.Visualization;
using Besm6.BitVisualizer.WinForms.Controls;

namespace Besm6.BitVisualizer.WinForms.Pages;

/// <summary>Вкладка нативного 48-битного числа БЭСМ-6.</summary>
public sealed class Besm6FloatPage : CalculationPageBase
{
    private readonly BitGridControl _grid = new();

    private static readonly BitField[] Fields =
    [
        new("Порядок", 47, 41, BitFieldKind.Exponent),
        new("Знак мантиссы", 40, 40, BitFieldKind.Sign),
        new("Мантисса", 39, 0, BitFieldKind.Fraction),
    ];

    public Besm6FloatPage()
        : base(
            "Число БЭСМ-6 (48 бит)",
            "Старшие семь бит — смещённый порядок (смещение 64), младшие 41 бит — знаковая мантисса в дополнительном коде.")
    {
        _grid.Configure(48, Fields);
        InputArea.Controls.Add(_grid);

        var button = new Button { Text = "Преобразовать", AutoSize = true };
        button.Click += (_, _) => Calculate();
        ActionArea.Controls.Add(button);
    }

    /// <summary>Число битов сетки.</summary>
    public int BitCount => _grid.BitCount;

    public override string PageName => "Число БЭСМ-6";

    public override void RunDefaultCalculation() => Calculate();

    /// <summary>Устанавливает состояние битов по 48-битному слову.</summary>
    public void SetBits(ulong word) => _grid.SetValue(word);

    /// <summary>Декодирует слово через ядро и публикует отчёт.</summary>
    public void Calculate()
    {
        HandleUserAction(() =>
        {
            var result = Besm6FloatDecoder.Decode(_grid.ToBitPattern());
            ShowResult(
                $"Значение: {result.Value.ToString("R", CultureInfo.InvariantCulture)}; " +
                $"порядок: {result.Exponent}; мантисса: {result.Mantissa}");
            RaiseReport(result.Report);
        });
    }

    protected override string CurrentInput
        => $"{_grid.ToBitPattern().ToBinaryString()}₂";
}