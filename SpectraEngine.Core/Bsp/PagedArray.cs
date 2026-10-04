using System;
using System.Collections;
using System.Collections.Generic;

namespace SpectraEngine.Core.Bsp;

// Immutable paged array with copy-on-write replacement: a derived instance
// clones only the pages it touches and shares the rest, so a one-brush edit
// does not copy a world-sized array.
internal sealed class PagedArray<T> : IReadOnlyList<T>
{
    // 1024 slots: an edit clones kilobytes, and the page table stays tiny.
    private const int PageShift = 10;
    private const int PageSize = 1 << PageShift;
    private const int PageMask = PageSize - 1;

    private readonly T[][] _pages;

    private PagedArray(T[][] pages, int count)
    {
        _pages = pages;
        Count = count;
    }

    public int Count { get; }

    public T this[int index] => _pages[index >> PageShift][index & PageMask];

    // O(n). For the full compile path only.
    public static PagedArray<T> From(IReadOnlyList<T> source)
    {
        int count = source.Count;
        var pages = new T[(count + PageSize - 1) >> PageShift][];
        for (int p = 0; p < pages.Length; p++)
        {
            int start = p << PageShift;
            var page = new T[Math.Min(PageSize, count - start)];
            for (int i = 0; i < page.Length; i++)
                page[i] = source[start + i];
            pages[p] = page;
        }
        return new PagedArray<T>(pages, count);
    }

    // Copy with the given slots replaced. Duplicate indices: last write wins.
    public PagedArray<T> WithReplacements(IReadOnlyList<(int Index, T Value)> replacements)
    {
        if (replacements.Count == 0) return this;
        var pages = (T[][])_pages.Clone();
        foreach ((int index, T value) in replacements)
        {
            int p = index >> PageShift;
            // Still the ancestor's page: clone before the first write.
            if (ReferenceEquals(pages[p], _pages[p]))
                pages[p] = (T[])_pages[p].Clone();
            pages[p][index & PageMask] = value;
        }
        return new PagedArray<T>(pages, Count);
    }

    // Grows to count without moving any existing index.
    public PagedArray<T> WithReplacements(int count, IReadOnlyList<(int Index, T Value)> replacements)
    {
        if (count == Count) return WithReplacements(replacements);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, Count);
        var pages = new T[(count + PageSize - 1) >> PageShift][];
        Array.Copy(_pages, pages, _pages.Length);
        for (int p = 0; p < pages.Length; p++)
        {
            int length = Math.Min(PageSize, count - (p << PageShift));
            if (pages[p] is null) pages[p] = new T[length];
            else if (pages[p].Length < length)
            {
                var expanded = new T[length];
                Array.Copy(pages[p], expanded, pages[p].Length);
                pages[p] = expanded;
            }
        }
        foreach (var (index, value) in replacements)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
            int p = index >> PageShift;
            if (p < _pages.Length && ReferenceEquals(pages[p], _pages[p]))
                pages[p] = (T[])pages[p].Clone();
            pages[p][index & PageMask] = value;
        }
        return new PagedArray<T>(pages, count);
    }

    public void CopyTo(int sourceIndex, T[] destination, int destinationIndex, int length)
    {
        while (length > 0)
        {
            int page = sourceIndex >> PageShift;
            int offset = sourceIndex & PageMask;
            int run = Math.Min(length, PageSize - offset);
            Array.Copy(_pages[page], offset, destination, destinationIndex, run);
            sourceIndex += run;
            destinationIndex += run;
            length -= run;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        foreach (T[] page in _pages)
        {
            foreach (T item in page)
                yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
