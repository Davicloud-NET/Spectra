using System;
using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;

namespace SpectraEngine.Editing.Viewport;

// Runs the fan over the glyphs of one view: where each is on screen, a pile
// in the order of the scene tree, and each place back into the world. A small
// mark is no glyph and stays on its node.
internal sealed class SceneIconFan
{
    private readonly IconFan _fan = new();
    private readonly SceneTreeOrder _order = new();

    // The glyphs in the order the fan gets them, each by its place among all
    // the icons.
    private int[] _glyphs = [];
    private Vector2[] _anchors = [];
    private Vector2[] _places = [];
    private float[] _halfWidths = [];
    private float[] _strengths = [];

    private int[] _piled = [];
    private int[] _treePlaces = [];

    private int[] _moved = [];
    private int _movedCount;

    // The icons the last spread drew off their nodes, each by its place among
    // all the icons.
    public ReadOnlySpan<int> Moved => _moved.AsSpan(0, _movedCount);

    // Moves the icons that would cover each other. Lights come first among
    // the icons, this many of them.
    public void Spread(Scene scene, Camera camera, Span<SceneIcon> icons, int lightCount)
    {
        _movedCount = 0;

        int count = Collect(icons);
        if (count < 2)
            return;

        Fan(camera, icons, count);

        // The scene lists nodes as they were attached. Mostly that is the
        // tree's order, and no pile has to be looked up.
        if (PutPilesInTreeOrder(scene, icons, lightCount))
            Fan(camera, icons, count);

        Vector3 right = camera.Right;
        Vector3 up = camera.Up;

        for (int i = 0; i < count; i++)
        {
            Vector2 moved = _places[i] - _anchors[i];
            if (moved == Vector2.Zero)
                continue;

            // Across the screen at the node's own depth, so the icon keeps its size.
            ref SceneIcon icon = ref icons[_glyphs[i]];
            icon.At = icon.Anchor + (right * (moved.X * icon.Pixel)) - (up * (moved.Y * icon.Pixel));
            _moved[_movedCount++] = _glyphs[i];
        }
    }

    private int Collect(ReadOnlySpan<SceneIcon> icons)
    {
        if (_glyphs.Length < icons.Length)
        {
            int room = Math.Max(icons.Length, _glyphs.Length * 2);
            _glyphs = new int[room];
            _anchors = new Vector2[room];
            _places = new Vector2[room];
            _halfWidths = new float[room];
            _strengths = new float[room];
            _piled = new int[room];
            _treePlaces = new int[room];
            _moved = new int[room];
        }

        int count = 0;
        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i].IsGlyph)
                _glyphs[count++] = i;
        }

        return count;
    }

    // Each glyph's place on screen in pixels, y down, and how wide it is
    // drawn. Then the fan.
    private void Fan(Camera camera, ReadOnlySpan<SceneIcon> icons, int count)
    {
        Vector3 eye = camera.Position;
        Vector3 right = camera.Right;
        Vector3 up = camera.Up;

        for (int i = 0; i < count; i++)
        {
            ref readonly SceneIcon icon = ref icons[_glyphs[i]];
            Vector3 offset = icon.Anchor - eye;

            _anchors[i] = new Vector2(Vector3.Dot(offset, right), -Vector3.Dot(offset, up)) / icon.Pixel;
            _halfWidths[i] = icon.Radius / icon.Pixel * ViewportGlyphs.Reach;
            _strengths[i] = icon.FanStrength;
        }

        _fan.Spread(
            _anchors.AsSpan(0, count), _halfWidths.AsSpan(0, count),
            _strengths.AsSpan(0, count), _places.AsSpan(0, count));
    }

    // Reorders the glyphs that stand in piles to the order of the scene tree.
    // False when they were in it already.
    private bool PutPilesInTreeOrder(Scene scene, ReadOnlySpan<SceneIcon> icons, int lightCount)
    {
        ReadOnlySpan<int> piled = _fan.Piled;
        bool isInOrder = true;

        for (int i = 0; i < piled.Length; i++)
        {
            int glyph = _glyphs[piled[i]];
            SceneNode node = glyph < lightCount
                ? scene.LightNodes[icons[glyph].Node]
                : scene.EntityNodes[icons[glyph].Node];

            _piled[i] = glyph;
            _treePlaces[i] = _order.Of(scene, node);
            isInOrder &= i == 0 || _treePlaces[i - 1] <= _treePlaces[i];
        }

        if (isInOrder)
            return false;

        // The piled glyphs change places among themselves. The rest overlap
        // nothing, so where they are listed does not matter.
        Array.Sort(_treePlaces, _piled, 0, piled.Length);
        for (int i = 0; i < piled.Length; i++)
            _glyphs[piled[i]] = _piled[i];

        return true;
    }
}
