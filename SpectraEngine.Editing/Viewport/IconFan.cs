using System;
using System.Numerics;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Moves icons that would be drawn over each other apart, so each one can be
/// read and clicked. An icon no other is drawn over keeps its place.
/// </summary>
// Icons whose anchors overlap are a pile and stand in rows, in their order.
// Icons still drawn over each other after that are pushed apart. Each move
// starts at nothing as two icons meet, so nothing jumps.
public sealed class IconFan
{
    /// <summary>How many icons stand in a row before the next row starts.</summary>
    public const int RowLength = 6;

    // A pair turns as it comes apart, from side by side to the line between
    // its anchors. The two ways round meet on this line, up and to the left of
    // the earlier icon, and are blended this far to each side of it.
    private const float SeamAngle = MathF.PI * 7f / 6f;
    private const float SeamHalfWidth = MathF.PI / 12f;

    // A push whose way is shorter than this share of touching counts for
    // less, down to nothing where the way turns round.
    private const float FullPushFrom = 0.5f;

    private Pile[] _piles = [];
    private Vector2[] _pushes = [];
    private int[] _piled = [];
    private int _piledCount;

    /// <summary>Works out where each icon is drawn, every icon taking part in full.</summary>
    /// <param name="anchors">Where each icon's node is on screen, in pixels with y down, in the order a row lists them.</param>
    /// <param name="halfWidths">Half of each icon's width, in pixels.</param>
    /// <param name="places">Receives where each icon is drawn.</param>
    public void Spread(ReadOnlySpan<Vector2> anchors, ReadOnlySpan<float> halfWidths, Span<Vector2> places) =>
        Spread(anchors, halfWidths, default, default, places);

    /// <summary>Works out where each icon is drawn, in the order a row lists them.</summary>
    /// <param name="anchors">Where each icon's node is on screen, in pixels with y down, in the order a row lists them.</param>
    /// <param name="halfWidths">Half of each icon's width, in pixels.</param>
    /// <param name="strengths">How much each icon takes part, from 0 to 1. Empty when all take part in full.</param>
    /// <param name="places">Receives where each icon is drawn.</param>
    public void Spread(
        ReadOnlySpan<Vector2> anchors,
        ReadOnlySpan<float> halfWidths,
        ReadOnlySpan<float> strengths,
        Span<Vector2> places) =>
        Spread(anchors, halfWidths, strengths, default, places);

    /// <summary>Works out where each icon is drawn.</summary>
    /// <param name="anchors">Where each icon's node is on screen, in pixels with y down.</param>
    /// <param name="halfWidths">Half of each icon's width, in pixels.</param>
    /// <param name="strengths">How much each icon takes part, from 0 to 1. Empty when all take part in full.</param>
    /// <param name="order">
    /// Where each icon comes in a row, lowest first. Empty when the icons are
    /// given in that order.
    /// </param>
    /// <param name="places">Receives where each icon is drawn.</param>
    public void Spread(
        ReadOnlySpan<Vector2> anchors,
        ReadOnlySpan<float> halfWidths,
        ReadOnlySpan<float> strengths,
        ReadOnlySpan<int> order,
        Span<Vector2> places)
    {
        int count = anchors.Length;
        if (halfWidths.Length != count || places.Length != count ||
            (strengths.Length != count && !strengths.IsEmpty) || (order.Length != count && !order.IsEmpty))
        {
            throw new ArgumentException("Every icon needs an anchor, a width and a place.");
        }

        if (_piles.Length < count)
        {
            int room = Math.Max(count, _piles.Length * 2);
            _piles = new Pile[room];
            _pushes = new Vector2[room];
            _piled = new int[room];
        }

        Span<Pile> piles = _piles.AsSpan(0, count);
        for (int i = 0; i < count; i++)
            piles[i] = new Pile { Cell = halfWidths[i] };

        Gather(anchors, halfWidths, strengths, order, piles);

        _piledCount = 0;
        for (int i = 0; i < count; i++)
        {
            bool isPiled = piles[i].Others > 0f;
            places[i] = isPiled ? Place(anchors[i], in piles[i]) : anchors[i];
            if (isPiled)
                _piled[_piledCount++] = i;
        }

        if (_piledCount > 0)
            PushApart(anchors, halfWidths, strengths, order, places);
    }

