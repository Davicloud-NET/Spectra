using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SpectraEngine.Core.Scene;
using System;
using System.Globalization;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Turns a <see cref="SceneNodeKind"/> into the row's glyph, looked up in the
/// application's resources. An unresolved key gives null and the row shows no icon.
/// </summary>
public sealed class SceneNodeKindIconConverter : IValueConverter
{
    /// <summary>The shared instance XAML binds to.</summary>
    public static SceneNodeKindIconConverter Instance { get; } = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is SceneNodeKind kind
            ? kind switch
            {
                SceneNodeKind.Group => "IconGroup",
                SceneNodeKind.Mesh => "IconMesh",
                SceneNodeKind.BrushWorld => "IconBrushWorld",
                SceneNodeKind.BrushPart => "IconBrushPart",
                SceneNodeKind.BrushSubtractive => "IconBrushSubtractive",
                SceneNodeKind.Light => "IconLight",
                SceneNodeKind.Entity => "IconEntity",
                _ => "IconEmpty",
            }
            : "IconEmpty";

        return Lookup<Geometry>(key);
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A row's icon is never edited.");

    internal static T? Lookup<T>(string key) where T : class =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out object? found)
            ? found as T
            : null;
}

/// <summary>The tint for a node kind's glyph. See <see cref="SceneNodeKindIconConverter"/>.</summary>
public sealed class SceneNodeKindBrushConverter : IValueConverter
{
    /// <summary>The shared instance XAML binds to.</summary>
    public static SceneNodeKindBrushConverter Instance { get; } = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is SceneNodeKind kind
            ? kind switch
            {
                SceneNodeKind.Group => "SpectraKindGroup",
                SceneNodeKind.Mesh => "SpectraKindMesh",
                SceneNodeKind.BrushWorld => "SpectraKindBrushWorld",
                SceneNodeKind.BrushPart => "SpectraKindBrushPart",
                SceneNodeKind.BrushSubtractive => "SpectraKindBrushSubtractive",
                SceneNodeKind.Light => "SpectraKindLight",
                SceneNodeKind.Entity => "SpectraKindEntity",
                _ => "SpectraTextMuted",
            }
            : "SpectraTextMuted";

        return SceneNodeKindIconConverter.Lookup<IBrush>(key);
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A row's tint is never edited.");
}

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
