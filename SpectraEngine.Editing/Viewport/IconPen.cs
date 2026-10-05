using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

// What a viewport icon is drawn with: the screen plane at the icon, one
// pixel's world size there, and which of the two passes is running.
internal struct IconPen(Camera camera, Vector3 at, float radius, float pixel, Vector3 colour)
{
    // Drawn under each stroke, so an icon reads over sky, floor or a lit wall.
    public static readonly Vector3 BackingColour = new(0.02f, 0.02f, 0.025f);

    private const int RingSegments = 12;

    public readonly Vector3 At = at;
    public readonly Vector3 Right = camera.Right;
    public readonly Vector3 Up = camera.Up;
    public readonly Vector3 Toward = Vector3.Normalize(Vector3.Cross(camera.Right, camera.Up));
    public readonly float Radius = radius;
    public readonly float Pixel = pixel;
    public readonly Vector3 Colour = colour;
    public bool Backing;

    // A unit vector across the screen at right angles to a world direction, so
    // parallel arrows stay side by side whichever way they point.
    public readonly Vector3 AcrossScreen(Vector3 direction)
    {
        Vector3 across = Vector3.Cross(direction, Toward);
        return across.LengthSquared() > 1e-6f ? Vector3.Normalize(across) : Right;
    }

    // One stroke of an icon. The debug lane draws hairlines, so the colour is
    // two lines a pixel apart, and the backing is one either side of those.
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
            output.Line(a + (across * 1.5f), b + (across * 1.5f), BackingColour);
            output.Line(a - (across * 1.5f), b - (across * 1.5f), BackingColour);
        }
        else
        {
            output.Line(a + (across * 0.5f), b + (across * 0.5f), Colour);
            output.Line(a - (across * 0.5f), b - (across * 0.5f), Colour);
        }
    }

    public readonly void Arrow(DebugDraw output, Vector3 from, Vector3 tip, Vector3 direction)
    {
        Stroke(output, from, tip);

        Vector3 barb = AcrossScreen(direction) * (Radius * 0.28f);
        Vector3 neck = tip - (direction * Radius * 0.5f);
        Stroke(output, tip, neck + barb);
        Stroke(output, tip, neck - barb);
    }

    public readonly void Ring(DebugDraw output, Vector3 centre, Vector3 u, Vector3 v, float radius)
    {
        Vector3 previous = centre + (u * radius);

        for (int i = 1; i <= RingSegments; i++)
        {
            float angle = i * (MathF.Tau / RingSegments);
            Vector3 current = centre + (u * radius * MathF.Cos(angle)) + (v * radius * MathF.Sin(angle));
            Stroke(output, previous, current);
            previous = current;
        }
    }

    // A hairline ring facing the screen, for an icon too small for strokes.
    public readonly void ThinRing(DebugDraw output, float radius, Vector3 colour)
    {
        Vector3 previous = At + (Right * radius);

        for (int i = 1; i <= RingSegments; i++)
        {
            float angle = i * (MathF.Tau / RingSegments);
            Vector3 current = At + (Right * radius * MathF.Cos(angle)) + (Up * radius * MathF.Sin(angle));
            output.Line(previous, current, colour);
            previous = current;
        }
    }
}
