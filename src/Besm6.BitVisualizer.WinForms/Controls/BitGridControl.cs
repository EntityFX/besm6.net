using System.Numerics;
using Besm6.BitVisualizer;
using Besm6.BitVisualizer.WinForms;

namespace Besm6.BitVisualizer.WinForms.Controls;

/// <summary>
/// Сетка чекбоксов для битов: бит 0 — правый, MSB — левый. Ячейки имеют
/// фиксированную геометрию (детерминированное выравнивание рядов), биты
/// сгруппированы по восьми, поля подсвечиваются и дублируются легендой.
/// Индексация коллекций (<see cref="BitBoxes"/>, <see cref="BitCells"/>) —
/// по номеру бита, а не порядку создания.
/// </summary>
public sealed class BitGridControl : UserControl
{
    private const int BitsPerGroup = 8;
    private const int BitsPerRow = 16;
    private const int CellWidth = 34;
    private const int CellHeight = 50;
    private const int CheckboxSize = 20;
    private const int GroupGap = 10;
    private static readonly Color TextColor = Color.FromArgb(0x33, 0x3A, 0x47);

    private readonly Label _title;
    private readonly FlowLayoutPanel _legend;
    private readonly TableLayoutPanel _grid;
    private readonly List<Panel> _cells = new();
    private readonly List<Label> _labels = new();
    private readonly List<CheckBox> _boxes = new();
    private int _bitCount;

    public BitGridControl()
    {
        AutoScroll = true;
        Padding = new Padding(6);

        _title = new Label
        {
            Text = "Биты (старшие слева)",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = TextColor,
            Dock = DockStyle.Top,
            Margin = new Padding(2, 2, 0, 6),
        };

        _legend = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Dock = DockStyle.Top,
            Margin = new Padding(2, 0, 0, 10),
        };

        _grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };

        Controls.Add(_grid);
        Controls.Add(_legend);
        Controls.Add(_title);
    }

    public int BitCount => _bitCount;

    public IReadOnlyList<CheckBox> BitBoxes => _boxes;

    public IReadOnlyList<Panel> BitCells => _cells;

    /// <summary>Перестраивает сетку под <paramref name="bitWidth"/> битов.</summary>
    public void Configure(int bitWidth, IReadOnlyList<BitField> fields)
    {
        if (bitWidth is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(bitWidth), bitWidth, "Размер сетки должен быть от 1 до 64 бит.");
        }

        if (fields is null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        _bitCount = bitWidth;
        _cells.Clear();
        _labels.Clear();
        _boxes.Clear();
        _grid.Controls.Clear();
        _grid.ColumnStyles.Clear();
        _grid.RowStyles.Clear();

        int rows = (bitWidth + BitsPerRow - 1) / BitsPerRow;
        bool hasSecondHalf = bitWidth > BitsPerGroup;
        int columns = hasSecondHalf ? BitsPerRow + 1 : BitsPerGroup;
        _grid.ColumnCount = columns;

        for (int column = 0; column < columns; column++)
        {
            bool isGap = column == BitsPerGroup;
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, isGap ? GroupGap : CellWidth));
        }

        for (int row = 0; row < rows; row++)
        {
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, CellHeight));
        }

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < BitsPerRow; column++)
            {
                int bitIndex = bitWidth - 1 - (row * BitsPerRow + column);
                if (bitIndex < 0)
                {
                    break;
                }

                (Panel cell, Label label, CheckBox box) = CreateCell(bitIndex);
                _cells.Add(cell);
                _labels.Add(label);
                _boxes.Add(box);
                int tableColumn = hasSecondHalf && column >= BitsPerGroup ? column + 1 : column;
                _grid.Controls.Add(cell, tableColumn, row);
            }
        }

        // Ячейки создаются строго от MSB к LSB: после разворота
        // индекс списка совпадает с номером бита.
        _cells.Reverse();
        _labels.Reverse();
        _boxes.Reverse();

        ApplyFields(fields);
    }

    public void ApplyFields(IReadOnlyList<BitField> fields)
    {
        if (fields is null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        BuildLegend(fields);

        foreach (BitField field in fields)
        {
            Color color = Theme.ColorFor(field.Kind);
            Color text = Theme.ForegroundFor(field.Kind);

            for (int bitIndex = field.LeastSignificantBit; bitIndex <= field.MostSignificantBit; bitIndex++)
            {
                _labels[bitIndex].ForeColor = text;
                _cells[bitIndex].BackColor = color;
                _boxes[bitIndex].AccessibleName = $"Бит {bitIndex}, {field.Name}";
            }
        }
    }

    /// <summary>Номер бита, отображаемого на позиции <paramref name="position"/> (0 — левая ячейка).</summary>
    public int GetDisplayedBitIndex(int position)
    {
        if (position < 0 || position >= _bitCount)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Позиция вне диапазона сетки.");
        }

        return _bitCount - 1 - position;
    }

    /// <summary>Устанавливает состояние всех битов по беззнаковому значению.</summary>
    public void SetValue(BigInteger value)
    {
        if (value < BigInteger.Zero || value >= (BigInteger.One << _bitCount))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Значение не помещается в ширину сетки.");
        }

        for (int i = 0; i < _bitCount; i++)
        {
            _boxes[i].Checked = ((value >> i) & BigInteger.One) != BigInteger.Zero;
        }
    }

    public BitPattern ToBitPattern()
    {
        BigInteger value = BigInteger.Zero;

        for (int i = 0; i < _bitCount; i++)
        {
            if (_boxes[i].Checked)
            {
                value |= BigInteger.One << i;
            }
        }

        return new BitPattern(_bitCount, value);
    }

    public void Clear()
    {
        foreach (CheckBox box in _boxes)
        {
            box.Checked = false;
        }
    }

    /// <summary>
    /// Геометрия фиксирована: подписи и чекбокс стоят в одной точке в каждой
    /// ячейке, поэтому ряды получаются идеально ровными.
    /// </summary>
    private static (Panel Cell, Label Label, CheckBox Box) CreateCell(int bitIndex)
    {
        var label = new Label
        {
            Text = bitIndex.ToString(),
            AutoSize = false,
            Size = new Size(CellWidth, 16),
            Location = new Point(0, 2),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8f),
            ForeColor = TextColor,
            Margin = Padding.Empty,
        };

        var box = new CheckBox
        {
            AutoSize = false,
            Size = new Size(CheckboxSize, CheckboxSize),
            Location = new Point((CellWidth - CheckboxSize) / 2, 24),
            CheckAlign = ContentAlignment.MiddleCenter,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = TextColor,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };

        var cell = new Panel
        {
            Size = new Size(CellWidth, CellHeight),
            Margin = Padding.Empty,
        };

        cell.Controls.Add(label);
        cell.Controls.Add(box);
        return (cell, label, box);
    }

    private void BuildLegend(IReadOnlyList<BitField> fields)
    {
        _legend.Controls.Clear();

        foreach (BitField field in fields)
        {
            _legend.Controls.Add(new Label
            {
                Text = field.Name,
                AutoSize = true,
                ForeColor = Theme.ForegroundFor(field.Kind),
                BackColor = Theme.ColorFor(field.Kind),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(8, 3, 8, 3),
                Margin = new Padding(0, 0, 10, 0),
            });
        }
    }
}