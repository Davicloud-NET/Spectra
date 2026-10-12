using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SpectraEngine.Entities.Generator.ScriptApi;

namespace SpectraEngine.Entities.Tests;

// Runs ScriptApiGenerator over an in-memory compilation: a fixture, and the
// part of the binding that generated code calls, as stubs.
internal static class ScriptApiHarness
{
    // What the binding has for generated code to call, with nothing behind
    // it. The real binding is in SpectraEngine.Scripting.Luau, which this
    // suite does not reference. Its own build is what holds the two alike.
    public const string BindingStubs = """
        #nullable enable
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Numerics;
        using SpectraEngine.Core.Scene;
        using SpectraEngine.Core.Scripting;

        namespace SpectraEngine.Scripting.Luau
        {
            public readonly ref struct LuauStack
            {
                public const int Raise = -2;
                public bool Push(bool value) => true;
                public bool Push(double value) => true;
                public bool Push(Vector3 value) => true;
                public bool Push(ReadOnlySpan<char> text) => true;
                public bool PushNil() => true;
                public bool IsNil(int index) => true;
                public int Fail(ReadOnlySpan<char> message) => -1;
            }

            public delegate int LuauHostFunction(LuauStack stack);

            public sealed class LuauHost
            {
            }
        }

        namespace SpectraEngine.Scripting.Luau.Runtime
        {
            internal static class ScriptMember
            {
                public const int None = int.MinValue;
                public const int ReadOnly = int.MinValue + 1;
                public const int NotCallable = int.MinValue + 2;
            }

            internal abstract class ScriptObject
            {
                public abstract string ClassName { get; }
                internal virtual int ReadMember(ScriptContext context, LuauStack stack, int atom) => ScriptMember.None;
                internal virtual int WriteMember(ScriptContext context, LuauStack stack, int atom) => ScriptMember.None;
                internal virtual int InvokeMember(ScriptContext context, LuauStack stack, int atom) => ScriptMember.None;
            }

            internal sealed class MethodTable
            {
            }

            internal sealed class ScriptContext
            {
                public LuauHost Host { get; } = new();
                public bool PushObject(LuauStack stack, object? value) => true;
                public bool TryGetObject<T>(LuauStack stack, int index, [NotNullWhen(true)] out T? value)
                    where T : class
                {
                    value = null;
                    return false;
                }

                public int PushMethod(LuauStack stack, int classId, ReadOnlySpan<byte> name) => 1;
                public void SetGlobalFunction(ReadOnlySpan<byte> name, LuauHostFunction function) { }
                public void SetGlobalObject(ReadOnlySpan<byte> name, object value) { }
                public int WriteText(LuauStack stack, SceneNode node, SettableMemberId member) => 0;
                public int WriteVector(LuauStack stack, SceneNode node, SettableMemberId member) => 0;
                public int WriteFrame(LuauStack stack, SceneNode node, SettableMemberId member) => 0;
            }

            internal static class ScriptArguments
            {
                public static bool TryDouble(LuauStack stack, int index, out double value) { value = 0; return true; }
                public static bool TryBoolean(LuauStack stack, int index, out bool value) { value = false; return true; }
                public static bool TryText(LuauStack stack, int index, [NotNullWhen(true)] out string? value) { value = ""; return true; }
                public static bool TryUtf8(LuauStack stack, int index, out ReadOnlySpan<byte> value) { value = default; return true; }
                public static bool TryVector(LuauStack stack, int index, out Vector3 value) { value = default; return true; }
                public static bool TryFrame(LuauStack stack, int index, out CFrame value) { value = default; return true; }
                public static int Wrong(LuauStack stack, int index, string function, string expected, bool isMethod = false) => -1;
                public static int NoSelf(LuauStack stack, string method) => -1;
            }

            internal static class FrameValue
            {
                public static bool Push(LuauStack stack, CFrame value) => true;
            }

            internal readonly ref struct ApiTable
            {
                public ApiTable(LuauHost host) { }
                public ApiTable Function(ReadOnlySpan<byte> name, LuauHostFunction function) => this;
                public ApiTable Value(ReadOnlySpan<byte> name, double value) => this;
                public ApiTable Value(ReadOnlySpan<byte> name, bool value) => this;
                public ApiTable Value(ReadOnlySpan<byte> name, string value) => this;
                public ApiTable Value(ReadOnlySpan<byte> name, Vector3 value) => this;
                public ApiTable Frame(ReadOnlySpan<byte> name, CFrame value) => this;
                public void SetAsGlobal(ReadOnlySpan<byte> name) { }
                public MethodTable KeepAsMethods() => new();
            }
        }
        """;

    // trackSteps makes the driver keep every intermediate table.
    public static GeneratorDriver Driver(bool trackSteps = false) =>
        CSharpGeneratorDriver.Create(
            generators: [new ScriptApiGenerator().AsSourceGenerator()],
            additionalTexts: [],
            parseOptions: null,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: trackSteps));

    public static SyntaxTree Stubs() => GeneratorHarness.Tree(BindingStubs, "BindingStubs.cs");

    public static GeneratorRun Run(string source)
    {
        GeneratorDriver driver = Driver().RunGeneratorsAndUpdateCompilation(
            GeneratorHarness.Compilation(Stubs(), GeneratorHarness.Tree(source, "Fixture.cs")),
            out Compilation output,
            out _,
            TestContext.Current.CancellationToken);

        return new GeneratorRun(driver.GetRunResult(), output);
    }
}
