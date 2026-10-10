using Avalonia;
using Avalonia.Controls;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The label side of a Properties row. It is as wide as its label and a gap,
/// up to a limit the row's width sets, and the panel's label column takes the
/// widest one.
/// </summary>
public sealed class RowLabelCell : Decorator
{
    /// <summary>The label column's width while every label is short.</summary>
    public const double Floor = 86;

    // What the narrowest panel leaves an editor. A dropdown's words just fit in it.
    private const double EditorFloor = 110;

    private const double Gap = 8;

    /// <summary>Defines <see cref="RowWidth"/>.</summary>
    public static readonly StyledProperty<double> RowWidthProperty =
        AvaloniaProperty.Register<RowLabelCell, double>(nameof(RowWidth));

    static RowLabelCell() => AffectsMeasure<RowLabelCell>(RowWidthProperty);

    /// <summary>How wide the whole row is, label and editor.</summary>
    public double RowWidth
    {
        get => GetValue(RowWidthProperty);
        set => SetValue(RowWidthProperty, value);
    }

    /// <summary>
    /// The widest the label column may be in a row this wide: never more than
    /// half the row, and never leaving the editor less than the narrowest
    /// panel does.
    /// </summary>
    public static double Limit(double rowWidth) =>
        Math.Max(Floor, Math.Min(rowWidth - EditorFloor, rowWidth / 2));

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } label)
            return default;

        double limit = Math.Min(availableSize.Width, Limit(RowWidth));

        // A label that fits the limit gives up the gap before it trims. One
        // that trims anyway keeps the gap, so its ellipsis does not touch the editor.
        label.Measure(availableSize.WithWidth(double.PositiveInfinity));
        if (label.DesiredSize.Width > limit)
            label.Measure(availableSize.WithWidth(Math.Max(0, limit - Gap)));

        return new Size(Math.Min(label.DesiredSize.Width + Gap, limit), label.DesiredSize.Height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        // At the width it was measured for, or a trimmed label would spread into the gap.
        Child?.Arrange(new Rect(0, 0, Math.Min(Child.DesiredSize.Width, finalSize.Width), finalSize.Height));
        return finalSize;
    }
}
