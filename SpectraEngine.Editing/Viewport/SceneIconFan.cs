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
    private readonly SceneTreeOrder _treeOrder = new();

    // The glyphs, each by its place among all the icons.
    private int[] _glyphs = [];
    private Vector2[] _anchors = [];
    private Vector2[] _places = [];
    private float[] _halfWidths = [];
    private float[] _strengths = [];
    private int[] _order = [];

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

        _treeOrder.Refresh(scene);

        Vector3 eye = camera.Position;
        Vector3 right = camera.Right;
        Vector3 up = camera.Up;

        // Each glyph's place on screen in pixels, y down, and how wide it is drawn.
        for (int i = 0; i < count; i++)
        {
            int place = _glyphs[i];
            ref readonly SceneIcon icon = ref icons[place];
            Vector3 offset = icon.Anchor - eye;

            _anchors[i] = new Vector2(Vector3.Dot(offset, right), -Vector3.Dot(offset, up)) / icon.Pixel;
            _halfWidths[i] = icon.Radius / icon.Pixel * ViewportGlyphs.Reach;
            _strengths[i] = icon.FanStrength;
            _order[i] = place < lightCount ? _treeOrder.OfLight(icon.Node) : _treeOrder.OfEntity(icon.Node);
        }

        _fan.Spread(
            _anchors.AsSpan(0, count), _halfWidths.AsSpan(0, count), _strengths.AsSpan(0, count),
            _order.AsSpan(0, count), _places.AsSpan(0, count));

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
            _order = new int[room];
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
}
