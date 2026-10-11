using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Where the icon of every light and point entity stands in one view. Icons
/// that would cover each other on screen are moved apart by <see cref="IconFan"/>.
/// </summary>
// Drawing and picking both ask here, so what is clicked is what is seen. The
// answer follows from the scene, the camera and the budgets alone: two of
// these with the same budgets agree to the bit.
public sealed class SceneIcons
{
    /// <summary>
    /// Under this radius on screen, in pixels, an icon is a smudge. It is a
    /// small mark then, and is not moved.
    /// </summary>
    public const float DetailPixels = 6.5f;

    // A glyph of DetailPixels takes no part in a fan and one of FullFanPixels
    // takes all of its part. So a pile does not jump apart at the size where
    // its marks turn into glyphs.
    private const float FullFanPixels = 8f;

    private readonly IconFan _fan = new();

    // Sorted when the first line home asks, so a pick does not pay for it.
    private readonly IconsAcrossScreen _across = new();
    private bool _isAcrossSorted;
    private Vector3 _eye;
    private Vector3 _right;

    private SceneIcon[] _icons = [];
    private int _lightCount;
    private int _markerCount;

    // The glyphs the fan runs over, and each one's place in _icons.
    private int[] _fanned = [];
    private Vector2[] _anchors = [];
    private Vector2[] _places = [];
    private float[] _halfWidths = [];
    private float[] _strengths = [];

    private float[] _nearness = [];

    /// <summary>
    /// How many light icons may be drawn in one frame. The rest are counted in
    /// <see cref="SkippedLights"/>.
    /// </summary>
    public int MaxLights { get; set; } = 256;

    /// <summary>
    /// How many point entities may be drawn in one frame. The rest are counted
    /// in <see cref="SkippedMarkers"/>.
    /// </summary>
    public int MaxMarkers { get; set; } = 256;

    /// <summary>
    /// How many point entities may be drawn as a glyph in one frame. When more
    /// are near enough to read, the nearest get the glyph.
    /// </summary>
    // A glyph costs ten times the lines of a small mark.
    public int MaxMarkerGlyphs { get; set; } = 64;

    /// <summary>Every icon in view: the lights, then the point entities.</summary>
    public ReadOnlySpan<SceneIcon> All => _icons.AsSpan(0, _lightCount + _markerCount);

    /// <summary>Every light in view, in the order the scene lists them.</summary>
    public ReadOnlySpan<SceneIcon> Lights => _icons.AsSpan(0, _lightCount);

    /// <summary>Every point entity in view, in the order the scene lists them.</summary>
    public ReadOnlySpan<SceneIcon> Markers => _icons.AsSpan(_lightCount, _markerCount);

    /// <summary>Lights the last placing left undrawn because <see cref="MaxLights"/> was reached.</summary>
    public int SkippedLights { get; private set; }

    /// <summary>Point entities the last placing left undrawn because <see cref="MaxMarkers"/> was reached.</summary>
    public int SkippedMarkers { get; private set; }

    /// <summary>Works out where every icon stands for this camera.</summary>
    public void Place(Scene scene, Camera camera, Vector2 viewportSize)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        _lightCount = 0;
        _markerCount = 0;
        SkippedLights = 0;
        SkippedMarkers = 0;
        _isAcrossSorted = false;
        _eye = camera.Position;
        _right = camera.Right;

        if (viewportSize.Y <= 0f)
            return;

        int most = scene.LightNodes.Count + scene.EntityNodes.Count;
        if (_icons.Length < most)
            _icons = new SceneIcon[Math.Max(most, _icons.Length * 2)];

        _lightCount = Gather(scene.LightNodes, areLights: true, camera, viewportSize, out int skippedLights);
        _markerCount = Gather(scene.EntityNodes, areLights: false, camera, viewportSize, out int skippedMarkers);
        SkippedLights = skippedLights;
        SkippedMarkers = skippedMarkers;

