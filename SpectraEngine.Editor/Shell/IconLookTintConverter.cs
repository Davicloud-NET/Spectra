using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Turns an <see cref="IconLook"/> into the brush that tints its glyph, found
/// in the theme.
/// </summary>
public sealed class IconLookTintConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IconLook look ? EditorIcons.Brush(look) : null;

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("An icon's tint is never edited.");
}
