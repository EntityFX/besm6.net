using Besm6.Android.Services;
using Microsoft.Maui.Graphics;

namespace Besm6.Android.Ui;

/// <summary>GraphicsView wrapper that renders decoded plotter segments.</summary>
public sealed class PlotterView : GraphicsView
{
    public static readonly BindableProperty DataProperty = BindableProperty.Create(
        nameof(Data), typeof(string), typeof(PlotterView), string.Empty,
        propertyChanged: (bindable, _, _) => ((PlotterView)bindable).Invalidate());

    public static readonly BindableProperty KindProperty = BindableProperty.Create(
        nameof(Kind), typeof(string), typeof(PlotterView), "watanabe",
        propertyChanged: (bindable, _, _) => ((PlotterView)bindable).Invalidate());

    private static readonly Color[] Colors =
    [
        Color.FromArgb("#1F2937"),
        Color.FromArgb("#DC2626"),
        Color.FromArgb("#15803D"),
        Color.FromArgb("#2563EB"),
        Color.FromArgb("#A16207"),
        Color.FromArgb("#0E7490"),
    ];

    public PlotterView()
    {
        Drawable = new Renderer(this);
        BackgroundColor = Color.FromArgb("#FFFCF7");
    }

    public string Data
    {
        get => (string)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private sealed class Renderer(PlotterView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            canvas.FillColor = Color.FromArgb("#FFFCF7");
            canvas.FillRectangle(rect);
            DrawGrid(canvas, rect);

            var segments = PlotterParser.Parse(owner.Data, owner.Kind);
            if (segments.Count == 0)
            {
                canvas.FontColor = Color.FromArgb("#8C7C70");
                canvas.FontSize = 13;
                canvas.DrawString("Нет данных для отрисовки", rect,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var segment in segments)
            {
                minX = Math.Min(minX, Math.Min(segment.X0, segment.X1));
                maxX = Math.Max(maxX, Math.Max(segment.X0, segment.X1));
                minY = Math.Min(minY, Math.Min(segment.Y0, segment.Y1));
                maxY = Math.Max(maxY, Math.Max(segment.Y0, segment.Y1));
            }

            float spanX = Math.Max(1, maxX - minX);
            float spanY = Math.Max(1, maxY - minY);
            float availableWidth = Math.Max(1, rect.Width - 32);
            float availableHeight = Math.Max(1, rect.Height - 32);
            float scale = Math.Min(availableWidth / spanX, availableHeight / spanY);
            float offsetX = (rect.Width - spanX * scale) / 2;
            float offsetY = (rect.Height - spanY * scale) / 2;

            canvas.StrokeSize = 1.8f;
            canvas.StrokeLineCap = LineCap.Round;
            for (int color = 0; color < Colors.Length; color++)
            {
                canvas.StrokeColor = Colors[color];
                foreach (var segment in segments)
                {
                    if (segment.Color != color) continue;
                    canvas.DrawLine(
                        offsetX + (segment.X0 - minX) * scale,
                        offsetY + (maxY - segment.Y0) * scale,
                        offsetX + (segment.X1 - minX) * scale,
                        offsetY + (maxY - segment.Y1) * scale);
                }
            }
        }

        private static void DrawGrid(ICanvas canvas, RectF rect)
        {
            canvas.StrokeColor = Color.FromArgb("#EDE5D9");
            canvas.StrokeSize = 1;
            const float step = 40f;
            for (float x = 0; x < rect.Width; x += step) canvas.DrawLine(x, 0, x, rect.Height);
            for (float y = 0; y < rect.Height; y += step) canvas.DrawLine(0, y, rect.Width, y);
        }
    }
}
