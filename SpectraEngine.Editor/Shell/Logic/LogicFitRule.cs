using Avalonia;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Says when the graph is fitted into the view, and when it keeps the place
// the user gave it.
internal sealed class LogicFitRule
{
    private Guid[] _placed = [];
    private Size _placedSize;
    private bool _awaitsFirst = true;
    private bool _sitsFitted;
    private bool _asked;

    // The user asked for another picture, so the next scene is fitted.
    public void Ask() => _asked = true;

    // A fit put the graph where it is.
    public void Fitted() => _sitsFitted = true;

    // Something else did: a drag, the wheel, a jump to a card.
    public void Moved() => _sitsFitted = false;

    // The view changed size. A graph nobody moved since it was fitted stays
    // fitted, so a pane that is resized, or gives room to the event strip,
    // goes on showing all of it.
    public void Resized() => _asked |= _sitsFitted;

    // The session ended. What was on show is remembered: the same level
    // coming back, as after a restart, keeps its place.
    public void Forget()
    {
        _awaitsFirst = true;
        _asked = false;
    }

    // A scene with cards was laid out. It is compared with the last scene
    // that had cards, so a view that showed nothing for a while, hidden or
    // between sessions, finds its graph where it left it.
    public void Placed(Guid[] shown, Size size, bool followsSelection)
    {
        bool same = shown.AsSpan().SequenceEqual(_placed);

        if (_sitsFitted)
            _asked = true;
        else if (_awaitsFirst)
            _asked |= !same;
        else if (followsSelection)
            _asked |= !same || size != _placedSize;
        else
            _asked |= !same && SharesNone(shown, _placed);

        _awaitsFirst = false;
        _placed = shown;
        _placedSize = size;
    }

    // Whether to fit now. Asked once there is a scene and a view to fit it in.
    public bool Take()
    {
        bool asked = _asked;
        _asked = false;
        return asked;
    }

    // In the whole level a new scene keeps the place the user gave the last
    // one, unless it is another level altogether.
    private static bool SharesNone(Guid[] shown, Guid[] placed)
    {
        var before = new HashSet<Guid>(placed);
        foreach (Guid id in shown)
        {
            if (before.Contains(id))
                return false;
        }

        return true;
    }
}
