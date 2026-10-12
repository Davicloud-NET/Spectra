namespace SpectraEngine.Entities.Tests;

// Source the script API generator's tests compile.
internal static class ScriptApiFixtures
{
    public const string Usings = """
        using System;
        using System.Numerics;
        using SpectraEngine.Core.Scene;
        using SpectraEngine.Core.Scripting;
        using SpectraEngine.Core.Scripting.Api;
        using SpectraEngine.Scripting.Luau;
        using SpectraEngine.Scripting.Luau.Runtime;

        namespace TestGame.Scripting;

        """;

    // Input of the committed snapshot: a class of its own objects, a class
    // over nodes with settable properties and one that extends it, a value
    // type, a library and globals, with a member of every shape that is bound.
    public const string Representative = Usings + """
        /// <summary>A lamp a script switches.</summary>
        [ScriptClass("Lamp")]
        internal sealed partial class Lamp : ScriptObject
        {
            /// <summary>Whether it shines.</summary>
            [ScriptProperty(Name = "Enabled")]
            public bool IsEnabled { get; set; }

            /// <summary>The lamp beside it, or nil.</summary>
            [ScriptProperty]
            public Lamp? Next { get; set; }

            /// <summary>Fires when it is switched, with whether it shines now.</summary>
            [ScriptSignal("enabled: boolean")]
            public Signal Switched { get; } = new();

            /// <summary>Switches it and says whether it shines now.</summary>
            [ScriptMethod]
            public bool Toggle() => IsEnabled = !IsEnabled;

            /// <summary>Dims it over some seconds.</summary>
            [ScriptMethod(Parameters = "seconds: number?")]
            private int Dim(LuauStack stack) => 0;
        }

        /// <summary>Something a script connects a function to.</summary>
        [ScriptClass("RBXScriptSignal", TypeParameters = "T... = ...any")]
        internal sealed partial class Signal : ScriptObject
        {
            /// <summary>Calls the function at every fire.</summary>
            [ScriptMethod(Parameters = "callback: (T...) -> ()")]
            private int Connect(LuauStack stack) => 0;
        }

        /// <summary>A node of the level.</summary>
        [ScriptClass("Instance", ChildType = "Instance")]
        internal static partial class NodeApi
        {
            /// <summary>Its name.</summary>
            [ScriptProperty(Sets = SettableMemberId.Name)]
            public static partial string Name(SceneNode node);

            /// <summary>Where it is.</summary>
            [ScriptProperty(Sets = SettableMemberId.Position)]
            internal static partial Vector3 Position(SceneNode node);

            /// <summary>Where it is and how it is turned.</summary>
            [ScriptProperty(Sets = SettableMemberId.CFrame)]
            public static partial CFrame CFrame(SceneNode node);

            /// <summary>The node above it.</summary>
            [ScriptProperty(CannotSet = "Parent cannot be set yet")]
            public static SceneNode? Parent(SceneNode node) => node.Parent;

            /// <summary>The child of that name, or nil.</summary>
            [ScriptMethod]
            public static SceneNode? FindFirstChild(SceneNode node, ReadOnlySpan<byte> name, bool recursive = false) => null;

            /// <summary>Whether it is under the other node.</summary>
            [ScriptMethod]
            public static bool IsDescendantOf(SceneNode node, SceneNode ancestor) => false;

            /// <summary>Its path from the root.</summary>
            [ScriptMethod]
            public static string GetFullName(SceneNode node) => node.Name;
        }

        /// <summary>A node with a shape.</summary>
        [ScriptClass("Part", Extends = "Instance")]
        internal static partial class PartApi
        {
            /// <summary>How heavy it is.</summary>
            [ScriptProperty]
            public static float Mass(SceneNode node) => 1f;

            /// <summary>Whether it touches the other part.</summary>
            [ScriptMethod]
            public static bool IsTouching(SceneNode node, SceneNode other) => false;
        }

        /// <summary>Three numbers.</summary>
        [ScriptValue("Point")]
        [ScriptOperator("+", "Point", "Point")]
        internal static partial class PointApi
        {
            /// <summary>The length.</summary>
            [ScriptProperty]
            public static float Magnitude(Vector3 self) => self.Length();

            /// <summary>Part of the way to the goal.</summary>
            [ScriptMethod]
            public static Vector3 Lerp(Vector3 self, Vector3 goal, float alpha = 0.5f) => Vector3.Lerp(self, goal, alpha);

            /// <summary>Turned about an axis, or about up.</summary>
            [ScriptMethod]
            public static CFrame Turned(Vector3 self, double angle, Vector3? axis = null, string? label = null) => default;

            /// <summary>Its numbers.</summary>
            [ScriptMethod]
            public static (float X, float Y, float Z) Unpack(Vector3 self) => (self.X, self.Y, self.Z);

            /// <summary>The same as Unpack.</summary>
            [ScriptMethod(Name = "components", Returns = "(number, number, number)")]
            [ScriptDeprecated("Unpack")]
            public static int Components(LuauStack stack, Vector3 self) => 3;

            [ScriptOperator("*", "number", "Point")]
            public static int Multiply(LuauStack stack) => 1;
        }

        /// <summary>Makes points.</summary>
        [ScriptLibrary("Point")]
        internal static partial class PointLibrary
        {
            /// <summary>The origin.</summary>
            [ScriptProperty(Name = "zero")]
            public static Vector3 Zero => Vector3.Zero;

            /// <summary>No turn.</summary>
            [ScriptProperty(Name = "identity")]
            public static CFrame Identity => CFrame.Identity;

            /// <summary>How many numbers a point has.</summary>
            [ScriptProperty(Name = "size")]
            public static int Size => 3;

            /// <summary>Makes a point.</summary>
            [ScriptFunction(Name = "new")]
            [ScriptOverload("", "Point")]
            [ScriptOverload("x: number, y: number", "Point")]
            private static int New(LuauStack stack) => 1;

            /// <summary>The point between two.</summary>
            [ScriptFunction(Name = "between")]
            public static Vector3 Between(Vector3 a, Vector3 b) => (a + b) / 2f;
        }

        [ScriptGlobals]
        internal sealed partial class LevelGlobals
        {
            /// <summary>The lamp of the level.</summary>
            [ScriptProperty(Name = "lamp")]
            public Lamp Lamp { get; } = new();

            /// <summary>The node of the running script.</summary>
            [ScriptProperty(Name = "script", Type = "Instance", ByHand = true)]
            private static object ScriptOf(SceneNode node) => node;

            /// <summary>Waits.</summary>
            [ScriptFunction(Name = "wait", Parameters = "seconds: number?", Returns = "(number, number)")]
            [ScriptDeprecated("task.wait")]
            private int Wait(LuauStack stack) => 0;

            /// <summary>Writes a warning.</summary>
            [ScriptFunction(Name = "warn", Parameters = "...: any", ByHand = true)]
            private static int Warn(nint state) => 0;

            /// <summary>The larger of two numbers.</summary>
            [ScriptFunction(Name = "larger")]
            public static double Larger(double a, double b) => Math.Max(a, b);
        }
        """;

