using SpectraEngine.Editing.Hosting;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// What the viewport draws over the picture while an asset drag is over it.
/// </summary>
/// <param name="Accepts">
/// Whether letting go here would place something. A refusal is still drawn.
/// </param>
/// <param name="Subject">
/// The content-relative path of what would be placed. Empty when refusing.
/// </param>
/// <param name="Reason">
/// Why not, verbatim from <see cref="AssetDropPolicy"/>. Empty when accepting.
/// </param>
/// <param name="IconKey">
/// The glyph beside the verdict: the dragged file's own icon, or the warning
/// when refusing. Empty draws none.
/// </param>
// A record struct so an unchanged prompt compares equal: DragOver fires at
// pointer rate and nearly always carries the same answer.
public readonly record struct ViewportDropPrompt(
    bool IsVisible,
    bool Accepts,
    string Headline,
    string Subject,
    string Reason,
    string Hint = "",
    string IconKey = "")
{
    /// <summary>No drag over the viewport, and nothing drawn.</summary>
    // Empty strings, not null: these bind to TextBlock.Text, where a null
    // leaves the previous value on screen.
    public static ViewportDropPrompt None { get; } =
        new(false, false, string.Empty, string.Empty, string.Empty);

    /// <summary>The icon a refusing prompt wears.</summary>
    public const string RefusingIcon = "IconWarning";

    /// <summary>
    /// What to draw for <paramref name="payload"/> hovering over the viewport,
    /// or <see cref="None"/> when nothing should be.
    /// </summary>
    /// <param name="viewportAcceptsDrops">
    /// <see cref="Viewport.IEngineViewport.AcceptsAssetDrops"/>. False hides the
    /// prompt entirely: a native child composites above anything drawn here.
    /// </param>
    /// <param name="scope">What a material would cover if it landed now.</param>
    public static ViewportDropPrompt For(
        ContentDragPayload? payload, bool hasSession, bool viewportAcceptsDrops,
        MaterialDropScope scope = MaterialDropScope.Face)
    {
        if (payload is null || !hasSession || !viewportAcceptsDrops)
            return None;

        if (AssetDropPolicy.Refuse(payload, hasSession, viewportAcceptsDrops) is { } refusal)
            return new ViewportDropPrompt(true, false, "Cannot place", string.Empty, refusal, "", RefusingIcon);

        // The file's icon, as the Content panel shows it.
        string icon = EditorIcons.ForFile(payload.Kind).Icon;

        if (payload.Kind == ContentKind.Material)
        {
            return new ViewportDropPrompt(
                true, true, "Drop to paint", payload.ContentPath, string.Empty,
                scope == MaterialDropScope.Face
                    ? "this face; hold Ctrl for the whole block"
                    : "the whole block; release Ctrl for one face",
                icon);
        }

        if (payload.Kind == ContentKind.Sound)
        {
            return new ViewportDropPrompt(
                true, true, "Drop to place", payload.ContentPath, string.Empty,
                "as a sound that plays this file", icon);
        }

        return new ViewportDropPrompt(true, true, "Drop to place", payload.ContentPath, string.Empty, IconKey: icon);
    }
}
