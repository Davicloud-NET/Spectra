using System;
using System.Numerics;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Moves icons that overlap on screen apart into rows, so each one can be read
/// and clicked. An icon nothing overlaps keeps its place.
/// </summary>
// Each pair that overlaps has a share: none when the two only touch, all of it
// once they are half a width apart or nearer. A pair's share puts the later of
// the two one place further along the row and draws both towards the point
// between them. Every share is zero as a pair meets, so an icon joins or
// leaves a pile with no jump, and neither does anything already in the pile.
// The answer depends on the arguments alone.
public sealed class IconFan
{
    /// <summary>How many icons stand in a row before the next row starts.</summary>
    public const int RowLength = 6;

    private Pile[] _piles = [];

    /// <summary>Works out where each icon is drawn, every icon taking part in full.</summary>
    /// <param name="anchors">Where each icon's node is on screen, in pixels with y down, in scene order.</param>
    /// <param name="halfWidths">Half of each icon's width, in pixels.</param>
    /// <param name="places">Receives where each icon is drawn.</param>
    public void Spread(ReadOnlySpan<Vector2> anchors, ReadOnlySpan<float> halfWidths, Span<Vector2> places) =>
        Spread(anchors, halfWidths, default, places);

    /// <summary>Works out where each icon is drawn.</summary>
    /// <param name="anchors">Where each icon's node is on screen, in pixels with y down, in scene order.</param>
    /// <param name="halfWidths">Half of each icon's width, in pixels.</param>
    /// <param name="strengths">How much each icon takes part, from 0 to 1. Empty when all take part in full.</param>
    /// <param name="places">Receives where each icon is drawn.</param>
    public void Spread(
        ReadOnlySpan<Vector2> anchors,
        ReadOnlySpan<float> halfWidths,
        ReadOnlySpan<float> strengths,
        Span<Vector2> places)
    {
        int count = anchors.Length;
        if (halfWidths.Length != count || places.Length != count || (strengths.Length != count && !strengths.IsEmpty))
            throw new ArgumentException("Every icon needs an anchor, a width and a place.");

        if (_piles.Length < count)
            _piles = new Pile[Math.Max(count, _piles.Length * 2)];

        Span<Pile> piles = _piles.AsSpan(0, count);
        for (int i = 0; i < count; i++)
            piles[i] = new Pile { Cell = halfWidths[i] };

        Gather(anchors, halfWidths, strengths, piles);

        for (int i = 0; i < count; i++)
            places[i] = piles[i].Ahead + piles[i].Behind > 0f ? Place(anchors[i], in piles[i]) : anchors[i];
    }

    // Adds every overlapping pair's share to both of its icons.
    private static void Gather(
        ReadOnlySpan<Vector2> anchors, ReadOnlySpan<float> halfWidths, ReadOnlySpan<float> strengths, Span<Pile> piles)
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

                float share = MathF.Min((reach - apart.Length()) * 2f / reach, 1f) * strength;
                if (!strengths.IsEmpty)
                    share *= strengths[j];

                if (share <= 0f)
                    continue;

                // A row is spaced for its widest icon.
                float wider = share * (halfWidths[j] - half);
                piles[i].Meet(share, apart, half + wider, isAhead: false);
                piles[j].Meet(share, -apart, halfWidths[j] - wider, isAhead: true);
            }
        }
    }

    private static Vector2 Place(Vector2 anchor, in Pile pile)
    {
        float others = pile.Ahead + pile.Behind;
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

    // What an icon's overlaps add up to.
    private struct Pile
    {
        // How many icons stand ahead of this one in its pile, and behind it.
        public float Ahead;
        public float Behind;

        // Towards the middle of the icons it overlaps.
        public Vector2 Pull;

        // Half the spacing of its row.
        public float Cell;

        // Counts one overlapping icon, which is ahead of this one in the order or behind it.
        public void Meet(float share, Vector2 toward, float cell, bool isAhead)
        {
            if (isAhead)
                Ahead += share;
            else
                Behind += share;

            // Squared, so a pair that has only begun to overlap is pushed
            // apart before it is drawn together.
            Pull += toward * (share * share);
            Cell = MathF.Max(Cell, cell);
        }
    }
}
