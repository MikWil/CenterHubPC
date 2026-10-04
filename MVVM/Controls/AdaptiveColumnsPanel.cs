using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace CenterHubNew.MVVM.Controls
{
    /// <summary>
    /// A panel that lays its children out in N equal-width columns, where N follows the available
    /// width. It is the backbone of every responsive CenterHub page: put the page's cards in one and
    /// they reflow from 3 columns to 2 to 1 as the window is resized.
    /// </summary>
    /// <remarks>
    /// <para>Children are placed row-major, left to right. A child that does not fit in the remaining
    /// columns of the current row starts a new row. Every row is as tall as its tallest child and each
    /// child is arranged with the full row height, so cards in one row line up (give a child a fixed
    /// <c>Height</c> to opt out). Children with <c>IsVisible="False"</c> take no space.</para>
    /// <para>A child may span several columns with <see cref="ColumnSpanProperty"/> (clamped to the
    /// current column count) or always take a whole row with <see cref="FullRowProperty"/>.</para>
    /// <para>The column count is computed by <see cref="ComputeColumns"/>.</para>
    /// </remarks>
    public class AdaptiveColumnsPanel : Panel
    {
        /// <summary>Defines the <see cref="MinColumnWidth"/> property.</summary>
        public static readonly StyledProperty<double> MinColumnWidthProperty =
            AvaloniaProperty.Register<AdaptiveColumnsPanel, double>(nameof(MinColumnWidth), 320d);

        /// <summary>Defines the <see cref="MaxColumns"/> property.</summary>
        public static readonly StyledProperty<int> MaxColumnsProperty =
            AvaloniaProperty.Register<AdaptiveColumnsPanel, int>(nameof(MaxColumns), 3);

        /// <summary>Defines the <see cref="ColumnSpacing"/> property.</summary>
        public static readonly StyledProperty<double> ColumnSpacingProperty =
            AvaloniaProperty.Register<AdaptiveColumnsPanel, double>(nameof(ColumnSpacing), 12d);

        /// <summary>Defines the <see cref="RowSpacing"/> property.</summary>
        public static readonly StyledProperty<double> RowSpacingProperty =
            AvaloniaProperty.Register<AdaptiveColumnsPanel, double>(nameof(RowSpacing), 12d);

        /// <summary>Defines the <see cref="CurrentColumns"/> property.</summary>
        public static readonly DirectProperty<AdaptiveColumnsPanel, int> CurrentColumnsProperty =
            AvaloniaProperty.RegisterDirect<AdaptiveColumnsPanel, int>(
                nameof(CurrentColumns), o => o.CurrentColumns);

        /// <summary>
        /// Attached property: how many columns a child spans (default 1, clamped to the current
        /// column count). Usage: <c>controls:AdaptiveColumnsPanel.ColumnSpan="2"</c>.
        /// </summary>
        public static readonly AttachedProperty<int> ColumnSpanProperty =
            AvaloniaProperty.RegisterAttached<AdaptiveColumnsPanel, Control, int>("ColumnSpan", 1);

        /// <summary>
        /// Attached property: when true the child always spans every column (and so always sits on a
        /// row of its own). Usage: <c>controls:AdaptiveColumnsPanel.FullRow="True"</c>.
        /// </summary>
        public static readonly AttachedProperty<bool> FullRowProperty =
            AvaloniaProperty.RegisterAttached<AdaptiveColumnsPanel, Control, bool>("FullRow");

        static AdaptiveColumnsPanel()
        {
            AffectsMeasure<AdaptiveColumnsPanel>(
                MinColumnWidthProperty, MaxColumnsProperty, ColumnSpacingProperty, RowSpacingProperty);
            AffectsParentMeasure<AdaptiveColumnsPanel>(ColumnSpanProperty, FullRowProperty);
        }

        private int _currentColumns = 1;

        // Columns / column width the children were last measured with (re-measure in Arrange if they differ).
        private int _measuredColumns = -1;
        private double _measuredColumnWidth = double.NaN;

        /// <summary>Minimum width of one column in DIPs. The column count drops until columns are at least this wide. Default 320.</summary>
        public double MinColumnWidth
        {
            get => GetValue(MinColumnWidthProperty);
            set => SetValue(MinColumnWidthProperty, value);
        }

        /// <summary>Upper limit for the number of columns. Default 3.</summary>
        public int MaxColumns
        {
            get => GetValue(MaxColumnsProperty);
            set => SetValue(MaxColumnsProperty, value);
        }

        /// <summary>Horizontal gap between columns in DIPs. Default 12.</summary>
        public double ColumnSpacing
        {
            get => GetValue(ColumnSpacingProperty);
            set => SetValue(ColumnSpacingProperty, value);
        }

        /// <summary>Vertical gap between rows in DIPs. Default 12.</summary>
        public double RowSpacing
        {
            get => GetValue(RowSpacingProperty);
            set => SetValue(RowSpacingProperty, value);
        }

        /// <summary>The number of columns used by the last arrange pass (read-only; useful for bindings and tests).</summary>
        public int CurrentColumns
        {
            get => _currentColumns;
            private set => SetAndRaise(CurrentColumnsProperty, ref _currentColumns, value);
        }

        /// <summary>Gets the number of columns a child spans.</summary>
        public static int GetColumnSpan(Control control) => control.GetValue(ColumnSpanProperty);

        /// <summary>Sets the number of columns a child spans.</summary>
        public static void SetColumnSpan(Control control, int value) => control.SetValue(ColumnSpanProperty, value);

        /// <summary>Gets whether a child always spans every column.</summary>
        public static bool GetFullRow(Control control) => control.GetValue(FullRowProperty);

        /// <summary>Sets whether a child always spans every column.</summary>
        public static void SetFullRow(Control control, bool value) => control.SetValue(FullRowProperty, value);

        /// <summary>
        /// Computes the column count for a width:
        /// <c>clamp(floor((width + spacing) / (minColumnWidth + spacing)), 1, maxColumns)</c>.
        /// Infinite (or NaN) width yields <paramref name="maxColumns"/>; a non-positive width yields 1.
        /// </summary>
        /// <param name="availableWidth">Width available to the panel.</param>
        /// <param name="minColumnWidth">Minimum width of a column.</param>
        /// <param name="maxColumns">Maximum number of columns (values below 1 are treated as 1).</param>
        /// <param name="spacing">Gap between columns.</param>
        public static int ComputeColumns(double availableWidth, double minColumnWidth, int maxColumns, double spacing)
        {
            int max = Math.Max(1, maxColumns);
            if (double.IsNaN(availableWidth) || double.IsPositiveInfinity(availableWidth))
                return max;
            if (availableWidth <= 0)
                return 1;

            double gap = Math.Max(0, spacing);
            double min = Math.Max(0, minColumnWidth);
            double unit = min + gap;
            if (unit <= 0)
                return max;

            double raw = Math.Floor((availableWidth + gap) / unit);
            if (raw < 1) return 1;
            if (raw > max) return max;
            return (int)raw;
        }

        private readonly struct Placement
        {
            public Placement(Control child, int row, int column, int span)
            {
                Child = child;
                Row = row;
                Column = column;
                Span = span;
            }

            public Control Child { get; }
            public int Row { get; }
            public int Column { get; }
            public int Span { get; }
        }

        private double ColumnWidthFor(double width, int columns)
        {
            if (double.IsInfinity(width) || double.IsNaN(width))
                return Math.Max(0, MinColumnWidth);
            double gap = Math.Max(0, ColumnSpacing);
            return Math.Max(0, (width - gap * (columns - 1)) / columns);
        }

        private double SpanWidth(double columnWidth, int span)
            => columnWidth * span + Math.Max(0, ColumnSpacing) * (span - 1);

        private List<Placement> Place(int columns)
        {
            var result = new List<Placement>(Children.Count);
            int row = 0, col = 0;
            foreach (Control child in Children)
            {
                if (!child.IsVisible)
                    continue;

                int span = GetFullRow(child) ? columns : Math.Clamp(GetColumnSpan(child), 1, columns);
                if (col + span > columns)
                {
                    row++;
                    col = 0;
                }

                result.Add(new Placement(child, row, col, span));
                col += span;
                if (col >= columns)
                {
                    row++;
                    col = 0;
                }
            }

            return result;
        }

        private void MeasureChildren(List<Placement> placements, double columnWidth)
        {
            foreach (var p in placements)
                p.Child.Measure(new Size(SpanWidth(columnWidth, p.Span), double.PositiveInfinity));
        }

        private static double[] RowHeights(List<Placement> placements)
        {
            int rows = placements.Count == 0 ? 0 : placements[^1].Row + 1;
            var heights = new double[rows];
            foreach (var p in placements)
                heights[p.Row] = Math.Max(heights[p.Row], p.Child.DesiredSize.Height);
            return heights;
        }

        /// <inheritdoc />
        protected override Size MeasureOverride(Size availableSize)
        {
            int columns = ComputeColumns(availableSize.Width, MinColumnWidth, MaxColumns, ColumnSpacing);
            double columnWidth = ColumnWidthFor(availableSize.Width, columns);
            var placements = Place(columns);
            MeasureChildren(placements, columnWidth);
            _measuredColumns = columns;
            _measuredColumnWidth = columnWidth;

            var heights = RowHeights(placements);
            double total = 0;
            for (int i = 0; i < heights.Length; i++)
                total += heights[i] + (i > 0 ? Math.Max(0, RowSpacing) : 0);

            double width = double.IsInfinity(availableSize.Width) || double.IsNaN(availableSize.Width)
                ? SpanWidth(columnWidth, columns)
                : availableSize.Width;
            return new Size(width, total);
        }

        /// <inheritdoc />
        protected override Size ArrangeOverride(Size finalSize)
        {
            int columns = ComputeColumns(finalSize.Width, MinColumnWidth, MaxColumns, ColumnSpacing);
            double columnWidth = ColumnWidthFor(finalSize.Width, columns);
            var placements = Place(columns);

            // The arrange width can differ from the measure width (e.g. Stretch inside a parent that
            // gives more room); in that case children must be measured again at the final column width.
            if (columns != _measuredColumns || Math.Abs(columnWidth - _measuredColumnWidth) > 0.01)
            {
                MeasureChildren(placements, columnWidth);
                _measuredColumns = columns;
                _measuredColumnWidth = columnWidth;
            }

            var heights = RowHeights(placements);
            var tops = new double[heights.Length];
            double y = 0;
            for (int i = 0; i < heights.Length; i++)
            {
                tops[i] = y;
                y += heights[i] + Math.Max(0, RowSpacing);
            }

            double step = columnWidth + Math.Max(0, ColumnSpacing);
            foreach (var p in placements)
            {
                p.Child.Arrange(new Rect(
                    p.Column * step,
                    tops[p.Row],
                    SpanWidth(columnWidth, p.Span),
                    heights[p.Row]));
            }

            CurrentColumns = columns;
            return finalSize;
        }
    }
}