    // The caching tests' class, and three edits of it. The edits are inside
    // the class, or Roslyn reuses its node and the transform does not re-run.
    public const string Cached = Usings + """
        /// <summary>A counter.</summary>
        [ScriptValue("Counter")]
        internal static partial class CounterApi
        {
            /// <summary>Its length.</summary>
            [ScriptProperty]
            public static float Length(Vector3 self) => self.Length();

            /// <summary>Adds to it.</summary>
            [ScriptMethod]
            public static Vector3 Add(Vector3 self, float amount) => self + new Vector3(amount);
        }
        """;

    // A member the generator ignores, appended last so no recorded span moves.
    public const string CachedWithSpareMember = Usings + """
        /// <summary>A counter.</summary>
        [ScriptValue("Counter")]
        internal static partial class CounterApi
        {
            /// <summary>Its length.</summary>
            [ScriptProperty]
            public static float Length(Vector3 self) => self.Length();

            /// <summary>Adds to it.</summary>
            [ScriptMethod]
            public static Vector3 Add(Vector3 self, float amount) => self + new Vector3(amount);

            private static int Spare() => 1;
        }
        """;

    // Other words of the same length, so no span moves and only the words differ.
    public const string CachedWithOtherWords = Usings + """
        /// <summary>A counter.</summary>
        [ScriptValue("Counter")]
        internal static partial class CounterApi
        {
            /// <summary>Its extent.</summary>
            [ScriptProperty]
            public static float Length(Vector3 self) => self.Length();

            /// <summary>Adds to it.</summary>
            [ScriptMethod]
            public static Vector3 Add(Vector3 self, float amount) => self + new Vector3(amount);
        }
        """;

    // A longer summary on the class moves the class and all in it.
    public const string CachedWithLongerClassSummary = Usings + """
        /// <summary>A counter of the things a level has.</summary>
        [ScriptValue("Counter")]
        internal static partial class CounterApi
        {
            /// <summary>Its length.</summary>
            [ScriptProperty]
            public static float Length(Vector3 self) => self.Length();

            /// <summary>Adds to it.</summary>
            [ScriptMethod]
            public static Vector3 Add(Vector3 self, float amount) => self + new Vector3(amount);
        }
        """;

    public const string CachedWithExtraMember = Usings + """
        /// <summary>A counter.</summary>
        [ScriptValue("Counter")]
        internal static partial class CounterApi
        {
            /// <summary>Its length.</summary>
            [ScriptProperty]
            public static float Length(Vector3 self) => self.Length();

            /// <summary>Adds to it.</summary>
            [ScriptMethod]
            public static Vector3 Add(Vector3 self, float amount) => self + new Vector3(amount);

            /// <summary>Its opposite.</summary>
            [ScriptMethod]
            public static Vector3 Negate(Vector3 self) => -self;
        }
        """;
}
