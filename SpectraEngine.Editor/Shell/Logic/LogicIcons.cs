using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

// The glyph in a card's header: one for each entity group, one for an entity
// with no group and one for a target nothing answers to. Chosen by group
// only. The shell knows no entity class by name.
internal sealed class LogicIcons
{
    private readonly Geometry? _mover = LogicTheme.Icon("IconEntityMover");
    private readonly Geometry? _logic = LogicTheme.Icon("IconEntityLogic");
    private readonly Geometry? _trigger = LogicTheme.Icon("IconEntityTrigger");
    private readonly Geometry? _player = LogicTheme.Icon("IconEntityPlayer");
    private readonly Geometry? _sound = LogicTheme.Icon("IconEntitySound");
    private readonly Geometry? _entity = LogicTheme.Icon("IconEntity");
    private readonly Geometry? _missing = LogicTheme.Icon("IconWarning");

    private readonly IPen _moverPen = Pen("SpectraKindBrushWorld");
    private readonly IPen _logicPen = Pen("SpectraKindEntity");
    private readonly IPen _triggerPen = Pen("SpectraKindLight");
    private readonly IPen _playerPen = Pen("SpectraKindBrushPart");
    private readonly IPen _soundPen = Pen("SpectraKindBrushSubtractive");
    private readonly IPen _entityPen = Pen("SpectraKindEntity");
    private readonly IPen _quietPen = Pen("SpectraTextMuted");
    private readonly IPen _missingPen = Pen("SpectraTextDanger");

    // The glyph on a 16 box, and the pen that tints it.
    public Geometry? Of(LogicCard card, out IPen pen)
    {
        switch (card.Stub)
        {
            case LogicStubKind.None:
                break;

            case LogicStubKind.Activator:
                pen = _quietPen;
                return _entity;

            default:
                pen = _missingPen;
                return _missing;
        }

        if (Is(card, "Movers"))
        {
            pen = _moverPen;
            return _mover;
        }

        if (Is(card, "Logic"))
        {
            pen = _logicPen;
            return _logic;
        }

        if (Is(card, "Triggers"))
        {
            pen = _triggerPen;
            return _trigger;
        }

        if (Is(card, "Player"))
        {
            pen = _playerPen;
            return _player;
        }

        if (Is(card, "Sound"))
        {
            pen = _soundPen;
            return _sound;
        }

        pen = _entityPen;
        return _entity;
    }

    private static bool Is(LogicCard card, string group) =>
        string.Equals(card.Group, group, StringComparison.Ordinal);

    private static ImmutablePen Pen(string brush) => new(
        LogicTheme.Brush(brush),
        LogicTheme.Size("SpectraLogicIconStroke"),
        null,
        PenLineCap.Round,
        PenLineJoin.Round);
}
