using System.Globalization;
using Besm6.Architecture.Visualization;
using Besm6.BitVisualizer.WinForms.Controls;

namespace Besm6.BitVisualizer.WinForms.Pages;

/// <summary>Вкладка 24-битной команды БЭСМ-6 с мгновенной сменой разметки по бит 19.</summary>
public sealed class InstructionPage : CalculationPageBase
{
    private readonly BitGridControl _grid = new();

    public InstructionPage()
        : base(
            "Команда БЭСМ-6 (24 бита)",
            "Бит 19 — признак формата: 1 — длинная команда, 0 — короткая. Границы полей перекрашиваются сразу при смене бита.")
    {
        _grid.Configure(24, FieldsFor(false));
        _grid.BitBoxes[19].CheckedChanged += (_, _) => _grid.ApplyFields(FieldsFor(_grid.BitBoxes[19].Checked));
        InputArea.Controls.Add(_grid);

        var button = new Button { Text = "Декодировать", AutoSize = true };
        button.Click += (_, _) => Decode();
        ActionArea.Controls.Add(button);
    }

    /// <summary>Значение форматного бита 19.</summary>
    public bool FormatBit => _grid.BitBoxes[19].Checked;

    /// <summary>Истинно, пока активна разметка короткой команды.</summary>
    public bool HasShortLayout
    {
        get
        {
            var fields = FieldsFor(FormatBit);
            var opcode = fields.Single(field => field.Kind == BitFieldKind.Opcode);
            return opcode.MostSignificantBit == 17;
        }
    }

    public override string PageName => "Команда БЭСМ-6";

    public override void RunDefaultCalculation() => Decode();

    /// <summary>Устанавливает форматный бит 19; сетка перекрашивается сразу.</summary>
    public void SetFormatBit(bool isLong) => _grid.BitBoxes[19].Checked = isLong;

    /// <summary>Декодирует полуслово через ядро и публикует отчёт.</summary>
    public void Decode()
    {
        HandleUserAction(() =>
        {
            var result = Besm6InstructionDecoder.Decode(_grid.ToBitPattern());
            ShowResult(
                $"M{result.Instruction.Register} {result.Mnemonic} {result.Instruction.Address.ToString("X")} — {result.Description}");
            RaiseReport(result.Report);
        });
    }

    protected override string CurrentInput
        => $"{_grid.ToBitPattern().ToBinaryString()}₂ ({_grid.ToBitPattern().ToOctalString()}₈)";

    private static BitField[] FieldsFor(bool isLong) => isLong
        ?
        [
            new BitField("Индекс-регистр", 23, 20, BitFieldKind.Register),
            new BitField("Формат", 19, 19, BitFieldKind.Format),
            new BitField("Код операции", 18, 15, BitFieldKind.Opcode),
            new BitField("Адрес", 14, 0, BitFieldKind.Address),
        ]
        :
        [
            new BitField("Индекс-регистр", 23, 20, BitFieldKind.Register),
            new BitField("Формат", 19, 19, BitFieldKind.Format),
            new BitField("Расширение адреса", 18, 18, BitFieldKind.Format),
            new BitField("Код операции", 17, 12, BitFieldKind.Opcode),
            new BitField("Адрес", 11, 0, BitFieldKind.Address),
        ];
}