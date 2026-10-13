namespace SpectraEngine.Core.Entities;

// The kinds of entry the play journal holds for the switches of a part or a
// mesh. One for each switch, and not one for the byte they share: the
// physics layer owns a bit of that byte.
internal static class FlagJournalKind
{
    public static readonly JournalKind<bool> CanCollide = new(
        static node => node.CanCollide,
        static (node, first) => node.CanCollide = first);

    public static readonly JournalKind<bool> CanQuery = new(
        static node => node.CanQuery,
        static (node, first) => node.CanQuery = first);

    public static readonly JournalKind<bool> CanTouch = new(
        static node => node.CanTouch,
        static (node, first) => node.CanTouch = first);

    public static readonly JournalKind<bool> IsRendered = new(
        static node => node.IsRendered,
        static (node, first) => node.IsRendered = first);
}
