using System;
using System.Numerics;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// One 48-byte <c>LGHT</c> record: the light a node carries. Records are in node
/// order, at most one per node. The node supplies position and direction.
/// </summary>
// A side table, not a payload kind: a node can carry a light beside a brush or
// an entity.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapLightRecord
{
    /// <summary>The <see cref="Flags"/> bit of a light that is switched off.</summary>
    // Stored inverted, so a record with no flag set has a light's defaults.
    public const ushort DisabledFlag = 1 << 0;

    /// <summary>Index into <c>NODE</c> of the node this light sits on.</summary>
    public readonly uint NodeIndex;

    /// <summary>
    /// The light's shape: 0 directional, 1 point, 2 spot, 3 rect, 4 disc. Read
    /// it through <see cref="TryDecodeKind"/>.
    /// </summary>
    // The file's own numbering. LightKind may be reordered without moving it.
    public readonly ushort KindRaw;

    /// <summary>Flag bits. See <see cref="DisabledFlag"/>.</summary>
    public readonly ushort Flags;

    /// <summary>Linear RGB colour.</summary>
    public readonly Vector3 Color;

    /// <summary>Multiplier on <see cref="Color"/>. Never negative.</summary>
    public readonly float Intensity;

    /// <summary>Distance at which the light reaches zero. Always positive.</summary>
    public readonly float Range;

    /// <summary>A spot light's fully lit half-angle, in degrees.</summary>
    public readonly float InnerAngle;

    /// <summary>A spot light's outer half-angle, in degrees.</summary>
    public readonly float OuterAngle;

    /// <summary>A rect light's width in world units.</summary>
    public readonly float Width;

    /// <summary>A rect light's height in world units.</summary>
    public readonly float Height;

    /// <summary>A disc light's radius in world units.</summary>
    public readonly float Radius;

    /// <summary>Builds the record of <paramref name="light"/> on node <paramref name="nodeIndex"/>.</summary>
    public ScmapLightRecord(uint nodeIndex, Light light)
    {
        ArgumentNullException.ThrowIfNull(light);

        NodeIndex = nodeIndex;
        KindRaw = EncodeKind(light.Kind);
        Flags = light.Enabled ? (ushort)0 : DisabledFlag;
        Color = light.Color;
        Intensity = light.Intensity;
        Range = light.Range;
        InnerAngle = light.InnerAngle;
        OuterAngle = light.OuterAngle;
        Width = light.Width;
        Height = light.Height;
        Radius = light.Radius;
    }

    /// <summary>Whether the light contributes at all.</summary>
    public bool Enabled => (Flags & DisabledFlag) == 0;

    /// <summary>
    /// A new light with this record's settings. Call only on a record the reader
    /// has accepted: <see cref="Light"/> throws on a range or an intensity it
    /// cannot hold.
    /// </summary>
    public Light ToLight()
    {
        TryDecodeKind(KindRaw, out LightKind kind);

        // Inner angle before outer: OuterAngle clamps against the inner one.
        return new Light
        {
            Kind = kind,
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
    }

    /// <summary>Turns a stored kind into a <see cref="LightKind"/>. False for a value the format does not define.</summary>
    public static bool TryDecodeKind(ushort raw, out LightKind kind)
    {
        switch (raw)
        {
            case 0: kind = LightKind.Directional; return true;
            case 1: kind = LightKind.Point; return true;
            case 2: kind = LightKind.Spot; return true;
            case 3: kind = LightKind.Rect; return true;
            case 4: kind = LightKind.Disc; return true;
            default: kind = default; return false;
        }
    }

    private static ushort EncodeKind(LightKind kind) => kind switch
    {
        LightKind.Directional => 0,
        LightKind.Point => 1,
        LightKind.Spot => 2,
        LightKind.Rect => 3,
        LightKind.Disc => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No .scmap value for this light kind."),
    };
}
