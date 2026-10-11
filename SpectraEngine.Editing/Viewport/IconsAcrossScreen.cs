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
    private Vector3 _eye;
    private Vector3 _right;

    public void Clear() => _count = 0;

    // Sorts the drawn icons by how far across the screen each stands, in pixels.
    public void Sort(ReadOnlySpan<SceneIcon> icons, Vector3 eye, Vector3 right)
    {
        if (_across.Length < icons.Length)
        {
            int room = Math.Max(icons.Length, _across.Length * 2);
            _across = new float[room];
            _places = new int[room];
        }

        _eye = eye;
        _right = right;
        _count = 0;

        for (int i = 0; i < icons.Length; i++)
        {
            if (!icons[i].IsDrawn)
                continue;

            _across[_count] = Across(icons[i].At, icons[i].Pixel);
            _places[_count++] = i;
        }

        Array.Sort(_across, _places, 0, _count);
    }

    // The places of the icons that stand no more than a margin, in pixels, to
    // the left or right of the line from this icon to its node.
    public ReadOnlySpan<int> BesideLineHome(in SceneIcon icon, float margin)
    {
        float at = Across(icon.At, icon.Pixel);
        float node = Across(icon.Anchor, icon.Pixel);
        float from = MathF.Min(at, node) - margin;
        float to = MathF.Max(at, node) + margin;

        int first = _across.AsSpan(0, _count).BinarySearch(from);
        if (first < 0)
            first = ~first;

        // A run of equal values may start before where the search landed.
        while (first > 0 && _across[first - 1] >= from)
            first--;

        int end = first;
        while (end < _count && _across[end] <= to)
            end++;

        return _places.AsSpan(first, end - first);
    }

    private float Across(Vector3 point, float pixel) => Vector3.Dot(point - _eye, _right) / pixel;
}
