using System;
using System.Numerics;
using SpectraEngine.Core.Assets;

namespace SpectraEngine.Core.Scene;

// Composes the stretches a segment spends inside single brushes into solid.
// World solid is every additive stretch less every cut, in any order, as the
// carve has it. Of two world brushes the earlier placement keeps an overlap,
// the world keeps what a part overlaps, and of two parts the one entered first.
internal sealed class SolidSpanComposer
{
    private Stretch[] _worldSolids = new Stretch[16];
    private int _worldSolidCount;

    private Stretch[] _parts = new Stretch[8];
    private int _partCount;

    // A cut's material is that of the face the segment leaves it by.
    private Stretch[] _cuts = new Stretch[8];
    private int _cutCount;

    // What is taken so far: sorted, and no two touch.
    private Reach[] _taken = new Reach[16];
    private int _takenCount;

    private SolidSpan[] _pieces = new SolidSpan[16];
    private int _pieceCount;

    // The side of the segment being composed. Only a segment that lies in a
    // brush face has sides.
    private Vector3 _side;

    public void Clear()
    {
        _worldSolidCount = 0;
        _partCount = 0;
        _cutCount = 0;
    }

    // rank is the brush's place in placement order.
    public void AddWorldSolid(
        float start, float end, MaterialRef material, UInt128 rank, LineFaces faces = default) =>
        Append(ref _worldSolids, ref _worldSolidCount, new Stretch(start, end, material, rank, faces));

    // leftBy is the material of the cut's face the segment leaves it by. What
    // is solid behind the cut is entered through that face.
    public void AddCut(float start, float end, MaterialRef leftBy, UInt128 rank, LineFaces faces = default) =>
        Append(ref _cuts, ref _cutCount, new Stretch(start, end, leftBy, rank, faces));

    // tieBreak orders two parts entered at the same distance, so the answer
    // does not depend on the order they were found in.
    public void AddPart(
        float start, float end, MaterialRef material, UInt128 tieBreak, LineFaces faces = default) =>
        Append(ref _parts, ref _partCount, new Stretch(start, end, material, tieBreak, faces));

    // Writes the spans nearest first and returns how many. truncated is set
    // when there were more than fit. side picks the brushes that count among
    // those the segment lies in a face of. It can be called once per side.
    public int Compose(Vector3 side, Span<SolidSpan> spans, out bool truncated)
    {
        _side = side;
        _pieceCount = 0;
        _takenCount = 0;

        // A cut is taken by nobody, so no world brush can claim it.
        for (int i = 0; i < _cutCount; i++)
        {
            if (_cuts[i].Faces.Covers(side))
                Take(_cuts[i].Start, _cuts[i].End);
        }

        SortByRank(_worldSolids, _worldSolidCount);
        for (int i = 0; i < _worldSolidCount; i++)
        {
            if (_worldSolids[i].Faces.Covers(side))
                Claim(in _worldSolids[i], behindCuts: true);
        }

        if (_partCount > 0)
        {
            // Start again from the world's solid alone: a part fills a cut.
            SortPieces();
            _takenCount = 0;
            for (int i = 0; i < _pieceCount; i++)
                Take(_pieces[i].Start, _pieces[i].End);

            SortByStart(_parts, _partCount);
            for (int i = 0; i < _partCount; i++)
            {
                if (_parts[i].Faces.Covers(side))
                    Claim(in _parts[i], behindCuts: false);
            }
        }

        SortPieces();
        return Write(_pieces.AsSpan(0, _pieceCount), spans, out truncated);
    }

    // Joins pieces with no air between them and drops what is too thin.
    // pieces are in order and do not overlap.
    public static int Write(ReadOnlySpan<SolidSpan> pieces, Span<SolidSpan> spans, out bool truncated)
    {
        truncated = false;
        int written = 0;
        bool open = false;
        SolidSpan current = default;

        foreach (SolidSpan next in pieces)
        {
            SolidSpan piece = next;

            if (open && piece.Start - current.End <= SolidSpan.Tolerance)
            {
                if (piece.Material == current.Material)
                {
                    current = current with { End = piece.End };
                    continue;
                }

                // Another material with no air between: the two meet at one
                // number, so a caller can tell.
                piece = piece with { Start = current.End };
            }

            if (open && !Emit(current, spans, ref written))
            {
                truncated = true;
                return written;
            }

            current = piece;
            open = true;
        }

        if (open && !Emit(current, spans, ref written))
            truncated = true;

        return written;
    }

