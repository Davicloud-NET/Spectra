using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Icons;

/// <summary>
/// Turns SVG path data into polylines. A curve or an arc becomes straight
/// segments that stay within a tolerance of it.
/// </summary>
public static class SvgPathFlattener
{
    // A curve this tight is not an icon. Stops a bad tolerance from running away.
    private const int MaxSegments = 64;

    /// <summary>
    /// Appends the polylines of <paramref name="data"/>, in the path's own units.
    /// </summary>
    /// <param name="tolerance">How far a segment may stray from the curve it stands for.</param>
    /// <param name="points">Receives every polyline's points, one polyline after the other.</param>
    /// <param name="starts">Receives the index in <paramref name="points"/> where each polyline begins.</param>
    /// <exception cref="FormatException">The text is not path data.</exception>
    public static void Flatten(ReadOnlySpan<char> data, float tolerance, List<Vector2> points, List<int> starts)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tolerance);

        var reader = new SvgPathReader(data);
        var pen = new Pen(points, starts, tolerance);
        char command = '\0';

        while (reader.TryReadCommand(ref command))
            pen.Run(command, ref reader);

        pen.DropLonePoint();
    }

    // Where the path is, and the polylines it has drawn so far.
    private struct Pen(List<Vector2> points, List<int> starts, float tolerance)
    {
        private Vector2 _current;
        private Vector2 _start;

        // The last control point, for the smooth commands, and what set it.
        private Vector2 _control;
        private char _controlOf;

        // After a close the next segment starts a polyline of its own.
        private bool _isClosed;

        public void Run(char command, ref SvgPathReader reader)
        {
            bool relative = char.IsAsciiLetterLower(command);
            Vector2 origin = relative ? _current : Vector2.Zero;
            char shape = char.ToUpperInvariant(command);

            switch (shape)
            {
                case 'M':
                    MoveTo(origin + reader.ReadPoint());
                    break;

                case 'L':
                    LineTo(origin + reader.ReadPoint());
                    break;

                case 'H':
                    LineTo(new Vector2(origin.X + reader.ReadNumber(), _current.Y));
                    break;

                case 'V':
                    LineTo(new Vector2(_current.X, origin.Y + reader.ReadNumber()));
                    break;

                case 'C':
                case 'S':
                    Cubic(shape, origin, ref reader);
                    break;

                case 'Q':
                case 'T':
                    Quadratic(shape, origin, ref reader);
                    break;

                case 'A':
                    Arc(origin, ref reader);
                    break;

                case 'Z':
                    Close();
                    break;

                default:
                    throw new FormatException($"'{command}' is not a path command.");
            }

            if (shape is not ('C' or 'S' or 'Q' or 'T'))
                _controlOf = '\0';
        }

        // A move that nothing was drawn from leaves one point, which is no line.
        public readonly void DropLonePoint()
        {
            if (starts.Count > 0 && starts[^1] == points.Count - 1)
            {
                points.RemoveAt(points.Count - 1);
                starts.RemoveAt(starts.Count - 1);
            }
        }

        private void MoveTo(Vector2 to)
        {
            DropLonePoint();
            starts.Add(points.Count);
            points.Add(to);
            _current = _start = to;
            _isClosed = false;
        }

        private void LineTo(Vector2 to)
        {
            if (_isClosed || starts.Count == 0)
                MoveTo(_current);

            if (to != _current)
                points.Add(to);

            _current = to;
        }

        private void Close()
        {
            LineTo(_start);
            _isClosed = true;
        }

        private void Cubic(char shape, Vector2 origin, ref SvgPathReader reader)
        {
            Vector2 from = _current;
            Vector2 first = shape == 'C' ? origin + reader.ReadPoint() : Reflected('C');
            Vector2 second = origin + reader.ReadPoint();
            Vector2 to = origin + reader.ReadPoint();

            // The most a control polygon bends, which bounds how far a chord strays.
            float bend = MathF.Max((from - (2f * first) + second).Length(), (first - (2f * second) + to).Length());
            int count = Segments(MathF.Sqrt(0.75f * bend / tolerance));

            for (int i = 1; i < count; i++)
            {
                float t = i / (float)count;
                float u = 1f - t;
                LineTo((from * (u * u * u)) + (first * (3f * u * u * t)) + (second * (3f * u * t * t)) + (to * (t * t * t)));
            }

            LineTo(to);
            _control = second;
            _controlOf = 'C';
        }

        private void Quadratic(char shape, Vector2 origin, ref SvgPathReader reader)
        {
            Vector2 from = _current;
            Vector2 control = shape == 'Q' ? origin + reader.ReadPoint() : Reflected('Q');
            Vector2 to = origin + reader.ReadPoint();

            float bend = (from - (2f * control) + to).Length();
            int count = Segments(MathF.Sqrt(bend / (4f * tolerance)));

            for (int i = 1; i < count; i++)
            {
                float t = i / (float)count;
                float u = 1f - t;
                LineTo((from * (u * u)) + (control * (2f * u * t)) + (to * (t * t)));
            }

            LineTo(to);
            _control = control;
            _controlOf = 'Q';
        }

        // A smooth command mirrors the control point before it. After any
        // other kind of command it has none, and uses where the path is.
        private readonly Vector2 Reflected(char family) =>
            _controlOf == family ? (2f * _current) - _control : _current;

        private void Arc(Vector2 origin, ref SvgPathReader reader)
        {
            float radiusX = MathF.Abs(reader.ReadNumber());
            float radiusY = MathF.Abs(reader.ReadNumber());
            float turn = reader.ReadNumber() * (MathF.PI / 180f);
            bool large = reader.ReadFlag();
            bool sweep = reader.ReadFlag();
            Vector2 to = origin + reader.ReadPoint();

            if (to == _current)
                return;

            if (radiusX < 1e-6f || radiusY < 1e-6f)
            {
                LineTo(to);
                return;
            }

            var arc = SvgArc.Between(_current, to, radiusX, radiusY, turn, large, sweep);

            // The angle whose chord sags by the tolerance on the larger radius.
            double sag = Math.Min(tolerance / Math.Max(arc.RadiusX, arc.RadiusY), 1.0);
            int count = Segments((float)(Math.Abs(arc.Sweep) / (2.0 * Math.Acos(1.0 - sag))));

            for (int i = 1; i < count; i++)
                LineTo(arc.At(arc.Start + (arc.Sweep * i / count)));

            LineTo(to);
        }

        private static int Segments(float wanted) =>
            float.IsFinite(wanted) ? Math.Clamp((int)MathF.Ceiling(wanted), 1, MaxSegments) : MaxSegments;
    }
}
