using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// One 56-byte <c>NATR</c> record: one attribute of one node. Records are in
/// node order, and a node's own are in the order they were added. Only the
/// fields the kind names hold the value. The rest are written zero.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapAttributeRecord
{
    /// <summary>The <see cref="KindRaw"/> of text. The value is <see cref="TextString"/>.</summary>
    public const byte TextKind = 1;

    /// <summary>The <see cref="KindRaw"/> of true or false. The value is <see cref="Number"/>, 1 or 0.</summary>
    public const byte BooleanKind = 2;

    /// <summary>The <see cref="KindRaw"/> of a number. The value is <see cref="Number"/>.</summary>
    public const byte NumberKind = 3;

    /// <summary>The <see cref="KindRaw"/> of three numbers. The value is <see cref="Three"/>.</summary>
    public const byte Vector3Kind = 4;

    /// <summary>The <see cref="KindRaw"/> of a colour. The value is <see cref="Three"/>: red, green, blue.</summary>
    public const byte Color3Kind = 5;

    /// <summary>
    /// The <see cref="KindRaw"/> of a position and a rotation. The value is
    /// <see cref="Three"/> and <see cref="Rotation"/>.
    /// </summary>
    public const byte CFrameKind = 6;

    /// <summary>Index into <c>NODE</c> of the node that holds the attribute.</summary>
    public readonly uint NodeIndex;

    /// <summary>Index into <c>STRT</c> of the attribute's name.</summary>
    public readonly uint NameString;

    /// <summary>One of the kinds above.</summary>
    public readonly byte KindRaw;

    /// <summary>Reserved; written zero.</summary>
    public readonly byte Reserved0;

    /// <summary>Reserved; written zero.</summary>
    public readonly ushort Reserved1;

    /// <summary>Index into <c>STRT</c> of the text, for <see cref="TextKind"/>.</summary>
    public readonly uint TextString;

    /// <summary>The number, or 1 for true and 0 for false.</summary>
    public readonly double Number;

    /// <summary>Three numbers, a colour's three parts, or a position.</summary>
    public readonly Vector3 Three;

    /// <summary>A rotation, for <see cref="CFrameKind"/>.</summary>
    public readonly Quaternion Rotation;

    /// <summary>Reserved; written zero.</summary>
    public readonly uint Reserved2;

    /// <summary>
    /// Builds the record of one attribute. Text is handed over as the index
    /// it was interned at.
    /// </summary>
    public ScmapAttributeRecord(uint nodeIndex, uint nameString, in MemberValue value, uint textString)
    {
        NodeIndex = nodeIndex;
        NameString = nameString;
        KindRaw = KindOf(value.Kind);
        Reserved0 = 0;
        Reserved1 = 0;
        TextString = value.Kind == MemberValueKind.Text ? textString : 0;
        Number = value.Kind switch
        {
            MemberValueKind.Number => value.Number,
            MemberValueKind.Boolean => value.Boolean ? 1d : 0d,
            _ => 0d,
        };
        Three = value.Kind switch
        {
            MemberValueKind.Vector3 or MemberValueKind.Color => value.Vector,
            MemberValueKind.CFrame => value.Frame.Position,
            _ => default,
        };
        Rotation = value.Kind == MemberValueKind.CFrame ? value.Frame.Rotation : default;
        Reserved2 = 0;
    }

    /// <summary>Whether <see cref="KindRaw"/> is a kind this engine has a value for.</summary>
    public bool IsKnownKind => KindRaw is >= TextKind and <= CFrameKind;

    /// <summary>The value. The text is handed in, since the record holds its index.</summary>
    public MemberValue ToValue(string text) => KindRaw switch
    {
        TextKind => MemberValue.Of(text),
        BooleanKind => MemberValue.Of(Number != 0d),
        NumberKind => MemberValue.Of(Number),
        Vector3Kind => MemberValue.Of(Three),
        Color3Kind => MemberValue.Of(new Color3(Three.X, Three.Y, Three.Z)),
        CFrameKind => MemberValue.Of(new CFrame(Three, Rotation)),
        _ => MemberValue.None,
    };

    private static byte KindOf(MemberValueKind kind) => kind switch
    {
        MemberValueKind.Text => TextKind,
        MemberValueKind.Boolean => BooleanKind,
        MemberValueKind.Number => NumberKind,
        MemberValueKind.Vector3 => Vector3Kind,
        MemberValueKind.Color => Color3Kind,
        MemberValueKind.CFrame => CFrameKind,
        _ => 0,
    };
}
