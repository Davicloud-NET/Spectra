namespace SpectraEngine.Core.Scripting;

/// <summary>The shapes a <see cref="MemberValue"/> comes in.</summary>
public enum MemberValueKind : byte
{
    /// <summary>No value.</summary>
    None = 0,

    /// <summary>Text.</summary>
    Text,

    /// <summary>Three numbers.</summary>
    Vector3,

    /// <summary>A position and a rotation.</summary>
    CFrame,
}
