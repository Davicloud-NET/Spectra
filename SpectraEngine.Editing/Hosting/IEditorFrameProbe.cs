using System.Numerics;

namespace SpectraEngine.Editing.Hosting;

/// <summary>
/// Something a host hangs off the editor's frame: it runs once per
/// <see cref="SceneEditorHost.Update"/>, after the real input frame has been
/// consumed, and may drive the scene itself. The demo's editing self-test is one.
/// </summary>
public interface IEditorFrameProbe
{
    /// <summary>Runs one frame of the probe, on the render thread inside the editor's update.</summary>
    /// <param name="deltaTime">Seconds since the previous frame.</param>
    /// <param name="viewportSize">The viewport in pixels.</param>
    /// <param name="viewportIdle">
    /// False while a gesture, grab or marquee is live. A probe that touches the
    /// scene must then do nothing.
    /// </param>
    void Update(double deltaTime, Vector2 viewportSize, bool viewportIdle);
}
