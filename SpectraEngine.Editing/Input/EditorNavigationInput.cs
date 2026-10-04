using System.Numerics;

namespace SpectraEngine.Editing.Input;

/// <summary>
/// The camera fly axis and boost flag for one frame, resolved by the host from
/// whatever keys or sticks it binds.
/// </summary>
public readonly struct EditorNavigationInput
{
    /// <summary>Creates a navigation input from an already-resolved axis.</summary>
    public EditorNavigationInput(Vector3 move, bool boost = false)
    {
        Move = move;
        Boost = boost;
    }

    /// <summary>
    /// The movement axis: X camera right, Y world up, Z camera forward, each in
    /// [-1, 1]. The camera controller normalizes it.
    /// </summary>
    // Y is world up so "go up" stays vertical when the camera is pitched.
    public Vector3 Move { get; }

    /// <summary>True while the move-faster modifier is held.</summary>
    public bool Boost { get; }

    /// <summary>True when no movement is being asked for.</summary>
    public bool IsIdle => Move == Vector3.Zero;

    /// <summary>Builds an axis from six held-key flags. Opposing keys cancel.</summary>
    public static EditorNavigationInput FromKeys(
        bool forward, bool back, bool left, bool right, bool up, bool down, bool boost = false)
    {
        var move = new Vector3(
            (right ? 1f : 0f) - (left ? 1f : 0f),
            (up ? 1f : 0f) - (down ? 1f : 0f),
            (forward ? 1f : 0f) - (back ? 1f : 0f));

        return new EditorNavigationInput(move, boost);
    }
}
