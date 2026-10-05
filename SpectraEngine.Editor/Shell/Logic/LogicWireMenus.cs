using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

// The menus the Logic canvas opens on its wires: what a dropped wire sends,
// and the one that removes a wire.
internal sealed class LogicWireMenus
{
    private readonly Control _owner;
    private readonly Action<Guid> _select;

    // The menu a dropped wire waits on, until a line is picked or it closes.
    private ContextMenu? _awaitsPick;

    // select asks for an entity to be selected.
    public LogicWireMenus(Control owner, Action<Guid> select)
    {
        _owner = owner;
        _select = select;
    }

    // The menu on show, or null.
    public ContextMenu? Shown { get; private set; }

    // Asks what the wire that was just dropped sends. Closed with nothing
    // picked, the wire is given up.
    public void OfferWire(LogicViewModel model, Point at)
    {
        if (model.Wiring.Menu() is not { } offer)
            return;

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = Words(offer.Title), IsEnabled = false });
        menu.Items.Add(new Separator());

        foreach (LogicWireMenuItem item in offer.Items)
            menu.Items.Add(Line(item, model));

        menu.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_awaitsPick, menu))
                return;

            _awaitsPick = null;
            model.Wiring.Cancel();
        };

        _awaitsPick = menu;
        OpenAt(menu, at);
    }

    // A right click on a wire selects it, as a click does, and offers to remove it.
    public void OfferRemoval(LogicViewModel model, Point at)
    {
        if (!model.Wiring.CanEdit
            || model.HitTest(at) is not { Kind: LogicHitKind.Label or LogicHitKind.Edge, Edge: { } edge })
        {
            return;
        }

        model.Wiring.Select(edge);
        _select(edge.Edge.From.NodeId);

        var line = new MenuItem { Header = Words(LogicWireText.Remove), InputGesture = new KeyGesture(Key.Delete) };
        line.Click += (_, _) => model.Wiring.RemoveSelected();

        var menu = new ContextMenu();
        menu.Items.Add(line);
        OpenAt(menu, at);
    }

    // Closes the menu of a wire that is not waiting on it any more: the
    // scene it was dropped on is gone.
    public void CloseStale(LogicViewModel? model)
    {
        if (_awaitsPick is { } menu && model?.Wiring.Gesture.Phase != LogicWirePhase.Dropped)
            menu.Close();
    }

    private MenuItem Line(LogicWireMenuItem item, LogicViewModel model)
    {
        var line = new MenuItem { Header = Words(item.Text) };
        if (item.MakesWire)
        {
            line.Click += (_, _) => Pick(item, model);
            return line;
        }

        foreach (LogicWireMenuItem inner in item.Items)
            line.Items.Add(Line(inner, model));

        return line;
    }

    // The pick ends the drag, and the menu then closes by itself. The sender
    // is selected so the Properties panel shows the new wire.
    private void Pick(LogicWireMenuItem item, LogicViewModel model)
    {
        _awaitsPick = null;
        if (model.Wiring.Pick(item) is Guid sender)
            _select(sender);
    }

    // At a point of the canvas, not at the pointer: the two differ when the
    // press came from a pen, a touch or a test. A menu that closes hands the
    // keyboard back, so Delete goes on reaching the canvas.
    private void OpenAt(ContextMenu menu, Point at)
    {
        Shown?.Close();
        Shown = menu;

        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(Shown, menu))
                Shown = null;

            _owner.Focus();
        };

        menu.Placement = PlacementMode.AnchorAndGravity;
        menu.PlacementAnchor = PopupAnchor.TopLeft;
        menu.PlacementGravity = PopupGravity.BottomRight;
        menu.PlacementRect = new Rect(at, new Size(1, 1));
        menu.Open(_owner);
    }

    // Not a string: a menu takes an underscore in one for the mark of an
    // access key, and class names have them.
    private static TextBlock Words(string text) => new() { Text = text };
}