    // Adds every overlapping pair's share to both of its icons. A share is
    // none when the two only touch and all once they are half a width apart.
    private static void Gather(
        ReadOnlySpan<Vector2> anchors,
        ReadOnlySpan<float> halfWidths,
        ReadOnlySpan<float> strengths,
        ReadOnlySpan<int> order,
        Span<Pile> piles)
    {
        for (int i = 0; i < anchors.Length - 1; i++)
        {
            float strength = strengths.IsEmpty ? 1f : strengths[i];
            if (strength <= 0f)
                continue;

            Vector2 at = anchors[i];
            float half = halfWidths[i];

            for (int j = i + 1; j < anchors.Length; j++)
            {
                // Where the two would touch.
                float reach = half + halfWidths[j];
                Vector2 apart = anchors[j] - at;
                if (MathF.Abs(apart.X) >= reach || apart.LengthSquared() >= reach * reach)
                    continue;

                float overlap = MathF.Min((reach - apart.Length()) * 2f / reach, 1f);
                float together = strengths.IsEmpty ? strength : strength * strengths[j];
                float share = overlap * together;
                if (share <= 0f)
                    continue;

                // From the earlier of the two in the order to the later.
                bool isLaterFirst = !order.IsEmpty && order[j] < order[i];
                int earlier = isLaterFirst ? j : i;
                int later = isLaterFirst ? i : j;
                if (isLaterFirst)
                    apart = -apart;

                // A row is spaced for its widest icon.
                float uneven = halfWidths[later] - halfWidths[earlier];
                float wider = share * uneven;
                uneven = MathF.Abs(uneven);

                // What takes the pair from its places in the row to where it
                // stands: touching, turned by how far it has come apart.
                Vector2 stands = Turned(apart, overlap) * (reach + (overlap * overlap * uneven));
                var inRow = new Vector2(overlap * (reach + (share * uneven)), 0f);
                Vector2 pull = (apart + (inRow - stands)) * (together * (1f + share) * 0.5f);

                piles[earlier].Meet(share, pull, halfWidths[earlier] + wider, isAhead: false);
                piles[later].Meet(share, -pull, halfWidths[later] - wider, isAhead: true);
            }
        }
    }

    // Which way the later icon of a pair stands from the earlier one. Side by
    // side while they overlap in full, then turning towards the line between
    // their anchors, the short way round. A pair listed against the screen
    // goes round underneath.
    private static Vector2 Turned(Vector2 apart, float overlap)
    {
        if (overlap >= 1f)
            return Vector2.UnitX;

        // Slow at first, so an icon that meets a whole pile does not dart off.
        float turn = 1f - (overlap * MathF.Sqrt(overlap));
        float fromSeam = MathF.Atan2(apart.Y, apart.X) - SeamAngle;
        if (fromSeam <= -MathF.PI)
            fromSeam += MathF.Tau;

        float under = turn * (SeamAngle + fromSeam);
        float over = turn * (SeamAngle + fromSeam - MathF.Tau);

        if (fromSeam <= -SeamHalfWidth)
            return Unit(under);

        if (fromSeam >= SeamHalfWidth)
            return Unit(over);

        // Across the seam the two ways are opposite at one distance and
        // cancel there. A turn that never jumps has one such place.
        return Vector2.Lerp(Unit(under), Unit(over), (fromSeam + SeamHalfWidth) / (SeamHalfWidth * 2f));
    }

    private static Vector2 Unit(float angle)
    {
        (float sine, float cosine) = MathF.SinCos(angle);
        return new Vector2(cosine, sine);
    }

