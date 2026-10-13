using SpectraEngine.Core.Scripting;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The words the Properties panel uses for the kinds of value an attribute
/// holds, in the order its dropdown lists them.
/// </summary>
public static class AttributeKindWords
{
    private static readonly (MemberValueKind Kind, string Label)[] Kinds =
    [
        (MemberValueKind.Text, "Text"),
        (MemberValueKind.Number, "Number"),
        (MemberValueKind.Boolean, "True or false"),
        (MemberValueKind.Vector3, "Three numbers"),
        (MemberValueKind.Color, "Color"),
        (MemberValueKind.CFrame, "Position and rotation"),
    ];

    /// <summary>Every label, for a dropdown.</summary>
    public static IReadOnlyList<string> Labels { get; } = Array.ConvertAll(Kinds, kind => kind.Label);

    /// <summary>What each kind is to a script, for the dropdown's tooltip.</summary>
    public const string Help =
        "What the value is. A script reads Text as a string, True or false as a boolean, Three numbers as a " +
        "Vector3, Color as a Color3, and Position and rotation as a CFrame.";

    /// <summary>The label of a kind, or empty for no kind.</summary>
    public static string LabelOf(MemberValueKind kind)
    {
        foreach ((MemberValueKind candidate, string label) in Kinds)
        {
            if (candidate == kind) return label;
        }

        return string.Empty;
    }

    /// <summary>The kind a label names, or <see cref="MemberValueKind.None"/>.</summary>
    public static MemberValueKind KindOf(string? label)
    {
        foreach ((MemberValueKind kind, string candidate) in Kinds)
        {
            if (string.Equals(candidate, label, StringComparison.Ordinal)) return kind;
        }

        return MemberValueKind.None;
    }
}