    // Adds what nothing has taken of the stretch, then takes all of it.
    private void Claim(in Stretch stretch, bool behindCuts)
    {
        float cursor = stretch.Start;
        MaterialRef material = stretch.Material;

        for (int i = 0; i < _takenCount && cursor < stretch.End; i++)
        {
            Reach taken = _taken[i];
            if (taken.End <= cursor)
                continue;
            if (taken.Start >= stretch.End)
                break;

            if (taken.Start > cursor)
                Append(ref _pieces, ref _pieceCount, new SolidSpan(cursor, taken.Start, material));

            cursor = taken.End;
            material = behindCuts ? EnteredAt(cursor, stretch.Material) : stretch.Material;
        }

        if (cursor < stretch.End)
            Append(ref _pieces, ref _pieceCount, new SolidSpan(cursor, stretch.End, material));

        Take(stretch.Start, stretch.End);
    }

    // The face solid is entered by at distance, where something taken ends.
    // After a cut it is the cut's face. After an earlier brush there is no
    // face between the two, and the stretch keeps its own.
    private MaterialRef EnteredAt(float distance, MaterialRef own)
    {
        MaterialRef entered = own;
        bool found = false;
        UInt128 earliest = default;

        for (int i = 0; i < _cutCount; i++)
        {
            ref readonly Stretch cut = ref _cuts[i];
            if (cut.End != distance || !cut.Faces.Covers(_side))
                continue;

            // Two cuts that end together: the earlier placement names it.
            if (!found || cut.Rank < earliest)
            {
                entered = cut.Material;
                earliest = cut.Rank;
                found = true;
            }
        }

        return entered;
    }

    // Merges the reach into the taken list, swallowing every entry it touches.
    private void Take(float start, float end)
    {
        int first = 0;
        while (first < _takenCount && _taken[first].End < start)
            first++;

        int last = first;
        while (last < _takenCount && _taken[last].Start <= end)
        {
            start = MathF.Min(start, _taken[last].Start);
            end = MathF.Max(end, _taken[last].End);
            last++;
        }

        int swallowed = last - first;
        if (swallowed == 0)
        {
            if (_takenCount == _taken.Length)
                Array.Resize(ref _taken, _taken.Length * 2);
            Array.Copy(_taken, first, _taken, first + 1, _takenCount - first);
            _takenCount++;
        }
        else if (swallowed > 1)
        {
            Array.Copy(_taken, last, _taken, first + 1, _takenCount - last);
            _takenCount -= swallowed - 1;
        }

        _taken[first] = new Reach(start, end);
    }

    // False when the span counts and there is no room for it.
    private static bool Emit(SolidSpan span, Span<SolidSpan> spans, ref int written)
    {
        if (span.Thickness < SolidSpan.Tolerance)
            return true;
        if (written == spans.Length)
            return false;

        spans[written++] = span;
        return true;
    }

    // Insertion sorts: a segment meets a handful of brushes, and nothing here
    // may allocate.

    private static void SortByRank(Stretch[] stretches, int count)
    {
        for (int i = 1; i < count; i++)
        {
            Stretch moving = stretches[i];
            int j = i - 1;
            for (; j >= 0 && stretches[j].Rank > moving.Rank; j--)
                stretches[j + 1] = stretches[j];
            stretches[j + 1] = moving;
        }
    }

    private static void SortByStart(Stretch[] stretches, int count)
    {
        for (int i = 1; i < count; i++)
        {
            Stretch moving = stretches[i];
            int j = i - 1;
            for (; j >= 0 && EntersAfter(in stretches[j], in moving); j--)
                stretches[j + 1] = stretches[j];
            stretches[j + 1] = moving;
        }
    }

    private static bool EntersAfter(in Stretch a, in Stretch b) =>
        a.Start > b.Start || (a.Start == b.Start && a.Rank > b.Rank);

    private void SortPieces()
    {
        for (int i = 1; i < _pieceCount; i++)
        {
            SolidSpan moving = _pieces[i];
            int j = i - 1;
            for (; j >= 0 && _pieces[j].Start > moving.Start; j--)
                _pieces[j + 1] = _pieces[j];
            _pieces[j + 1] = moving;
        }
    }

    private static void Append<T>(ref T[] items, ref int count, T item)
    {
        if (count == items.Length)
            Array.Resize(ref items, items.Length * 2);
        items[count++] = item;
    }

    private readonly record struct Reach(float Start, float End);

    private readonly record struct Stretch(
        float Start, float End, MaterialRef Material, UInt128 Rank, LineFaces Faces);
}
