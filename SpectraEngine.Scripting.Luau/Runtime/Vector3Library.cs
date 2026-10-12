using System.Numerics;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>The way a script makes a Vector3, and the vectors that have a name.</summary>
[ScriptLibrary("Vector3")]
internal static partial class Vector3Library
{
    /// <summary>The vector 0, 0, 0.</summary>
    [ScriptProperty(Name = "zero")]
    public static Vector3 Zero => Vector3.Zero;

    /// <summary>The vector 1, 1, 1.</summary>
    [ScriptProperty(Name = "one")]
    public static Vector3 One => Vector3.One;

    /// <summary>The vector 1, 0, 0.</summary>
    [ScriptProperty(Name = "xAxis")]
    public static Vector3 XAxis => Vector3.UnitX;

    /// <summary>The vector 0, 1, 0.</summary>
    [ScriptProperty(Name = "yAxis")]
    public static Vector3 YAxis => Vector3.UnitY;

    /// <summary>The vector 0, 0, 1.</summary>
    [ScriptProperty(Name = "zAxis")]
    public static Vector3 ZAxis => Vector3.UnitZ;

    /// <summary>Makes a vector from three numbers. One that is left out is 0.</summary>
    // The compiler makes the vector itself when it sees three numbers. This
    // is for every other call: fewer numbers, or the function passed around.
    [ScriptFunction(Name = "new")]
    public static Vector3 New(float x = 0f, float y = 0f, float z = 0f) => new(x, y, z);
}
