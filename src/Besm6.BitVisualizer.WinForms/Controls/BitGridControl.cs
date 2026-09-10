using System.Numerics;
using Besm6.Architecture.Visualization;

namespace Besm6.BitVisualizer.WinForms.Controls;

/// <summary>Сетка битов: блоки по восемь ячеек от старшего бита (слева) к младшему (справа).</summary>
public sealed class BitGridControl : UserControl
{
    private const int CellWidth = 30;
    private const int CellHeight = 42;

    private readonly TableLayoutPanel _rows = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1,
    };
    private readonly List<Panel> _cells = new();
    private readonly List<Label> _labels = new();
    private readonly List<CheckBox> _boxes = new();
    private int _bitCount;

    public BitGridControl()
    {
        AutoScroll = true;
        Padding = new Padding(6);
        Controls.Add(_rows);
    }

    /// <summary>Число битов в сетке.</summary>
    public int BitCount => _bitCount;

    /// <summary>Чекбоксы битов; индекс элемента — номер бита (0 — младший).</summary>
    public IReadOnlyList<CheckBox> BitBoxes => _boxes;

    /// <summary>Номер бита (0 — младший), отображённый на позиции <paramref name="visualIndex"/> слева направо.</summary>
    public int GetDisplayedBitIndex(int visualIndex)
    {
        if (visualIndex < 0 || visualIndex >= _bitCount)
            throw new ArgumentOutOfRangeException(nameof(visualIndex), "Визуальный индекс выходит за границы сетки.");
        return _bitCount - 1 - visualIndex;
    }

    /// <summary>Перестраивает сетку под заданную ширину и схему полей, сбрасывая состояние битов.</summary>
    public void Configure(int width, IReadOnlyList<BitField> fields)
    {
        if (width < 1)
            throw new ArgumentOutOfRangeException(nameof(width), "Ширина должна быть положительной.");
        ArgumentNullException.ThrowIfNull(fields);

        _bitCount = width;
        _cells.Clear();
        _labels.Clear();
        _boxes.Clear();
        _rows.SuspendLayout();
        _rows.Controls.Clear();
        _rows.RowStyles.Clear();
        _rows.RowCount = 0;

        for (int row = 0; row * 8 < width; row++)
        {
            var rowPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };
            for (int column = 0; column < 8; column++)
            {
                int bitIndex = width - 1 - (row * 8 + column);
                if (bitIndex < 0)
                {
                    rowPanel.Controls.Add(new Panel { Size = new Size(CellWidth, CellHeight) });
                    continue;
                }

                rowPanel.Controls.Add(CreateCell(bitIndex));
            }

            _rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _rows.Controls.Add(rowPanel, 0, _rows.RowCount++);
        }

        _rows.ResumeLayout();
        ApplyFields(fields);
    }

    /// <summary>Перекрашивает ячейки и подписи по новой схеме полей, сохраняя состояние чекбоксов.</summary>
    public void ApplyFields(IReadOnlyList<BitField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        for (int bitIndex = 0; bitIndex < _bitCount; bitIndex++)
        {
            BitField field = fields.FirstOrDefault(candidate => candidate.Contains(bitIndex))
                ?? new BitField("Значение", bitIndex, bitIndex, BitFieldKind.Value);
            _cells[bitIndex].BackColor = Theme.ColorFor(field.Kind);
            _labels[bitIndex].ForeColor = Theme.ForegroundFor(field.Kind);
            _boxes[bitIndex].AccessibleName = $"{field.Name}, бит {bitIndex}";
        }
    }

    /// <summary>Устанавливает состояние всех чекбоксов по беззнаковому значению.</summary>
    public void SetValue(BigInteger value)
    {
        if (value < BigInteger.Zero || value >= BigInteger.One << _bitCount)
            throw new ArgumentOutOfRangeException(nameof(value), "Значение не помещается в ширину сетки.");
        for (int bitIndex = 0; bitIndex < _bitCount; bitIndex++)
            _boxes[bitIndex].Checked = ((value >> bitIndex) & BigInteger.One) != BigInteger.Zero;
    }

    /// <summary>Собирает состояние чекбоксов в <see cref="BitPattern"/> ширины сетки.</summary>
    public BitPattern ToBitPattern()
    {
        BigInteger value = BigInteger.Zero;
        for (int bitIndex = _bitCount - 1; bitIndex >= 0; bitIndex--)
        {
            value <<= 1;
            if (_boxes[bitIndex].Checked)
                value += BigInteger.One;
        }

        return new BitPattern(_bitCount, value);
    }

    /// <summary>Сбрасывает все биты в ноль.</summary>
    public void Clear()
    {
        foreach (CheckBox box in _boxes)
            box.Checked = false;
    }

    private Panel CreateCell(int bitIndex)
    {
        var label = new Label
        {
            Text = bitIndex.ToString(),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f),
        };
        var box = new CheckBox
        {
            Size = new Size(18, 18),
            Appearance = Appearance.Button,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        var cell = new Panel { Size = new Size(CellWidth, CellHeight) };
        label.Location = new Point((CellWidth - label.PreferredWidth) / 2, 2);
        box.Location = new Point((CellWidth - 18) / 2, 16);
        cell.Controls.Add(label);
        cell.Controls.Add(box);

        _cells.Add(cell);
        _labels.Add(label);
        _boxes.Add(box);
        return cell;
    }
}