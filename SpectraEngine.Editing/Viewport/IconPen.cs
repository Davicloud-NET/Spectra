using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;

namespace SpectraEngine.Editing.Viewport;

// What a viewport icon is drawn with: the screen plane at the icon, one
// pixel's world size there, and which of the two passes is running.
internal struct IconPen(Camera camera, Vector3 at, float radius, float pixel, Vector3 color)
{
    // Drawn under each stroke, so an icon reads over sky, floor or a lit wall.
    public static readonly Vector3 BackingColor = new(0.02f, 0.02f, 0.025f);

    // Under this radius a stroke is one pixel wide. Two would run together in
    // a drawing this small.
    private const float ThinBelowPixels = 10.5f;

    private const float PointerStart = 1.3f;
    private const float PointerEnd = 2.6f;

    private const int RingSegments = 12;

    private const float HomeMarkPixels = 2f;

    public readonly Vector3 At = at;
    public readonly Vector3 Right = camera.Right;
    public readonly Vector3 Up = camera.Up;
    public readonly Vector3 Toward = Vector3.Normalize(Vector3.Cross(camera.Right, camera.Up));
    public readonly float Radius = radius;
    public readonly float Pixel = pixel;
    public readonly Vector3 Color = color;
    public readonly bool IsThin = radius < ThinBelowPixels * pixel;
    public bool Backing;

    // One stroke of an icon. The debug lane draws hairlines, so the colour is
    // two lines a pixel apart, and the backing is one either side of those. A
    // thin stroke is one line of colour between two of backing.
    public readonly void Stroke(DebugDraw output, Vector3 a, Vector3 b)
    {
        // At right angles to the stroke as it lies on screen.
        Vector3 along = b - a;
        float x = Vector3.Dot(along, Right);
        float y = Vector3.Dot(along, Up);
        float length = MathF.Sqrt((x * x) + (y * y));

        Vector3 across = length > 1e-6f
            ? ((Right * -y) + (Up * x)) * (Pixel / length)
            : Right * Pixel;

        if (Backing)
        {
            Vector3 beside = across * (IsThin ? 1f : 1.5f);
            output.Line(a + beside, b + beside, BackingColor);
            output.Line(a - beside, b - beside, BackingColor);
        }
        else if (IsThin)
        {
            output.Line(a, b, Color);
        }
        else
        {
            output.Line(a + (across * 0.5f), b + (across * 0.5f), Color);
            output.Line(a - (across * 0.5f), b - (across * 0.5f), Color);
        }
    }

    // An icon file's drawing, facing the screen and centred on the icon.
    public readonly void Glyph(DebugDraw output, ViewportGlyph glyph)
    {
        float size = Radius * ViewportGlyphs.Reach * 2f;
        Vector3 across = Right * size;
        Vector3 down = Up * -size;
        Vector3 corner = At - ((across + down) * 0.5f);

        for (int stroke = 0; stroke < glyph.StrokeCount; stroke++)
        {
            ReadOnlySpan<Vector2> points = glyph.Stroke(stroke);
            Vector3 previous = corner + (across * points[0].X) + (down * points[0].Y);

            for (int i = 1; i < points.Length; i++)
            {
                Vector3 current = corner + (across * points[i].X) + (down * points[i].Y);
                Stroke(output, previous, current);
                previous = current;
            }
        }
    }

    // The arrow that says which way the thing under a glyph faces. It starts
    // clear of the glyph.
    public readonly void Pointer(DebugDraw output, Vector3 direction)
    {
        Vector3 tip = At + (direction * Radius * PointerEnd);
        Stroke(output, At + (direction * Radius * PointerStart), tip);

        // Across the screen, so the barbs show whichever way the arrow points.
        Vector3 across = Vector3.Cross(direction, Toward);
        across = across.LengthSquared() > 1e-6f ? Vector3.Normalize(across) : Right;

        Vector3 barb = across * (Radius * 0.28f);
        Vector3 neck = tip - (direction * Radius * 0.5f);
        Stroke(output, tip, neck + barb);
        Stroke(output, tip, neck - barb);
    }

    // A hairline ring facing the screen, for an icon too small for strokes.
    public readonly void ThinRing(DebugDraw output, float radius, Vector3 color)
    {
        Vector3 previous = At + (Right * radius);

        for (int i = 1; i <= RingSegments; i++)
        {
            float angle = i * (MathF.Tau / RingSegments);
            Vector3 current = At + (Right * radius * MathF.Cos(angle)) + (Up * radius * MathF.Sin(angle));
            output.Line(previous, current, color);
            previous = current;
        }
    }

    // A hairline diamond facing the screen.
    public readonly void ThinDiamond(DebugDraw output, Vector3 centre, float radius, Vector3 color)
    {
        Vector3 across = Right * radius;
        Vector3 along = Up * radius;

        output.Line(centre + across, centre + along, color);
        output.Line(centre + along, centre - across, color);
        output.Line(centre - across, centre - along, color);
        output.Line(centre - along, centre + across, color);
    }

    // Shows where the node of a moved icon is: a small mark on it, and a line
    // to it from the icon's rim once it is out from under the icon. Hairlines
    // with no backing, drawn before any glyph so they lie under all of them.
    public readonly void Home(DebugDraw output, Vector3 home)
    {
        Vector3 away = home - At;
        float distance = away.Length();
        if (distance <= 0f)
            return;

        Vector3 dim = Color * LightOverlay.HomeLineDim;

        // Full size once the icon has moved by half its width. It grows with
        // the move, so the mark does not appear from nothing.
        float grown = MathF.Min(distance / (Radius * ViewportGlyphs.Reach), 1f);
        ThinDiamond(output, home, HomeMarkPixels * Pixel * grown, dim);

        if (distance > Radius)
            output.Line(At + (away * (Radius / distance)), home, dim);
    }
}
