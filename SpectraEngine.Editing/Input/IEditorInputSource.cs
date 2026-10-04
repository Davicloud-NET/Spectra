namespace SpectraEngine.Editing.Input;

/// <summary>
/// Produces one <see cref="EditorInputFrame"/> per frame for the editing layer.
/// Each host supplies its own. Called once per frame on the render thread.
/// </summary>
public interface IEditorInputSource
{
    /// <summary>
    /// Snapshots the current input state for a frame of
    /// <paramref name="deltaTime"/> seconds.
    /// </summary>
    /// <param name="navigation">The fly-camera axis the host resolved from its keymap this frame.</param>
    EditorInputFrame CaptureFrame(float deltaTime, EditorNavigationInput navigation = default);
}
