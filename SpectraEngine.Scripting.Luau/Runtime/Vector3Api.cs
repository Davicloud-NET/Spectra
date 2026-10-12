using System.Numerics;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>Three numbers: a position, a direction or a size. It is Luau's own vector type.</summary>
// The arithmetic and a component written out, as in v.X, are Luau's and
// call nothing here. The component properties below answer a name that was
// worked out, as in v[axis].
[ScriptValue("Vector3")]
[ScriptOperator("+", "Vector3", "Vector3")]
[ScriptOperator("-", "Vector3", "Vector3")]
[ScriptOperator("*", "Vector3 | number", "Vector3")]
[ScriptOperator("/", "Vector3 | number", "Vector3")]
[ScriptOperator("//", "Vector3 | number", "Vector3")]
[ScriptOperator("unm", "", "Vector3")]
internal static partial class Vector3Api
{
    /// <summary>The first number.</summary>
    [ScriptProperty]
    public static float X(Vector3 self) => self.X;

    /// <summary>The second number.</summary>
    [ScriptProperty]
    public static float Y(Vector3 self) => self.Y;

    /// <summary>The third number.</summary>
    [ScriptProperty]
    public static float Z(Vector3 self) => self.Z;

    /// <summary>The same as X, in Luau's own spelling.</summary>
    [ScriptProperty(Name = "x")]
    public static float LowerX(Vector3 self) => self.X;

    /// <summary>The same as Y, in Luau's own spelling.</summary>
    [ScriptProperty(Name = "y")]
    public static float LowerY(Vector3 self) => self.Y;

    /// <summary>The same as Z, in Luau's own spelling.</summary>
    [ScriptProperty(Name = "z")]
    public static float LowerZ(Vector3 self) => self.Z;

    /// <summary>The length.</summary>
    [ScriptProperty]
    public static float Magnitude(Vector3 self) => self.Length();

    /// <summary>The same direction with a length of one.</summary>
    [ScriptProperty]
    public static Vector3 Unit(Vector3 self) => Vector3.Normalize(self);

    /// <summary>The dot product with another vector.</summary>
    [ScriptMethod]
    public static float Dot(Vector3 self, Vector3 other) => Vector3.Dot(self, other);

    /// <summary>The cross product with another vector.</summary>
    [ScriptMethod]
    public static Vector3 Cross(Vector3 self, Vector3 other) => Vector3.Cross(self, other);

    /// <summary>The vector part of the way to the goal: 0 is this vector and 1 is the goal.</summary>
    [ScriptMethod]
    public static Vector3 Lerp(Vector3 self, Vector3 goal, float alpha) => Vector3.Lerp(self, goal, alpha);
}
