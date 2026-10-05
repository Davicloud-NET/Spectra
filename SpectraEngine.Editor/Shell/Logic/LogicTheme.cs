using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Editor.Shell.Logic;

// Reads the theme's tokens for the graph. A key the theme lacks draws nothing
// and is remembered, so a test can name it: a failed lookup raises nothing.
internal static class LogicTheme
{
    private static readonly List<string> MissingKeys = [];

    public static IReadOnlyList<string> Missing => MissingKeys;

    public static IImmutableBrush Brush(string key) =>
        TryFind(key, out IBrush? brush) ? brush.ToImmutable() : Brushes.Transparent;

    public static FontFamily Font(string key) =>
        TryFind(key, out FontFamily? family) ? family : FontFamily.Default;

    public static double FontSize(string key) =>
        TryFind(key, out double size) ? size : TextElement.FontSizeProperty.GetDefaultValue(typeof(TextElement));

    // A stroke width, a radius or a length. One when the theme lacks it, so
    // nothing divides by nothing.
    public static double Size(string key) => TryFind(key, out double size) ? size : 1;

    public static Geometry? Icon(string key) => TryFind(key, out Geometry? icon) ? icon : null;

    private static bool TryFind<T>(string key, [NotNullWhen(true)] out T? found)
    {
        if (Application.Current?.TryFindResource(key, out object? value) == true && value is T typed)
        {
            found = typed;
            return true;
        }

        if (!MissingKeys.Contains(key))
            MissingKeys.Add(key);

        found = default;
        return false;
    }
}
