using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Hosting;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// What the selection outline should call attention to this frame, beyond the
/// selection itself.
/// </summary>
/// <param name="Hovered">What the cursor is over, or null.</param>
/// <param name="HoveredPlane">Which of its planes, or -1. Only meaningful for a brush.</param>
/// <param name="MaterialDrag">What a material drag over the viewport would paint, or null when none is in flight.</param>
/// <param name="PickedFaceNode">The brush whose face is picked, or null.</param>
/// <param name="PickedFacePlane">Which of its planes, or -1.</param>
public readonly record struct OutlineFocus(
    SceneNode? Hovered,
    int HoveredPlane,
    MaterialDropScope? MaterialDrag,
    SceneNode? PickedFaceNode = null,
    int PickedFacePlane = -1);
