namespace SpectraEngine.Editor.Shell;

/// <summary>
/// An icon and the colour it is drawn in, each named by its key in the theme.
/// </summary>
/// <param name="Icon">The key of the glyph, which is its file's name.</param>
/// <param name="Tint">The key of the brush that strokes it.</param>
public readonly record struct IconLook(string Icon, string Tint);
