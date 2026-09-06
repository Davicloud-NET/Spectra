namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Whether an asset dragged out of the content browser can become a node, and
/// what to say when it cannot.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every refusal here is a SENTENCE, because the alternative is a gesture
/// that ends in nothing.</b> A drag has no keyboard equivalent to fall back on
/// and no menu item greying out beside it: if the drop does nothing and says
/// nothing, the honest reading is that the shell lost the gesture. That is
/// especially true of the viewport-mode refusal, which is a difference between
/// two panes that render an identical picture.
/// </para>
/// <para>
/// <b>Pure, and separate from both the viewport and the window</b>, because a
/// drag gesture cannot be driven headlessly at all: the only part of this
/// decision a test can reach is the part that is a function of its inputs, so
/// that part is written down as one.
/// </para>
/// </remarks>
public static class AssetDropPolicy
{
    /// <summary>
    /// What to tell the user instead of placing <paramref name="payload"/>, or
    /// null when the drop should go ahead.
    /// </summary>
    /// <remarks>
    /// <b>Order matters and follows what the user can act on.</b> No session is
    /// checked first because nothing else is true yet; the viewport mode next,
    /// because it is a property of the whole session rather than of this file;
    /// and the kind last, since "this is not a model" is only worth saying to
    /// somebody whose drop could otherwise have landed.
    /// </remarks>
    /// <param name="payload">What is being dragged.</param>
    /// <param name="hasSession">Whether a project is open with an engine in it.</param>
    /// <param name="viewportAcceptsDrops">
    /// Whether the running viewport is a drop target at all. A native child
    /// window is not: the OS delivers input to the HWND, its messages do not
    /// bubble into Avalonia, and OLE would need an <c>IDropTarget</c> registered
    /// on that window.
    /// </param>
    public static string? Refuse(ContentDragPayload payload, bool hasSession, bool viewportAcceptsDrops)
    {
        if (!hasSession)
            return "Open a project before dropping anything into the viewport.";

        if (!viewportAcceptsDrops)
        {
            return "This viewport is a native child window and cannot take a drop. " +
                "Relaunch with --viewport=composition to drag assets into the scene.";
        }

        if (!CanPlace(payload.Kind))
            return RefuseKind(payload);

        return null;
    }

    /// <summary>
    /// Why this kind has no drop, naming what to reach for instead.
    /// </summary>
    /// <remarks>
    /// <b>A texture gets its own sentence, because it is the near miss.</b>
    /// Somebody dragging a <c>.png</c> onto a wall is asking for exactly what a
    /// material drop does, and "only models and materials can be dropped" tells
    /// them nothing about which of the two files in front of them is which.
    /// Making a material out of a texture is a real verb and is not built, so
    /// the refusal says that rather than implying the file is useless.
    /// </remarks>
    private static string RefuseKind(ContentDragPayload payload) => payload.Kind switch
    {
        ContentKind.Texture =>
            $"{payload.Name} is a texture; drop a material instead. " +
            "A face wears a material file, and making one out of a texture is not built yet.",

        _ => $"{payload.Name} cannot be dropped into the scene; only models and materials can.",
    };

    /// <summary>
    /// Whether a kind has a placement at all.
    /// </summary>
    /// <remarks>
    /// <b>Two kinds and two different gestures behind one predicate.</b> A model
    /// becomes a node at the point under the pointer; a material becomes the
    /// surface of the FACE under it, which needs a plane index off the ray
    /// rather than a position. They share this answer because the question is
    /// only "would letting go here do anything", which the overlay and the drop
    /// both have to agree about.
    /// </remarks>
    public static bool CanPlace(ContentKind kind) =>
        kind is ContentKind.Model or ContentKind.Material;
}
