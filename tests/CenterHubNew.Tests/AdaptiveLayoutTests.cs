using Avalonia;
using Avalonia.Controls;
using CenterHubNew.MVVM.Controls;
using Xunit;
using Control = Avalonia.Controls.Control;
using Size = Avalonia.Size;
using Rect = Avalonia.Rect;

namespace CenterHubNew.Tests;

public class AdaptiveLayoutTests
{
    // ---------- ComputeColumns ----------

    [Theory]
    [InlineData(300, 1)]
    [InlineData(320, 1)]
    [InlineData(651, 1)]
    [InlineData(652, 2)]
    [InlineData(660, 2)]
    [InlineData(983, 2)]
    [InlineData(984, 3)]
    [InlineData(1000, 3)]
    [InlineData(5000, 3)]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    public void ComputeColumns_FollowsWidth(double width, int expected)
    {
        Assert.Equal(expected, AdaptiveColumnsPanel.ComputeColumns(width, 320, 3, 12));
    }

    [Fact]
    public void ComputeColumns_RespectsMaxColumns()
    {
        Assert.Equal(2, AdaptiveColumnsPanel.ComputeColumns(5000, 320, 2, 12));
        Assert.Equal(1, AdaptiveColumnsPanel.ComputeColumns(5000, 320, 1, 12));
        Assert.Equal(1, AdaptiveColumnsPanel.ComputeColumns(5000, 320, 0, 12)); // nonsense max -> 1
        Assert.Equal(5, AdaptiveColumnsPanel.ComputeColumns(5000, 320, 5, 12));
    }

    [Fact]
    public void ComputeColumns_InfiniteWidthUsesMaxColumns()
    {
        Assert.Equal(3, AdaptiveColumnsPanel.ComputeColumns(double.PositiveInfinity, 320, 3, 12));
        Assert.Equal(4, AdaptiveColumnsPanel.ComputeColumns(double.PositiveInfinity, 320, 4, 12));
    }

    [Fact]
    public void ComputeColumns_SpacingCountsBetweenColumns()
    {
        // two 100 wide columns + 50 gap = 250
        Assert.Equal(1, AdaptiveColumnsPanel.ComputeColumns(249, 100, 4, 50));
        Assert.Equal(2, AdaptiveColumnsPanel.ComputeColumns(250, 100, 4, 50));
        Assert.Equal(3, AdaptiveColumnsPanel.ComputeColumns(400, 100, 4, 50));
    }

    // ---------- Panel layout (no window) ----------

    private static Border Card(double minHeight) => new Border { MinHeight = minHeight };

