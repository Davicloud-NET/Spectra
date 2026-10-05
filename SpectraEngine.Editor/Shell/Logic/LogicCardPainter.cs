using Avalonia;
using Avalonia.Media;
using SpectraEngine.Core.Inspection;

namespace SpectraEngine.Editor.Shell.Logic;

// Draws one card. Every piece of text is cut to the box the layout gave it,
// so no text of one card can reach another card or a label.
internal sealed class LogicCardPainter
{
    private readonly LogicPalette _palette;
    private readonly LogicTextCache _texts;

    public LogicCardPainter(LogicPalette palette, LogicTextCache texts)
    {
        _palette = palette;
        _texts = texts;
    }

    public void Draw(
        DrawingContext context,
        LogicSceneCard card,
        LogicViewModel model,
        LogicDetail detail,
        bool isHovered)
    {
        bool isSelected = model.IsSelected(card.Card);

        if (detail >= LogicDetail.Far)
        {
            DrawBox(context, card, isSelected, isHovered, hasName: detail == LogicDetail.Far);
            return;
        }

        DrawSurface(context, card, detail);
        DrawHeader(context, card);

        if (detail == LogicDetail.Full)
        {
            DrawState(context, card, model);
            DrawPorts(context, card);
            DrawNote(context, card);
        }

        DrawEdge(context, card, isSelected, isHovered);
    }

    private static bool IsUnanswered(LogicSceneCard card) =>
        card.Stub is LogicStubKind.MissingName or LogicStubKind.MissingPrefix or LogicStubKind.NoTarget;

    private void DrawBox(DrawingContext context, LogicSceneCard card, bool isSelected, bool isHovered, bool hasName)
    {
        Rect box = card.Bounds;
        double radius = _palette.CardRadius;
        bool isUnanswered = IsUnanswered(card);

        IBrush fill = isSelected ? _palette.SelectedFill : isUnanswered ? _palette.StubHead : _palette.CardHead;
        context.DrawRectangle(fill, null, box, radius, radius);

        if (isHovered)
            context.DrawRectangle(_palette.HoverWash, null, box, radius, radius);

        if (!hasName)
            return;

        LogicInk ink = isSelected ? LogicInk.FarSelectedName
            : isUnanswered ? LogicInk.FarStubName
            : LogicInk.FarName;

        string words = card.Card.Name.Length > 0 ? card.Card.Name : card.Card.DisplayName;
        FormattedText name = _texts.Get(words, ink, box.Width - 2 * LogicDrawMetrics.HeaderPadding);
        if (name.Height > box.Height)
            return;

        context.DrawText(name, new Point(box.X + LogicDrawMetrics.HeaderPadding, box.Center.Y - name.Height / 2));
    }

    private void DrawSurface(DrawingContext context, LogicSceneCard card, LogicDetail detail)
    {
        Rect box = card.Bounds;
        double radius = _palette.CardRadius;
        bool isUnanswered = IsUnanswered(card);

        context.DrawRectangle(isUnanswered ? _palette.Stub : _palette.Card, null, box, radius, radius);
        context.DrawRectangle(
            isUnanswered ? _palette.StubHead : _palette.CardHead,
            null,
            new RoundedRect(card.Header, radius, radius, 0, 0));

        // Half a pixel up, so a one pixel line fills one row of pixels.
        double rule = card.Header.Bottom - 0.5;
        context.DrawLine(_palette.CardEdge, new Point(box.X, rule), new Point(box.Right, rule));

        if (detail == LogicDetail.Full && card.StateRow is Rect state)
        {
            rule = state.Bottom - 0.5;
            context.DrawLine(_palette.StateRule, new Point(box.X, rule), new Point(box.Right, rule));
        }
    }

