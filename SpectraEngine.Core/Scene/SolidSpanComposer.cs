using System;
using SpectraEngine.Core.Assets;

namespace SpectraEngine.Core.Scene;

// Turns the stretches a segment spends inside single brushes into the solid
// it passes through. Works on distances along the segment and nothing else.
// World solid is every additive stretch less every cut, whatever their order:
// that is the rule the carve uses. Order only says who keeps an overlap. Of two
// world brushes the earlier placement does. The world keeps what a part
// overlaps, and of two parts the one entered first does. Nothing cuts a part.
internal sealed class SolidSpanComposer
{
    private Stretch[] _worldSolids = new Stretch[16];
    private int _worldSolidCount;

    private Stretch[] _parts = new Stretch[8];
    private int _partCount;

    private Reach[] _cuts = new Reach[8];
    private int _cutCount;

    // What is taken so far: sorted, and no two touch.
    private Reach[] _taken = new Reach[16];
    private int _takenCount;

    private SolidSpan[] _pieces = new SolidSpan[16];
    private int _pieceCount;

    public void Clear()
    {
        _worldSolidCount = 0;
        _partCount = 0;
        _cutCount = 0;
    }

    // rank is the brush's place in placement order.
    public void AddWorldSolid(float start, float end, MaterialRef material, UInt128 rank) =>
        Append(ref _worldSolids, ref _worldSolidCount, new Stretch(start, end, material, rank));

    public void AddCut(float start, float end) =>
        Append(ref _cuts, ref _cutCount, new Reach(start, end));

    // tieBreak orders two parts entered at the same distance, so the answer
    // does not depend on the order they were found in.
    public void AddPart(float start, float end, MaterialRef material, UInt128 tieBreak) =>
        Append(ref _parts, ref _partCount, new Stretch(start, end, material, tieBreak));

    // Writes the spans nearest first and returns how many. truncated is set
    // when there were more than fit.
    public int Compose(Span<SolidSpan> spans, out bool truncated)
    {
        _pieceCount = 0;
        _takenCount = 0;

        // A cut is taken by nobody, so no world brush can claim it.
        for (int i = 0; i < _cutCount; i++)
            Take(_cuts[i].Start, _cuts[i].End);

        SortByRank(_worldSolids, _worldSolidCount);
        for (int i = 0; i < _worldSolidCount; i++)
            Claim(in _worldSolids[i]);

        if (_partCount > 0)
        {
            // Start again from the world's solid alone: a part fills a cut.
            SortPieces();
            _takenCount = 0;
            for (int i = 0; i < _pieceCount; i++)
                Take(_pieces[i].Start, _pieces[i].End);

            SortByStart(_parts, _partCount);
            for (int i = 0; i < _partCount; i++)
                Claim(in _parts[i]);
        }

        SortPieces();
        return Write(spans, out truncated);
    }

    // Adds what nothing has taken of the stretch, then takes all of it.
    private void Claim(in Stretch stretch)
    {
        float cursor = stretch.Start;

        for (int i = 0; i < _takenCount && cursor < stretch.End; i++)
        {
            Reach taken = _taken[i];
            if (taken.End <= cursor)
                continue;
            if (taken.Start >= stretch.End)
                break;

            if (taken.Start > cursor)
                Append(ref _pieces, ref _pieceCount, new SolidSpan(cursor, taken.Start, stretch.Material));

            cursor = taken.End;
        }

        if (cursor < stretch.End)
            Append(ref _pieces, ref _pieceCount, new SolidSpan(cursor, stretch.End, stretch.Material));

        Take(stretch.Start, stretch.End);
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

    // Joins pieces with no air between them and drops what is too thin.
    private int Write(Span<SolidSpan> spans, out bool truncated)
    {
        truncated = false;
        int written = 0;
        bool open = false;
        SolidSpan current = default;

        for (int i = 0; i < _pieceCount; i++)
        {
            SolidSpan piece = _pieces[i];

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

    private readonly record struct Stretch(float Start, float End, MaterialRef Material, UInt128 Rank);
}
