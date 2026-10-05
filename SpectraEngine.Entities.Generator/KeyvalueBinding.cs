using Microsoft.CodeAnalysis;

namespace SpectraEngine.Entities.Generator;

// The C# types a keyvalue can be stored in.
internal enum ClrKind
{
    Unknown = 0,
    Bool,
    Int,
    UInt,
    Float,
    String,
    Vector2,
    Vector3,
    Vector4,
    Guid,
}

// Name is the KeyvalueType member name, Value its wire byte. Reader is the
// KeyvalueWire method, or null when the wire form is the value.
internal readonly record struct KeyvalueRow(
    string Name,
    byte Value,
    ClrKind Clr,
    string CSharpType,
    string? Reader);

// Mirrors Core's KeyvalueType, which this assembly cannot reference. The
// numbering is a wire byte: append only, never renumber.
// Kinds that share a C# type (Color and Angles in a Vector3, asset paths and
// TargetName in a string, Distance in a float) are never inferred and must be
// stated.
internal static class KeyvalueBinding
{
    // KeyvalueType.NodeRef.
    public const byte NodeRefValue = 10;

    private static readonly KeyvalueRow[] Rows =
    [
        new KeyvalueRow("Bool", 0, ClrKind.Bool, "bool", "TryParseBool"),
        new KeyvalueRow("Int", 1, ClrKind.Int, "int", "TryParseInt"),
        new KeyvalueRow("Float", 2, ClrKind.Float, "float", "TryParseFloat"),
        new KeyvalueRow("String", 3, ClrKind.String, "string", null),
        new KeyvalueRow("Vec2", 4, ClrKind.Vector2, "global::System.Numerics.Vector2", "TryParseVec2"),
        new KeyvalueRow("Vec3", 5, ClrKind.Vector3, "global::System.Numerics.Vector3", "TryParseVec3"),
        new KeyvalueRow("Vec4", 6, ClrKind.Vector4, "global::System.Numerics.Vector4", "TryParseVec4"),
        new KeyvalueRow("Color", 7, ClrKind.Vector3, "global::System.Numerics.Vector3", "TryParseColor"),
        new KeyvalueRow("Angles", 8, ClrKind.Vector3, "global::System.Numerics.Vector3", "TryParseAngles"),
        new KeyvalueRow("TargetName", 9, ClrKind.String, "string", null),
        new KeyvalueRow("NodeRef", NodeRefValue, ClrKind.Guid, "global::System.Guid", "TryParseNodeRef"),
        new KeyvalueRow("AssetModel", 11, ClrKind.String, "string", null),
        new KeyvalueRow("AssetMaterial", 12, ClrKind.String, "string", null),
        new KeyvalueRow("AssetTexture", 13, ClrKind.String, "string", null),
        new KeyvalueRow("AssetSound", 14, ClrKind.String, "string", null),
        new KeyvalueRow("Choices", 15, ClrKind.String, "string", null),
        new KeyvalueRow("Flags", 16, ClrKind.UInt, "uint", "TryParseFlags"),
        new KeyvalueRow("Distance", 17, ClrKind.Float, "float", "TryParseFloat"),
    ];

    public static bool TryGet(byte value, out KeyvalueRow row)
    {
        for (int i = 0; i < Rows.Length; i++)
        {
            if (Rows[i].Value == value)
            {
                row = Rows[i];
                return true;
            }
        }

        row = default;
        return false;
    }

    // The KeyvalueType a member gets when the author states none.
    public static bool TryInfer(ClrKind kind, out KeyvalueRow row)
    {
        // First row for the kind wins, so the table lists each C# type's
        // primary meaning first: Vec3 before Color, String before TargetName.
        for (int i = 0; i < Rows.Length; i++)
        {
            if (Rows[i].Clr == kind)
            {
                row = Rows[i];
                return true;
            }
        }

        row = default;
        return false;
    }

    public static ClrKind Classify(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return ClrKind.Bool;
            case SpecialType.System_Int32:
                return ClrKind.Int;
            case SpecialType.System_UInt32:
                return ClrKind.UInt;
            case SpecialType.System_Single:
                return ClrKind.Float;
            case SpecialType.System_String:
                return ClrKind.String;
        }

        // By full name: the transform holds no compilation to look symbols up in.
        switch (type.ToDisplayString())
        {
            case "System.Numerics.Vector2":
                return ClrKind.Vector2;
            case "System.Numerics.Vector3":
                return ClrKind.Vector3;
            case "System.Numerics.Vector4":
                return ClrKind.Vector4;
            case "System.Guid":
                return ClrKind.Guid;
            default:
                return ClrKind.Unknown;
        }
    }
}
