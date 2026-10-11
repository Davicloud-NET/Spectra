using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Turns a row's depth into its left margin. The tree is a flat list, so the
/// control does no indenting of its own.
/// </summary>
public sealed class TreeDepthIndentConverter : IValueConverter
{
    /// <summary>Pixels of indent per level of depth.</summary>
    // The chevron column's width, so a child's chevron sits under its parent's.
    public const double PerLevel = 14;

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(value is int depth ? depth * PerLevel : 0, 0, 0, 0);

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A row's indent is never edited.");
}
