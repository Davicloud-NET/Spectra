using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

// The menus the Logic canvas opens on its wires: what a dropped wire sends,
// and the one that removes a wire. A menu that closes leaves the keyboard
// where it is: a click that closes one has given it to what was clicked.
internal sealed class LogicWireMenus
{
    private readonly Control _owner;
    private readonly Action<Guid> _select;

    // The menu a dropped wire waits on, until a line is picked or it closes.
    private ContextMenu? _awaitsPick;

    // The menu that removes a wire, and the wire it was opened on.
    private ContextMenu? _removes;
    private LogicSelectedWire _removed;

    // select asks for an entity to be selected.
    public LogicWireMenus(Control owner, Action<Guid> select)
    {
        _owner = owner;
        _select = select;
    }

    // The menu on show, or null.
    public ContextMenu? Shown { get; private set; }

    // Whether the menu that removes a wire is open. It has the keyboard then,
    // and its wire stays selected.
    public bool OffersRemoval => _removes is { IsOpen: true };

    // Asks what the wire that was just dropped sends. Closed with nothing
    // picked, the wire is given up.
    public void OfferWire(LogicViewModel model, Point at)
    {
        if (model.Wiring.Menu() is not { } offer)
            return;

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = Words(offer.Title), IsEnabled = false });
        if (offer.Note.Length > 0)
            menu.Items.Add(new MenuItem { Header = Words(offer.Note), IsEnabled = false });

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

    // A right click on a wire selects it, as a click does, and offers to
    // remove it. The wire is let go when the menu closes: the keyboard may be
    // anywhere by then, and Delete must not be left to mean this wire.
    public void OfferRemoval(LogicViewModel model, Point at)
    {
        if (model.HitTest(at) is not { Kind: LogicHitKind.Label or LogicHitKind.Edge, Edge: { } edge })
            return;

        model.Wiring.Select(edge);
        if (model.Wiring.Selected is not { } wire)
            return;

        _select(edge.Edge.From.NodeId);

        var line = new MenuItem { Header = Words(LogicWireText.Remove), InputGesture = new KeyGesture(Key.Delete) };
        line.Click += (_, _) => Remove(model, wire);

        var menu = new ContextMenu();
        menu.Items.Add(line);
        menu.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_removes, menu))
                return;

            _removes = null;
            LetGo(model, wire);
        };

        // Before it opens: it takes the keyboard from the canvas as it does.
        _removes = menu;
        _removed = wire;
        OpenAt(menu, at);
    }

    // Closes a menu whose wire is not waiting on it any more: the scene a
    // wire was dropped on is gone, or the wire to remove is not selected.
    public void CloseStale(LogicViewModel? model)
    {
        if (_awaitsPick is { } pick && model?.Wiring.Gesture.Phase != LogicWirePhase.Dropped)
            pick.Close();

        if (_removes is { } removal && model?.Wiring.Selected != _removed)
            removal.Close();
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

    // Only the wire the menu was opened on: the selection may have moved.
    private void Remove(LogicViewModel model, LogicSelectedWire wire)
    {
        _removes = null;
        if (model.Wiring.Selected == wire)
            model.Wiring.RemoveSelected();
    }

    private static void LetGo(LogicViewModel model, LogicSelectedWire wire)
    {
        if (model.Wiring.Selected == wire)
            model.Wiring.Select(null);
    }

    // At a point of the canvas, not at the pointer: the two differ when the
    // press came from a pen, a touch or a test.
    private void OpenAt(ContextMenu menu, Point at)
    {
        Shown?.Close();
        Shown = menu;

        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(Shown, menu))
                Shown = null;
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
