using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Turns an <see cref="IconLook"/> into its glyph, found in the theme. A key
/// the theme lacks gives null and nothing is drawn.
/// </summary>
public sealed class IconLookGlyphConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IconLook look ? EditorIcons.Glyph(look) : null;

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("An icon is never edited.");
}
