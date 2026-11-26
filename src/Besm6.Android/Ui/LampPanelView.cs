using Besm6.Android.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace Besm6.Android.Ui;

/// <summary>Compact lamp-panel visualization of the BESM-6 register racks.</summary>
public sealed class LampPanelView : GraphicsView
{
    public static readonly BindableProperty SnapshotProperty = BindableProperty.Create(
        nameof(Snapshot), typeof(RegisterSnapshot), typeof(LampPanelView), null,
        propertyChanged: (bindable, _, _) => ((LampPanelView)bindable).Refresh());

    private const float RowHeight = 20;
    private const float BitStep = 9;
    private const float BitsLeft = 104;
    private const float LampRadius = 3.2f;

    public LampPanelView()
    {
        Drawable = new Renderer(this);
        WidthRequest = 720;
        HeightRequest = 180;
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, _) => Invalidate();
    }

    public RegisterSnapshot? Snapshot
    {
        get => (RegisterSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    private void Refresh()
    {
        int rows = 3;
        if (Snapshot is { } snapshot)
        {
            rows += snapshot.Buffers.Length;
            rows += snapshot.Interrupts is null ? 0 : 1;
            rows += 1 + 16 + 3 + 2 + 2;
        }
        HeightRequest = 32 + rows * RowHeight;
        Invalidate();
    }

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
            if (snapshot is null)
            {
                canvas.FontColor = muted;
                canvas.DrawString("Снимок ламп ещё не получен.", rect,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            float y = 18;
            Section("БРУС");
            if (snapshot.OperandProtection is { } protection)
                Row("РЗ", protection, 32);
            foreach (var buffer in snapshot.Buffers)
                Row($"{buffer.Name}[{buffer.Slot}]", buffer.Raw50 ?? buffer.Value,
                    buffer.Raw50 is null ? 48 : 50);
            if (snapshot.Interrupts is { } interrupts)
                Row("ГРП", interrupts, 48);

            Section("УУ");
            if (snapshot.ControlFlags is { } controls)
                Row("Управление", controls, 15);
            for (int index = 0; index < 16 && index < snapshot.M.Length; index++)
                Row($"M{index}", snapshot.M[index], 15);
            Row("K", snapshot.K, 15);
            Row("C", snapshot.C, 15);
            Row("R", snapshot.R, 6);

            Section("АУ");
            Row("A", snapshot.A, 48);
            Row("Y", snapshot.Y, 48);

            void Section(string title)
            {
                canvas.FontColor = Color.FromArgb(dark ? "#E06A58" : "#9B2C1F");
                canvas.FontSize = 11;
                canvas.DrawString(title, 8, y + 3, HorizontalAlignment.Left);
                canvas.FontColor = ink;
                canvas.FontSize = 10;
                y += RowHeight + 2;
            }

            void Row(string label, ulong value, int width)
            {
                canvas.FontColor = muted;
                canvas.DrawString(label, 8, y + 3, HorizontalAlignment.Left);
                for (int bit = width - 1; bit >= 0; bit--)
                {
                    bool set = ((value >> bit) & 1) != 0;
                    canvas.FillColor = set ? on : off;
                    float x = BitsLeft + (width - 1 - bit) * BitStep;
                    canvas.FillCircle(x, y + 4, LampRadius);
                }
                y += RowHeight;
            }
        }
    }
}
