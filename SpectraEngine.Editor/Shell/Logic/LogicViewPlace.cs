using Avalonia;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Where the graph sits in its view, and what puts it there: a fit, the user,
// or a card that has just come on show.
internal sealed class LogicViewPlace
{
    private readonly LogicFitRule _fit = new();
    private readonly Action _moved;
    private LogicPanZoom _view = LogicPanZoom.Identity;

    // moved is told when the graph sits somewhere else.
    public LogicViewPlace(Action moved) => _moved = moved;

    public LogicPanZoom View
    {
        get => _view;
        set
        {
            if (_view == value)
                return;

            _view = value;
            _fit.Moved();
            _moved();
        }
    }

    // How large the view is.
    public Size Size { get; private set; }

    // The user asked for another picture, so the next scene is fitted.
    public void AskForFit() => _fit.Ask();

    // The session ended. Where the graph sits is kept for the level coming back.
    public void Forget() => _fit.Forget();

    // A graph that still sits as a fit left it is fitted again.
    public void Resize(Size size)
    {
        if (Size == size)
            return;

        Size = size;
        _fit.Resized();
    }

    // A scene with cards was laid out.
    public void LaidOut(Guid[] shown, Size scene, bool followsSelection) =>
        _fit.Placed(shown, scene, followsSelection);

    // The whole graph, as large as fits and no larger than its own size.
    public void Fit(LogicScene scene)
    {
        View = LogicPanZoom.Fit(scene.Size, Size);
        _fit.Fitted();
    }

    public void ShowActualSize() =>
        View = _view.ZoomedAbout(new Point(Size.Width / 2, Size.Height / 2), 1);

    public void CenterOn(Rect scene) => View = _view.CenteredOn(scene, Size);

    // Moves the graph no further than it takes to have a card in the view.
    public void Reveal(Rect scene)
    {
        if (HasRoom)
            View = _view.Showing(scene, Size, LogicDrawMetrics.RevealMargin);
    }

    // Places the scene when a fit is due and there is a view to fit it in.
    public void FitIfAsked(LogicScene? scene, IReadOnlySet<Guid> selected)
    {
        if (!HasRoom || scene is not { Cards.Count: > 0 } || !_fit.Take())
            return;

        // Placed, not fitted: in a low pane a fit would shrink the text away.
        View = LogicPanZoom.Placed(scene.Size, Size, scene.BoundsOf(selected));
        _fit.Fitted();
    }

    private bool HasRoom => Size.Width > 0 && Size.Height > 0;
}
