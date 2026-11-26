using Besm6.Android.Services;
using Besm6.Runtime;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace Besm6.Android.Ui;

/// <summary>Phone-friendly lamp panel; wide words wrap into 24-bit lines.</summary>
public sealed class LampPanelView : GraphicsView
{
    public static readonly BindableProperty SnapshotProperty = BindableProperty.Create(
        nameof(Snapshot), typeof(RegisterSnapshot), typeof(LampPanelView), null,
        propertyChanged: (bindable, _, _) => ((LampPanelView)bindable).Refresh());

    private const float RowHeight = 19;
    private const float LabelWidth = 66;
    private const float RightPadding = 6;
    private const float LampRadius = 3.4f;

    public LampPanelView()
    {
        Drawable = new Renderer(this);
        HorizontalOptions = LayoutOptions.Fill;
        HeightRequest = 180;
        SizeChanged += (_, _) => UpdateHeight();
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, _) => Invalidate();
    }

    public RegisterSnapshot? Snapshot
    {
        get => (RegisterSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    private static int BitsPerLine(float width) => width < 330 ? 16 : 24;

    private void Refresh()
    {
        UpdateHeight();
        Invalidate();
    }

    private void UpdateHeight()
    {
        int bitsPerLine = BitsPerLine(Width > 0 ? (float)Width : 360);
        int bufferLines = LinesFor(50, bitsPerLine);
        int lines = 3; // section headings
        lines += 1; // РЗ
        lines += bufferLines * 4; // БРЧ
        lines += bufferLines * 4; // БРС
        lines += bufferLines * 8; // БРЗ
        lines += 1; // ГРП
        lines += 1; // управление
        lines += 16; // M0..M15
        lines += 3; // K, C, R
        lines += LinesFor(48, bitsPerLine) * 2; // A, Y
        HeightRequest = 28 + lines * RowHeight;
    }

    private static int LinesFor(int bits, int perLine) => (bits + perLine - 1) / perLine;

    private sealed class Renderer(LampPanelView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            Color background = Color.FromArgb(dark ? "#1D1B19" : "#FFFCF7");
            Color ink = Color.FromArgb(dark ? "#F2EEE8" : "#1D1B19");
            Color muted = Color.FromArgb(dark ? "#B5ADA4" : "#6D665F");
            Color on = Color.FromArgb("#E8A15C");
            Color off = Color.FromArgb(dark ? "#3B342F" : "#DDD5CA");

            canvas.FillColor = background;
            canvas.FillRectangle(rect);
            canvas.FontColor = ink;
            canvas.FontSize = 10;

            var snapshot = owner.Snapshot;
            BufferRegisterView[] buffers = snapshot?.Buffers ?? [];

            float width = rect.Width > 0 ? rect.Width : 360;
            int perLine = BitsPerLine(width);
            float step = Math.Max(7.0f, (width - LabelWidth - RightPadding) / perLine);
            float radius = Math.Min(LampRadius, step * 0.34f);
            float y = 16;

            ulong BufferValue(string name, int slot)
            {
                var buffer = buffers.FirstOrDefault(item =>
                    string.Equals(item.Name, name, StringComparison.Ordinal) && item.Slot == slot);
                return buffer?.Raw50 ?? buffer?.Value ?? 0;
            }

            Section("БРУС");
            Row("РЗ", snapshot?.OperandProtection ?? 0, 32);
            for (int slot = 0; slot < 4; slot++) Row($"БРЧ[{slot}]", BufferValue("БРЧ", slot), 50);
            for (int slot = 0; slot < 4; slot++) Row($"БРС[{slot}]", BufferValue("БРС", slot), 50);
            for (int slot = 0; slot < 8; slot++) Row($"БРЗ[{slot}]", BufferValue("БРЗ", slot), 50);
            Row("ГРП", snapshot?.Interrupts ?? 0, 48);

            Section("УУ");
            Row("Управление", snapshot?.ControlFlags ?? 0, 15);
            for (int index = 0; index < 16; index++)
                Row($"M{index}", snapshot is { } s && index < s.M.Length ? s.M[index] : 0, 15);
            Row("K", snapshot?.K ?? 0, 15);
            Row("C", snapshot?.C ?? 0, 15);
            Row("R", snapshot?.R ?? 0, 6);

            Section("АУ");
            Row("A", snapshot?.A ?? 0, 48);
            Row("Y", snapshot?.Y ?? 0, 48);

            void Section(string title)
            {
                canvas.FontColor = Color.FromArgb(dark ? "#E06A58" : "#9B2C1F");
                canvas.FontSize = 11;
                canvas.DrawString(title, 8, y + 3, HorizontalAlignment.Left);
                canvas.FontColor = ink;
                canvas.FontSize = 10;
                y += RowHeight + 1;
            }

            void Row(string label, ulong value, int widthBits)
            {
                int remaining = widthBits;
                int high = widthBits - 1;
                bool first = true;
                while (remaining > 0)
                {
                    int count = Math.Min(perLine, remaining);
                    canvas.FontColor = muted;
                    canvas.FontSize = 9;
                    canvas.DrawString(first ? label : $"… {high}..{high - count + 1}",
                        8, y + 3, HorizontalAlignment.Left);

                    for (int i = 0; i < count; i++)
                    {
                        int bit = high - i;
                        bool set = ((value >> bit) & 1) != 0;
                        canvas.FillColor = set ? on : off;
                        canvas.FillCircle(LabelWidth + i * step, y + 4, radius);
                    }

                    y += RowHeight;
                    remaining -= count;
                    high -= count;
                    first = false;
                }
            }
        }
    }
}
