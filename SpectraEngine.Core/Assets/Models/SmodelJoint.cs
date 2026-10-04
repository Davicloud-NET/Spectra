using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// One joint of a cooked skeleton, as its fifty-six bytes sit in a <c>SKEL</c>
/// section. Parents come before children, so a hierarchy builds in one forward pass.
/// </summary>
// The inverse bind is four rows of three; the dropped column is (0, 0, 0, 1).
// Row-vector convention: translation is the fourth row, so drop a column, not a row.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmodelJoint
{
    /// <summary>
    /// Offset into the <c>NAME</c> blob of this joint's name, or
    /// <see cref="SmodelFormat.NameOffsetAbsent"/> when it has none.
    /// </summary>
    public readonly uint NameOffset;

    /// <summary>
    /// Index of this joint's parent, always less than the joint's own index, or
    /// <see cref="NoParent"/> for a root.
    /// </summary>
    public readonly int ParentIndex;

    /// <summary>First row of the inverse bind matrix.</summary>
    public readonly Vector3 InverseBindRow0;

    /// <summary>Second row of the inverse bind matrix.</summary>
    public readonly Vector3 InverseBindRow1;

    /// <summary>Third row of the inverse bind matrix.</summary>
    public readonly Vector3 InverseBindRow2;

    /// <summary>Fourth row of the inverse bind matrix, which is the translation.</summary>
    public readonly Vector3 InverseBindRow3;

    /// <summary>What <see cref="ParentIndex"/> holds for a root joint.</summary>
    public const int NoParent = -1;

    /// <summary>Builds one joint record.</summary>
    public SmodelJoint(
        uint nameOffset,
        int parentIndex,
        Vector3 inverseBindRow0,
        Vector3 inverseBindRow1,
        Vector3 inverseBindRow2,
        Vector3 inverseBindRow3)
    {
        NameOffset = nameOffset;
        ParentIndex = parentIndex;
        InverseBindRow0 = inverseBindRow0;
        InverseBindRow1 = inverseBindRow1;
        InverseBindRow2 = inverseBindRow2;
        InverseBindRow3 = inverseBindRow3;
    }

    /// <summary>Whether this joint is a root.</summary>
    public bool IsRoot => ParentIndex == NoParent;

    /// <summary>Whether this joint carries a name record.</summary>
    public bool HasName => NameOffset != SmodelFormat.NameOffsetAbsent;

    /// <summary>The stored rows as a full matrix.</summary>
    public Matrix4x4 InverseBind => new(
        InverseBindRow0.X, InverseBindRow0.Y, InverseBindRow0.Z, 0f,
        InverseBindRow1.X, InverseBindRow1.Y, InverseBindRow1.Z, 0f,
        InverseBindRow2.X, InverseBindRow2.Y, InverseBindRow2.Z, 0f,
        InverseBindRow3.X, InverseBindRow3.Y, InverseBindRow3.Z, 1f);
}