    private static AdaptiveColumnsPanel Layout(AdaptiveColumnsPanel panel, double width, double height = 2000)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, height));
        return panel;
    }

    private static AdaptiveColumnsPanel PanelWith(params Control[] children)
    {
        var panel = new AdaptiveColumnsPanel(); // defaults: min 320, max 3, spacing 12
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    [Fact]
    public void Panel_ThreeColumns_RowHeightIsTallestChild()
    {
        var a = Card(50);
        var b = Card(80);
        var c = Card(30);
        var d = Card(40);
        var panel = Layout(PanelWith(a, b, c, d), 984); // 3 x 320 + 2 x 12

        Assert.Equal(3, panel.CurrentColumns);

        // Row 0: a, b, c at x = 0, 332, 664, equal widths of 320, all as tall as the tallest (80)
        Assert.Equal(0, a.Bounds.X, 1);
        Assert.Equal(332, b.Bounds.X, 1);
        Assert.Equal(664, c.Bounds.X, 1);
        foreach (var child in new[] { a, b, c })
        {
            Assert.Equal(0, child.Bounds.Y, 1);
            Assert.Equal(320, child.Bounds.Width, 1);
            Assert.Equal(80, child.Bounds.Height, 1);
        }

        // Row 1: d starts a new row after 80 + 12 spacing
        Assert.Equal(0, d.Bounds.X, 1);
        Assert.Equal(92, d.Bounds.Y, 1);
        Assert.Equal(320, d.Bounds.Width, 1);
        Assert.Equal(40, d.Bounds.Height, 1);
    }

    [Fact]
    public void Panel_DesiredHeight_IsRowsPlusSpacing()
    {
        var panel = PanelWith(Card(50), Card(80), Card(30), Card(40));
        panel.Measure(new Size(984, double.PositiveInfinity));
        Assert.Equal(80 + 12 + 40, panel.DesiredSize.Height, 1);
        Assert.Equal(984, panel.DesiredSize.Width, 1);
    }

    [Fact]
    public void Panel_TwoColumns_WhenNarrower()
    {
        var a = Card(50);
        var b = Card(60);
        var c = Card(70);
        var panel = Layout(PanelWith(a, b, c), 652); // 2 x 320 + 12

        Assert.Equal(2, panel.CurrentColumns);
        Assert.Equal(0, a.Bounds.X, 1);
        Assert.Equal(332, b.Bounds.X, 1);
        Assert.Equal(320, a.Bounds.Width, 1);
        Assert.Equal(320, b.Bounds.Width, 1);
        Assert.Equal(60, a.Bounds.Height, 1);
        Assert.Equal(0, c.Bounds.X, 1);
        Assert.Equal(72, c.Bounds.Y, 1); // 60 + 12
    }

    [Fact]
    public void Panel_SingleColumn_StacksChildrenFullWidth()
    {
        var a = Card(50);
        var b = Card(60);
        var panel = Layout(PanelWith(a, b), 300);

        Assert.Equal(1, panel.CurrentColumns);
        Assert.Equal(0, a.Bounds.X, 1);
        Assert.Equal(300, a.Bounds.Width, 1);
        Assert.Equal(0, a.Bounds.Y, 1);
        Assert.Equal(0, b.Bounds.X, 1);
        Assert.Equal(300, b.Bounds.Width, 1);
        Assert.Equal(62, b.Bounds.Y, 1);
    }

    [Fact]
    public void Panel_ColumnSpan_TakesSeveralColumns()
    {
        var a = Card(50);
        var b = Card(50);
        var c = Card(50);
        var d = Card(50);
        AdaptiveColumnsPanel.SetColumnSpan(a, 2);
        var panel = Layout(PanelWith(a, b, c, d), 984);

        // Row 0: a spans columns 0-1 (2 x 320 + 12 = 652), b in column 2. Row 1: c, d.
        Assert.Equal(0, a.Bounds.X, 1);
        Assert.Equal(652, a.Bounds.Width, 1);
        Assert.Equal(664, b.Bounds.X, 1);
        Assert.Equal(320, b.Bounds.Width, 1);
        Assert.Equal(0, c.Bounds.X, 1);
        Assert.Equal(62, c.Bounds.Y, 1);
        Assert.Equal(332, d.Bounds.X, 1);
        Assert.Equal(62, d.Bounds.Y, 1);
    }

    [Fact]
    public void Panel_ColumnSpan_DoesNotFitInRow_StartsNewRow()
    {
        var a = Card(50);
        var b = Card(50);
        var c = Card(50);
        AdaptiveColumnsPanel.SetColumnSpan(b, 2);
        var panel = Layout(PanelWith(a, b, c), 984);

        // a in column 0, b (span 2) does fit in columns 1-2
        Assert.Equal(332, b.Bounds.X, 1);
        Assert.Equal(652, b.Bounds.Width, 1);
        Assert.Equal(0, b.Bounds.Y, 1);
        Assert.Equal(62, c.Bounds.Y, 1);

        // with c spanning 3 columns it must wrap to its own row
        AdaptiveColumnsPanel.SetColumnSpan(c, 3);
        Layout(panel, 984);
        Assert.Equal(0, c.Bounds.X, 1);
        Assert.Equal(984, c.Bounds.Width, 1);
        Assert.Equal(62, c.Bounds.Y, 1);
    }

    [Fact]
    public void Panel_ColumnSpan_IsClampedToColumnCount()
    {
        var a = Card(50);
        AdaptiveColumnsPanel.SetColumnSpan(a, 3);
        var panel = Layout(PanelWith(a), 300); // single column

        Assert.Equal(1, panel.CurrentColumns);
        Assert.Equal(0, a.Bounds.X, 1);
        Assert.Equal(300, a.Bounds.Width, 1);
    }

    [Fact]
    public void Panel_FullRow_SpansEveryColumnOnItsOwnRow()
    {
        var a = Card(50);
        var board = Card(100);
        var b = Card(40);
        var c = Card(40);
        AdaptiveColumnsPanel.SetFullRow(board, true);
        var panel = Layout(PanelWith(a, board, b, c), 984);

        // a alone on row 0, board on row 1 across all 3 columns, b + c on row 2
        Assert.Equal(0, a.Bounds.Y, 1);
        Assert.Equal(320, a.Bounds.Width, 1);
        Assert.Equal(0, board.Bounds.X, 1);
        Assert.Equal(984, board.Bounds.Width, 1);
        Assert.Equal(62, board.Bounds.Y, 1);
        Assert.Equal(100, board.Bounds.Height, 1);
        Assert.Equal(174, b.Bounds.Y, 1); // 62 + 100 + 12
        Assert.Equal(174, c.Bounds.Y, 1);
        Assert.Equal(332, c.Bounds.X, 1);
        Assert.Equal(3, panel.CurrentColumns);
    }

    [Fact]
    public void Panel_MaxColumns_CapsColumns()
    {
        var a = Card(50);
        var b = Card(50);
        var c = Card(50);
        var panel = PanelWith(a, b, c);
        panel.MaxColumns = 2;
        Layout(panel, 2000);

        Assert.Equal(2, panel.CurrentColumns);
        Assert.Equal(994, a.Bounds.Width, 1); // (2000 - 12) / 2
        Assert.Equal(62, c.Bounds.Y, 1);
    }

    [Fact]
    public void Panel_HiddenChildren_TakeNoSpace()
    {
        var a = Card(50);
        var hidden = Card(50);
        hidden.IsVisible = false;
        var c = Card(50);
        Layout(PanelWith(a, hidden, c), 984);

        Assert.Equal(332, c.Bounds.X, 1); // directly next to a
        Assert.Equal(0, c.Bounds.Y, 1);
    }

    [Fact]
    public void Panel_InfiniteWidth_UsesMaxColumnsAtMinWidth()
    {
        var panel = PanelWith(Card(50), Card(50), Card(50));
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(984, panel.DesiredSize.Width, 1); // 3 x 320 + 2 x 12
        Assert.Equal(50, panel.DesiredSize.Height, 1);
    }

    [Fact]
    public void Panel_ReflowsWhenWidthChanges()
    {
        var a = Card(50);
        var b = Card(50);
        var c = Card(50);
        var panel = PanelWith(a, b, c);

        Layout(panel, 984);
        Assert.Equal(3, panel.CurrentColumns);

        Layout(panel, 652);
        Assert.Equal(2, panel.CurrentColumns);
        Assert.Equal(62, c.Bounds.Y, 1); // second row at 50 + 12

        Layout(panel, 300);
        Assert.Equal(1, panel.CurrentColumns);
        Assert.Equal(124, c.Bounds.Y, 1); // 2 x (50 + 12)
    }
}
