namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Whether an asset dragged out of the content browser can be dropped into the
/// scene, and what to say when it cannot.
/// </summary>
// Pure so tests can reach it; a drag gesture cannot be driven headlessly.
public static class AssetDropPolicy
{
    /// <summary>
    /// What to tell the user instead of placing <paramref name="payload"/>, or
    /// null when the drop should go ahead.
    /// </summary>
    /// <param name="viewportAcceptsDrops">False for a native child viewport, which gets no Avalonia drag events.</param>
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

    private static string RefuseKind(ContentDragPayload payload) => payload.Kind switch
    {
        ContentKind.Texture =>
            $"{payload.Name} is a texture; drop a material instead. " +
            "A face wears a material file, and making one out of a texture is not built yet.",

        _ => $"{payload.Name} cannot be dropped into the scene; " +
            "only models, materials, sounds and scripts can.",
    };

    /// <summary>Whether dropping this kind into the scene does anything.</summary>
    public static bool CanPlace(ContentKind kind) =>
        kind is ContentKind.Model or ContentKind.Material or ContentKind.Sound or ContentKind.Script;

    /// <summary>
    /// Whether this kind can be dropped onto a node in the scene tree. Only a
    /// script: it goes under a node, and where the others land is a point in
    /// the viewport.
    /// </summary>
    public static bool CanDropOnNode(ContentKind kind) => kind == ContentKind.Script;

    /// <summary>
    /// What to tell the user instead of adding <paramref name="payload"/> under
    /// a node of the scene tree, or null when the drop should go ahead.
    /// </summary>
    public static string? RefuseOnNode(ContentDragPayload payload, bool hasSession, bool isPlaying)
    {
        if (!hasSession)
            return "Open a project before dropping anything into the scene.";

        if (!CanDropOnNode(payload.Kind))
        {
            string where = CanPlace(payload.Kind) ? " Drop it into the viewport instead." : string.Empty;
            return $"{payload.Name} cannot be dropped on a node; only a script can.{where}";
        }

        if (isPlaying)
            return $"{payload.Name} was not added: the level is playing. Stop it first.";

        return null;
    }
}
