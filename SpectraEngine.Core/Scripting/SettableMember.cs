using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting;

/// <summary>One member of a node a script can set.</summary>
/// <param name="Id">Which member it is.</param>
/// <param name="Name">What a script calls it.</param>
/// <param name="Kind">The kind of value it takes, and reads back as.</param>
public readonly record struct SettableMember(SettableMemberId Id, string Name, MemberValueKind Kind)
{
    /// <summary>The classes of a member every node has.</summary>
    public static readonly IReadOnlyList<string> EveryNode = [NodeClass.Instance];

    private readonly IReadOnlyList<string>? _classes;

    /// <summary>
    /// The script classes that have the member, as <see cref="NodeClass"/>
    /// names them. A node of a class that extends one of them has it too.
    /// <see cref="EveryNode"/> unless said.
    /// </summary>
    public IReadOnlyList<string> Classes
    {
        get => _classes ?? EveryNode;
        init => _classes = value;
    }
}
