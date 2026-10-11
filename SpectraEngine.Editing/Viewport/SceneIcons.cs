using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Scene;

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

    private const float FullFanPixels = 8f;

    private readonly SceneIconFan _fan = new();
    private readonly NearestGlyphs _nearestGlyphs = new();

    // Sorted when the first line across the icons asks, so a pick does not
    // pay for it.
    private readonly IconsAcrossScreen _across = new();
    private bool _isAcrossSorted;
    private Vector3 _eye;
    private Vector3 _right;
    private Vector3 _up;

    private SceneIcon[] _icons = [];
    private int _lightCount;
    private int _markerCount;

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
        _up = camera.Up;

        if (viewportSize.Y <= 0f)
            return;

        int most = scene.LightNodes.Count + scene.EntityNodes.Count;
        if (_icons.Length < most)
            _icons = new SceneIcon[Math.Max(most, _icons.Length * 2)];

        _lightCount = Gather(scene.LightNodes, areLights: true, camera, viewportSize, out int skippedLights);
        _markerCount = Gather(scene.EntityNodes, areLights: false, camera, viewportSize, out int skippedMarkers);
        SkippedLights = skippedLights;
        SkippedMarkers = skippedMarkers;

        bool isFlat = camera.ProjectionKind == CameraProjectionKind.Orthographic;
        _nearestGlyphs.Keep(_icons.AsSpan(_lightCount, _markerCount), MaxMarkerGlyphs, isFlat);
        _fan.Spread(scene, camera, _icons.AsSpan(0, _lightCount + _markerCount), _lightCount);
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
    /// The icon of this node as it was last placed, when it is drawn off the
    /// node. False for an icon on its node and for a node with no icon in view.
    /// </summary>
    // Only the moved icons are looked at, and the budgets cap how many there are.
    public bool TryFindMoved(Scene scene, SceneNode node, out SceneIcon icon)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(node);

        foreach (int place in _fan.Moved)
        {
            IReadOnlyList<SceneNode> nodes = place < _lightCount ? scene.LightNodes : scene.EntityNodes;
            int listed = _icons[place].Node;

            if (listed < nodes.Count && ReferenceEquals(nodes[listed], node))
            {
                icon = _icons[place];
                return true;
            }
        }

        icon = default;
        return false;
    }

    // Where a point is on screen, in pixels from the eye's own point, y up.
    // Icons on one screen point can be far apart in the world, so what
    // covers what is worked out here.
    internal Vector2 OnScreen(Vector3 point, float pixel)
    {
        Vector3 offset = point - _eye;
        return new Vector2(Vector3.Dot(offset, _right), Vector3.Dot(offset, _up)) / pixel;
    }

    // The places in All of the drawn icons that stand between two lines down
    // the screen, each given by how far across it is in pixels.
    internal ReadOnlySpan<int> Between(float left, float right)
    {
        if (!_isAcrossSorted)
        {
            _across.Sort(All, _eye, _right);
            _isAcrossSorted = true;
        }

        return _across.Between(left, right);
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

            // A glyph of DetailPixels takes no part in a fan and one of
            // FullFanPixels all of its part, so a pile does not jump apart at
            // the size where its marks turn into glyphs.
            bool isGlyph = isDrawn && radius >= DetailPixels * pixel;
            float size = ((radius / pixel) - DetailPixels) / (FullFanPixels - DetailPixels);

            _icons[first + count++] = new SceneIcon
            {
                Node = i,
                Anchor = at,
                At = at,
                Radius = radius,
                Pixel = pixel,
                IsDrawn = isDrawn,
                IsGlyph = isGlyph,
                FanStrength = isGlyph ? Math.Clamp(size, 0f, 1f) : 0f,
            };

            if (isDrawn)
                drawn++;
        }

        return count;
    }
}
