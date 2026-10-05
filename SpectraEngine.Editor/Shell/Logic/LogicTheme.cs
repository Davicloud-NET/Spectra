using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Reads the theme's tokens for the graph. A key the theme lacks is drawn in
// a colour nobody would choose and remembered, so a test can name it.
internal static class LogicTheme
{
    private static readonly List<string> MissingKeys = [];

    public static IReadOnlyList<string> Missing => MissingKeys;

    public static IImmutableBrush Brush(string key) =>
        TryFind(key, out IBrush? brush) ? brush.ToImmutable() : Brushes.Magenta;

    public static FontFamily Font(string key) =>
        TryFind(key, out FontFamily? family) ? family : FontFamily.Default;

    public static double FontSize(string key) =>
        TryFind(key, out double size) ? size : TextElement.FontSizeProperty.GetDefaultValue(typeof(TextElement));

    public static double Opacity(string key) => TryFind(key, out double opacity) ? opacity : 1;

    public static Geometry? Icon(string key) => TryFind(key, out Geometry? icon) ? icon : null;

    private static bool TryFind<T>(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? found)
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