    private static Vector2 Place(Vector2 anchor, in Pile pile)
    {
        float others = pile.Others;
        Slot(pile.Ahead, out float column, out float row);
        Slot(others, out _, out float lastRow);

        // A short row is centred on its own length, a stack on a full row.
        var cell = new Vector2(
            column - (MathF.Min(others, RowLength - 1) * 0.5f),
            row - (lastRow * 0.5f));

        return anchor + (pile.Pull / (1f + others)) + (cell * (pile.Cell * 2f));
    }

    // Where the icon with this many ahead of it stands. Rows snake: one runs
    // left to right and the next right to left, so two icons next to each
    // other in the order are next to each other on screen. Reading order would
    // send one right across the pile each time a row filled.
    private static void Slot(float ahead, out float column, out float row)
    {
        float turn = ahead + 1f;
        float rows = MathF.Floor(turn / RowLength);
        float into = turn - (rows * RowLength);

        // The last step of a row goes down to the next one, not along.
        row = rows - 1f + MathF.Min(into, 1f);
        float along = MathF.Max(into - 1f, 0f);
        column = ((int)rows & 1) == 0 ? along : RowLength - 1 - along;
    }

    // Pushes apart two icons that are still drawn over each other, each by
    // half of what is missing: a row and an icon beside it, or two rows. Only
    // a pile's icons were moved, so one of the two is in a pile.
    private void PushApart(
        ReadOnlySpan<Vector2> anchors,
        ReadOnlySpan<float> halfWidths,
        ReadOnlySpan<float> strengths,
        ReadOnlySpan<int> order,
        Span<Vector2> places)
    {
        ReadOnlySpan<Pile> piles = _piles.AsSpan(0, places.Length);
        Span<Vector2> pushes = _pushes.AsSpan(0, places.Length);
        pushes.Clear();

        foreach (int moved in _piled.AsSpan(0, _piledCount))
        {
            for (int other = 0; other < places.Length; other++)
            {
                // Two icons of piles meet once, at the first given of them.
                if (other == moved || (other < moved && piles[other].Others > 0f))
                    continue;

                float reach = halfWidths[moved] + halfWidths[other];
                if (MathF.Abs(places[other].X - places[moved].X) >= reach)
                    continue;

                bool isOtherLater = order.IsEmpty || order[other] == order[moved]
                    ? other > moved
                    : order[other] > order[moved];
                int earlier = isOtherLater ? moved : other;
                int later = isOtherLater ? other : moved;

                Vector2 apart = places[later] - places[earlier];
                if (apart.LengthSquared() >= reach * reach)
                    continue;

                // The later one goes underneath, the more so the nearer the
                // two are. So two on one point have a way to part.
                float missing = reach - apart.Length();
                Vector2 way = apart + new Vector2(0f, missing);
                float length = way.Length();
                if (length <= 0f)
                    continue;

                // Two of one pile stand where the pile's rule put them.
                float together = (reach - Vector2.Distance(anchors[later], anchors[earlier])) * 2f / reach;
                float part = MathF.Min(length / (reach * FullPushFrom), 1f) * (1f - Math.Clamp(together, 0f, 1f));
                if (!strengths.IsEmpty)
                    part *= strengths[moved] * strengths[other];

                Vector2 push = way * (missing * part * 0.5f / length);
                pushes[earlier] -= push;
                pushes[later] += push;
            }
        }

        for (int i = 0; i < places.Length; i++)
            places[i] += pushes[i];
    }

    // What an icon's overlaps add up to.
    private struct Pile
    {
        // How many icons stand ahead of this one in its pile, and behind it.
        public float Ahead;
        public float Behind;

        // Towards where its pairs stand.
        public Vector2 Pull;

        // Half the spacing of its row.
        public float Cell;

        public readonly float Others => Ahead + Behind;

        // Counts one overlapping icon, which is ahead of this one in the order or behind it.
        public void Meet(float share, Vector2 pull, float cell, bool isAhead)
        {
            if (isAhead)
                Ahead += share;
            else
                Behind += share;

            Pull += pull;
            Cell = MathF.Max(Cell, cell);
        }
    }
}
