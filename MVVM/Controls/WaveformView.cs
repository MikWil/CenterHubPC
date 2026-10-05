using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CenterHubNew.MVVM.Controls
{
    /// <summary>
    /// A read-only waveform strip: one thin bar per few pixels, the parts trimmed away drawn dimmed,
    /// and a thin playhead. Peaks are 0..1 and are re-bucketed to the control's width, so the same
    /// array works at any size. Bind the brushes to theme tokens with DynamicResource.
    /// </summary>
    public sealed class WaveformView : Control
    {
        private const double BarWidth = 2;
        private const double BarPitch = 3;

        /// <summary>Peak per bucket (0..1) over the whole, untrimmed take.</summary>
        public static readonly StyledProperty<float[]?> PeaksProperty =
            AvaloniaProperty.Register<WaveformView, float[]?>(nameof(Peaks));

        /// <summary>Fraction (0..1) of the take cut from the start.</summary>
        public static readonly StyledProperty<double> TrimStartProperty =
            AvaloniaProperty.Register<WaveformView, double>(nameof(TrimStart));

        /// <summary>Fraction (0..1) of the take cut from the end.</summary>
        public static readonly StyledProperty<double> TrimEndProperty =
            AvaloniaProperty.Register<WaveformView, double>(nameof(TrimEnd));

        /// <summary>Playhead, 0..1 of the TRIMMED loop; a negative value hides it.</summary>
        public static readonly StyledProperty<double> PositionProperty =
            AvaloniaProperty.Register<WaveformView, double>(nameof(Position), -1.0);

        public static readonly StyledProperty<IBrush?> WaveBrushProperty =
            AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(WaveBrush));

        public static readonly StyledProperty<IBrush?> DimBrushProperty =
            AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(DimBrush));

        public static readonly StyledProperty<IBrush?> PlayheadBrushProperty =
            AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(PlayheadBrush));

        static WaveformView()
        {
            AffectsRender<WaveformView>(
                PeaksProperty, TrimStartProperty, TrimEndProperty, PositionProperty,
                WaveBrushProperty, DimBrushProperty, PlayheadBrushProperty);
        }

        public float[]? Peaks
        {
            get => GetValue(PeaksProperty);
            set => SetValue(PeaksProperty, value);
        }

        public double TrimStart
        {
            get => GetValue(TrimStartProperty);
            set => SetValue(TrimStartProperty, value);
        }

        public double TrimEnd
        {
            get => GetValue(TrimEndProperty);
            set => SetValue(TrimEndProperty, value);
        }

        public double Position
        {
            get => GetValue(PositionProperty);
            set => SetValue(PositionProperty, value);
        }

        public IBrush? WaveBrush
        {
            get => GetValue(WaveBrushProperty);
            set => SetValue(WaveBrushProperty, value);
        }

        public IBrush? DimBrush
        {
            get => GetValue(DimBrushProperty);
            set => SetValue(DimBrushProperty, value);
        }

        public IBrush? PlayheadBrush
        {
            get => GetValue(PlayheadBrushProperty);
            set => SetValue(PlayheadBrushProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            double width = Bounds.Width;
            double height = Bounds.Height;
            if (width <= 0 || height <= 0) return;

            var peaks = Peaks;
            var wave = WaveBrush ?? Brushes.White;
            var dim = DimBrush ?? Brushes.Gray;
            double cutStart = Clamp01(TrimStart);
            double cutEnd = Clamp01(TrimEnd);
            double mid = height / 2;

            if (peaks is { Length: > 0 })
            {
                int columns = Math.Max(1, (int)(width / BarPitch));
                double pitch = width / columns;
                int count = peaks.Length;

                for (int c = 0; c < columns; c++)
                {
                    int from = (int)((long)c * count / columns);
                    int to = Math.Max(from + 1, (int)((long)(c + 1) * count / columns));
                    to = Math.Min(to, count);

                    float peak = 0;
                    for (int i = from; i < to; i++)
                        if (peaks[i] > peak) peak = peaks[i];

                    double barHeight = Math.Clamp(peak, 0f, 1f) * (height - 6);
                    barHeight = Math.Max(2, barHeight);

                    double center = (c + 0.5) / columns;
                    bool trimmedAway = center < cutStart || center > 1.0 - cutEnd;
                    var rect = new Rect(c * pitch + (pitch - BarWidth) / 2, mid - barHeight / 2, BarWidth, barHeight);
                    context.DrawRectangle(trimmedAway ? dim : wave, null, rect, 1, 1);
                }
            }

            double position = Position;
            if (position >= 0 && PlayheadBrush is { } playhead)
            {
                double kept = Math.Max(0.0, 1.0 - cutStart - cutEnd);
                double x = (cutStart + Math.Min(1.0, position) * kept) * width;
                context.DrawRectangle(playhead, null, new Rect(Math.Clamp(x - 1, 0, Math.Max(0, width - 2)), 0, 2, height));
            }
        }

        private static double Clamp01(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0.0, 1.0);
    }
}