    private void DrawHeader(DrawingContext context, LogicSceneCard card)
    {
        Rect box = card.Bounds;
        double iconLeft = box.X + LogicDrawMetrics.HeaderPadding;
        double iconTop = box.Y + (LogicMetrics.HeaderHeight - LogicDrawMetrics.IconSize) / 2;

        if (_palette.Icons.Of(card.Card, out IPen iconPen) is { } icon)
        {
            using (context.PushTransform(Matrix.CreateTranslation(iconLeft, iconTop)))
                context.DrawGeometry(null, iconPen, icon);
        }

        double left = iconLeft + LogicDrawMetrics.IconSize + LogicDrawMetrics.IconGap;
        double room = box.Right - LogicDrawMetrics.HeaderPadding - left;

        (LogicInk nameInk, LogicInk lineInk) = card.Stub switch
        {
            LogicStubKind.None => (LogicInk.Name, LogicInk.ClassLine),
            LogicStubKind.Activator => (LogicInk.QuietName, LogicInk.ClassLine),
            _ => (LogicInk.StubName, LogicInk.StubLine),
        };

        // A wire with no target has no name to show. Its one line stands in
        // the middle, in the place and the ink of a name.
        if (card.Card.Name.Length == 0)
        {
            FormattedText only = _texts.Get(card.Card.DisplayName, nameInk, room);
            context.DrawText(only, new Point(left, box.Y + (LogicMetrics.HeaderHeight - only.Height) / 2));
            return;
        }

        FormattedText name = _texts.Get(card.Card.Name, nameInk, room);
        context.DrawText(name, new Point(left, box.Y + LogicDrawMetrics.NameLine - name.Height / 2));

        FormattedText line = _texts.Get(card.Card.DisplayName, lineInk, room);
        context.DrawText(line, new Point(left, box.Y + LogicDrawMetrics.ClassLine - line.Height / 2));
    }

    private void DrawState(DrawingContext context, LogicSceneCard card, LogicViewModel model)
    {
        if (card.StateRow is not Rect row
            || card.Card.IsStub
            || !model.TryGetState(card.Card.NodeId, out LogicEntityState state))
        {
            return;
        }

        double left = row.X + LogicDrawMetrics.HeaderPadding;
        double right = row.Right - LogicDrawMetrics.HeaderPadding;
        double middle = row.Y + (row.Height - 1) / 2;
        double? baseline = null;

        if (!string.IsNullOrEmpty(state.Label))
        {
            // The label may take half the row. The value gets what is left.
            FormattedText label = _texts.GetRunning(state.Label, LogicInk.StateLabel, (right - left) / 2);
            double top = middle - label.Height / 2;

            context.DrawText(label, new Point(left, top));
            left += label.Width + LogicDrawMetrics.StateGap;
            baseline = top + label.Baseline;
        }

        if (string.IsNullOrEmpty(state.Value))
            return;

        // On the label's line where there is one, in the middle of the row where there is none.
        FormattedText value = _texts.GetRunning(state.Value, LogicInk.StateValue, right - left);
        context.DrawText(value, new Point(left, baseline - value.Baseline ?? middle - value.Height / 2));
    }

    private void DrawPorts(DrawingContext context, LogicSceneCard card)
    {
        for (int i = 0; i < card.Ports.Count; i++)
        {
            LogicScenePort port = card.Ports[i];
            Rect row = port.Row;

            // A port only a wire names, on a class that is known not to have it.
            LogicInk ink = card.Card.IsKnownClass && !port.Port.IsDeclared ? LogicInk.WrongPort : LogicInk.Port;

            FormattedText name = _texts.Get(port.Name, ink, row.Width - 2 * LogicDrawMetrics.RowPadding);
            double left = port.IsOutput
                ? row.Right - LogicDrawMetrics.RowPadding - name.Width
                : row.X + LogicDrawMetrics.RowPadding;

            context.DrawText(name, new Point(left, row.Center.Y - name.Height / 2));
        }
    }

    private void DrawNote(DrawingContext context, LogicSceneCard card)
    {
        if (card.NoteRow is not Rect row || card.Note.Length == 0)
            return;

        FormattedText note = _texts.Get(card.Note, LogicInk.Note, row.Width - 2 * LogicDrawMetrics.RowPadding);
        context.DrawText(note, new Point(row.X + LogicDrawMetrics.RowPadding, row.Center.Y - note.Height / 2));
    }

    private void DrawEdge(DrawingContext context, LogicSceneCard card, bool isSelected, bool isHovered)
    {
        Rect box = card.Bounds;
        double radius = _palette.CardRadius;

        if (isHovered)
            context.DrawRectangle(_palette.HoverWash, null, box, radius, radius);

        if (isSelected)
        {
            // Half inside the card and half outside, as a ring round it.
            context.DrawRectangle(null, _palette.SelectedEdge, box, radius, radius);
            return;
        }

        IPen edge = card.Stub switch
        {
            LogicStubKind.None => isHovered ? _palette.HoveredEdge : _palette.CardEdge,
            LogicStubKind.Activator => _palette.QuietEdge,
            _ => _palette.StubEdge,
        };

        context.DrawRectangle(null, edge, box.Deflate(0.5), radius, radius);
    }
}
