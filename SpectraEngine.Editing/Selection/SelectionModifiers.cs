using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Selection;

/// <summary>
/// What a modifier key means for selection: nothing held replaces, Shift adds,
/// Ctrl toggles. Shift wins when both are held. Shared by click and box select.
/// </summary>
public static class SelectionModifiers
{
    /// <summary>Resolves held modifiers into the selection combination they mean.</summary>
    public static SelectionUpdate Resolve(KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Shift) != 0)
            return SelectionUpdate.Add;

        if ((modifiers & KeyModifiers.Control) != 0)
            return SelectionUpdate.Toggle;

        return SelectionUpdate.Replace;
    }

    /// <summary>True when the modifiers keep what is already selected.</summary>
    public static bool IsAdditive(KeyModifiers modifiers) =>
        Resolve(modifiers) != SelectionUpdate.Replace;
}
