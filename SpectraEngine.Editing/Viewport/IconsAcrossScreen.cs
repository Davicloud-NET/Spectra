using System;
using System.Numerics;

namespace SpectraEngine.Editing.Viewport;

// The drawn icons in order from the left of the screen to the right, so the
// ones that may stand over a line are found without looking at every icon.
internal sealed class IconsAcrossScreen
{
    private float[] _across = [];
    private int[] _places = [];
    private int _count;

    // Sorts the drawn icons by how far across the screen each stands, in pixels.
    public void Sort(ReadOnlySpan<SceneIcon> icons, Vector3 eye, Vector3 right)
    {
        if (_across.Length < icons.Length)
        {
            int room = Math.Max(icons.Length, _across.Length * 2);
            _across = new float[room];
            _places = new int[room];
        }

        _count = 0;

        for (int i = 0; i < icons.Length; i++)
        {
            if (!icons[i].IsDrawn)
                continue;

            _across[_count] = Vector3.Dot(icons[i].At - eye, right) / icons[i].Pixel;
            _places[_count++] = i;
        }

        Array.Sort(_across, _places, 0, _count);
    }

    // The places of the icons that stand from this far across the screen to
    // that far, in pixels.
    public ReadOnlySpan<int> Between(float left, float right)
    {
        int first = _across.AsSpan(0, _count).BinarySearch(left);
        if (first < 0)
            first = ~first;

        // A run of equal values may start before where the search landed.
        while (first > 0 && _across[first - 1] >= left)
            first--;

        int end = first;
        while (end < _count && _across[end] <= right)
            end++;

        return _places.AsSpan(first, end - first);
    }
}
