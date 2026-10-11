namespace SpectraEngine.Core.Scripting;

/// <summary>One member of a node a script can set.</summary>
/// <param name="Id">Which member it is.</param>
/// <param name="Name">What a script calls it.</param>
/// <param name="Kind">The kind of value it takes, and reads back as.</param>
public readonly record struct SettableMember(SettableMemberId Id, string Name, MemberValueKind Kind);