        KeepNearestMarkerGlyphs();
        FanOut(camera);
    }

    /// <summary>
    /// The nearest icon <paramref name="ray"/> passes through, where the icon
    /// is drawn. Places the icons first.
    /// </summary>
    // An icon that was moved is not found on its node: only what is drawn
    // there is.
    public bool TryPick(
        Scene scene, Camera camera, in Ray3 ray, Vector2 viewportSize,
        out SceneNode? node, out float distance)
    {
        Place(scene, camera, viewportSize);

        node = null;
        distance = float.PositiveInfinity;
        int nearest = -1;

        for (int i = 0; i < _lightCount + _markerCount; i++)
        {
            ref readonly SceneIcon icon = ref _icons[i];

            // The radius the overlays draw at.
            if (!LightPicking.TryRaySphere(in ray, icon.At, icon.Radius, out float hit) || hit >= distance)
                continue;

            nearest = i;
            distance = hit;
        }

        if (nearest < 0)
            return false;

        node = nearest < _lightCount
            ? scene.LightNodes[_icons[nearest].Node]
            : scene.EntityNodes[_icons[nearest].Node];
        return true;
    }

    /// <summary>
    /// The icon of this node as it was last placed. False when the node has
    /// none in view.
    /// </summary>
    public bool TryFind(Scene scene, SceneNode node, out SceneIcon icon)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(node);

        icon = default;

        bool isLight = node.Light is not null;
        if (!isLight && !EntityMarkerPicking.HasMarker(node))
            return false;

        ReadOnlySpan<SceneIcon> icons = isLight ? Lights : Markers;
        IReadOnlyList<SceneNode> nodes = isLight ? scene.LightNodes : scene.EntityNodes;

        foreach (ref readonly SceneIcon candidate in icons)
        {
            if (candidate.Node < nodes.Count && ReferenceEquals(nodes[candidate.Node], node))
            {
                icon = candidate;
                return true;
            }
        }

        return false;
    }

    // The places in All of the icons that may stand over the line from this
    // icon to its node.
    internal ReadOnlySpan<int> BesideLineHome(int place, float marginPixels)
    {
        if (!_isAcrossSorted)
        {
            _across.Sort(All, _eye, _right);
            _isAcrossSorted = true;
        }

        return _across.BesideLineHome(in _icons[place], marginPixels);
    }

    // Measures the icons of one of the scene's two lists into _icons, after
    // the ones already there. A node past the budget is still measured: it is
    // not drawn, and can still be clicked.
    private int Gather(
        IReadOnlyList<SceneNode> nodes, bool areLights, Camera camera, Vector2 viewportSize, out int skipped)
    {
        int budget = areLights ? MaxLights : MaxMarkers;
        int first = _lightCount;
        int count = 0;
        int drawn = 0;
        skipped = 0;

        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode node = nodes[i];
            if (areLights ? node.Light is null : !EntityMarkerPicking.HasMarker(node))
                continue;

            bool isDrawn = drawn < budget;
            if (!isDrawn)
                skipped++;

            Vector3 at = node.WorldPosition;
            if (!LightPicking.TryMeasure(camera, viewportSize, at, out float radius, out float pixel))
                continue;

            _icons[first + count++] = new SceneIcon
            {
                Node = i,
                Anchor = at,
                At = at,
                Radius = radius,
                Pixel = pixel,
                IsDrawn = isDrawn,
                IsGlyph = isDrawn && radius >= DetailPixels * pixel,
            };

            if (isDrawn)
                drawn++;
        }

        return count;
    }

    // When more point entities are near enough for a glyph than may have one,
    // the nearest keep it.
    private void KeepNearestMarkerGlyphs()
    {
        Span<SceneIcon> markers = _icons.AsSpan(_lightCount, _markerCount);
        if (_nearness.Length < markers.Length)
            _nearness = new float[Math.Max(markers.Length, _nearness.Length * 2)];

        int detailed = 0;
        foreach (ref readonly SceneIcon marker in markers)
        {
            if (marker.IsGlyph)
                _nearness[detailed++] = marker.Pixel;
        }

        if (detailed <= MaxMarkerGlyphs)
            return;

        // A pixel covers more of the world the farther off it is, so the
        // smallest belong to the nearest.
        float farthest = float.NegativeInfinity;
        if (MaxMarkerGlyphs > 0)
        {
            Array.Sort(_nearness, 0, detailed);
            farthest = _nearness[MaxMarkerGlyphs - 1];
        }

        int kept = 0;
        foreach (ref SceneIcon marker in markers)
            marker.IsGlyph = marker.IsGlyph && marker.Pixel <= farthest && kept++ < MaxMarkerGlyphs;
    }

    // Runs the fan over the glyphs, in the order they are listed: lights, then
    // point entities. A small mark stays on its node.
    private void FanOut(Camera camera)
    {
        int count = ProjectGlyphs(camera);
        if (count < 2)
            return;

        _fan.Spread(
            _anchors.AsSpan(0, count), _halfWidths.AsSpan(0, count),
            _strengths.AsSpan(0, count), _places.AsSpan(0, count));

        Vector3 right = camera.Right;
        Vector3 up = camera.Up;

        for (int i = 0; i < count; i++)
        {
            Vector2 moved = _places[i] - _anchors[i];
            if (moved == Vector2.Zero)
                continue;

            // Across the screen at the node's own depth, so the icon keeps its size.
            ref SceneIcon icon = ref _icons[_fanned[i]];
            icon.At = icon.Anchor + (right * (moved.X * icon.Pixel)) - (up * (moved.Y * icon.Pixel));
        }
    }

    // Fills the fan's input with every glyph: where it is on screen in pixels,
    // y down, and how wide it is drawn.
    private int ProjectGlyphs(Camera camera)
    {
        int total = _lightCount + _markerCount;
        if (_fanned.Length < total)
        {
            int room = Math.Max(total, _fanned.Length * 2);
            _fanned = new int[room];
            _anchors = new Vector2[room];
            _places = new Vector2[room];
            _halfWidths = new float[room];
            _strengths = new float[room];
        }

        Vector3 eye = camera.Position;
        Vector3 right = camera.Right;
        Vector3 up = camera.Up;
        int count = 0;

        for (int i = 0; i < total; i++)
        {
            ref readonly SceneIcon icon = ref _icons[i];
            if (!icon.IsGlyph)
                continue;

            Vector3 offset = icon.Anchor - eye;
            float radius = icon.Radius / icon.Pixel;

            _fanned[count] = i;
            _anchors[count] = new Vector2(Vector3.Dot(offset, right), -Vector3.Dot(offset, up)) / icon.Pixel;
            _halfWidths[count] = radius * ViewportGlyphs.Reach;
            _strengths[count] = Math.Clamp((radius - DetailPixels) / (FullFanPixels - DetailPixels), 0f, 1f);
            count++;
        }

        return count;
    }
}
