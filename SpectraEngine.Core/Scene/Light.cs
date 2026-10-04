using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

/// <summary>What shape a light's emission has.</summary>
public enum LightKind
{
    /// <summary>
    /// Parallel rays from infinitely far away: a sun. Position is ignored and
    /// the node's forward axis is the direction the light travels.
    /// </summary>
    Directional,

    /// <summary>
    /// Radiates from the node's position in every direction, falling off with
    /// distance and stopping at <see cref="Light.Range"/>.
    /// </summary>
    Point,

    /// <summary>
    /// A cone from the node's position along its forward axis, with a soft edge
    /// between <see cref="Light.InnerAngle"/> and <see cref="Light.OuterAngle"/>.
    /// </summary>
    Spot,

    /// <summary>
    /// A one-sided rectangle emitting from its whole surface, facing along the
    /// node's forward axis and sized by <see cref="Light.Width"/> and
    /// <see cref="Light.Height"/>.
    /// </summary>
    Rect,

    /// <summary>
    /// A one-sided disc, as <see cref="Rect"/> but circular and sized by
    /// <see cref="Light.Radius"/>.
    /// </summary>
    Disc,
}

/// <summary>
/// A light attached to a <see cref="SceneNode"/>. The node supplies the
/// position and orientation. Mutable, so a duplicate needs <see cref="Clone"/>.
/// </summary>
public sealed class Light
{
    private float _intensity = 1f;
    private float _range = 10f;
    private float _innerAngle = 25f;
    private float _outerAngle = 35f;
    private float _width = 1f;
    private float _height = 1f;
    private float _radius = 0.5f;

    /// <summary>The light's shape.</summary>
    public LightKind Kind { get; set; } = LightKind.Directional;

    /// <summary>
    /// Linear RGB colour, not a display colour. Convert a picked colour with
    /// <c>ColorSpace.SrgbToLinear</c>.
    /// </summary>
    public Vector3 Color { get; set; } = Vector3.One;

    /// <summary>Multiplier on <see cref="Color"/>. Negative values are refused.</summary>
    public float Intensity
    {
        get => _intensity;
        set => _intensity = value >= 0f
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Light intensity cannot be negative.");
    }

    /// <summary>
    /// Distance at which the light's contribution reaches zero. Ignored by a
    /// directional light. Must be positive.
    /// </summary>
    // A hard cutoff, not an inverse-square tail, so the light can be culled.
    public float Range
    {
        get => _range;
        set => _range = value > 0f
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Light range must be positive.");
    }

    /// <summary>Whether this light contributes at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// A spot light's fully lit half-angle, in degrees. Clamped to
    /// <see cref="MaxConeAngle"/>.
    /// </summary>
    public float InnerAngle
    {
        get => _innerAngle;
        set => _innerAngle = Math.Clamp(value, 0f, MaxConeAngle);
    }

    /// <summary>
    /// A spot light's outer half-angle, in degrees: the edge of the cone.
    /// Never reads smaller than <see cref="InnerAngle"/>.
    /// </summary>
    // Clamped on read: the two angles can be assigned in either order, and an
    // outer inside the inner inverts the falloff.
    public float OuterAngle
    {
        get => Math.Max(_outerAngle, _innerAngle);
        set => _outerAngle = Math.Clamp(value, 0f, MaxConeAngle);
    }

    /// <summary>A rect light's width in world units, along the node's right axis.</summary>
    public float Width
    {
        get => _width;
        set => _width = Math.Max(value, MinimumExtent);
    }

    /// <summary>A rect light's height in world units, along the node's up axis.</summary>
    public float Height
    {
        get => _height;
        set => _height = Math.Max(value, MinimumExtent);
    }

    /// <summary>A disc light's radius in world units.</summary>
    public float Radius
    {
        get => _radius;
        set => _radius = Math.Max(value, MinimumExtent);
    }

    /// <summary>The largest half-angle a cone may have.</summary>
    public const float MaxConeAngle = 89f;

    /// <summary>The smallest an area light's extent may be.</summary>
    // Not zero: the shader divides by the shape's size.
    public const float MinimumExtent = 0.001f;

    /// <summary>A new light carrying the same settings.</summary>
    public Light Clone() => new()
    {
        Kind = Kind,
        Color = Color,
        Intensity = Intensity,
        Range = Range,
        Enabled = Enabled,
        InnerAngle = InnerAngle,
        OuterAngle = OuterAngle,
        Width = Width,
        Height = Height,
        Radius = Radius,
    };

    /// <summary>
    /// The node rotation that makes a light travel along
    /// <paramref name="travelDirection"/>. A sun wants a negative Y. Use this
    /// rather than euler angles, where the pitch sign is easy to get backwards.
    /// The roll is arbitrary.
    /// </summary>
    /// <exception cref="ArgumentException">The direction has no length.</exception>
    public static Quaternion RotationForDirection(Vector3 travelDirection)
    {
        if (travelDirection.LengthSquared() < 1e-12f)
            throw new ArgumentException("A light direction needs a length.", nameof(travelDirection));

        Vector3 forward = Vector3.Normalize(travelDirection);

        // World up is parallel to a light pointing straight down.
        Vector3 reference = MathF.Abs(forward.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 right = Vector3.Normalize(Vector3.Cross(reference, forward));
        Vector3 up = Vector3.Cross(forward, right);

        // Row vectors (v * M): the third row is the forward axis, which is what
        // Scene.CollectLights reads.
        var basis = new Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f);

        return Quaternion.CreateFromRotationMatrix(basis);
    }
}
