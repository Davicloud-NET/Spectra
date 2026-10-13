using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Writes the binding of a class or a value: a read, a write and a call of a
// member, each a switch on the member's atom, and the table a method is
// handed out from when a script reads it without calling it.
internal static class ScriptClassEmitter
{
    private const string Signature = ScriptNames.Context + " context, " + ScriptNames.Stack + " stack";

    public static void Emit(SourceWriter w, ScriptContainerModel model)
    {
        if (model.Self is not { } self)
            return;

        ScriptFunctionModel[] functions = model.Functions.ToArray();
        EmitRead(w, model, self);
        if (model.Kind == ScriptContainerKind.Class)
            EmitWrite(w, model, self);

        EmitInvoke(w, model, self, functions);
        foreach (ScriptFunctionModel function in functions.Where(function => !function.IsByStack))
            ScriptFunctionEmitter.EmitGlue(w, function, self, isStatic: true);

        if (functions.Length > 0)
            EmitMethods(w, self, functions);

        foreach (ScriptPropertyModel property in model.Properties.Where(property => property.Sets.Length > 0))
            EmitDoorRead(w, property, self);

        if (!model.IsStatic)
            EmitOverrides(w, model);
    }

    private static void EmitRead(SourceWriter w, ScriptContainerModel model, ScriptTypeModel self)
    {
        w.Line("/// <summary>Pushes the member with this atom. <c>ScriptMember.None</c> when there is none.</summary>");
        w.Open($"internal static int ScriptRead({Signature}, {self.ClrType} self, int atom)");
        w.Open("switch (atom)");

        foreach (ScriptPropertyModel property in model.Properties)
        {
            w.Line($"case {Atom(property.LuauName)}:");
            string read = string.Format(property.ReadFormat, "self");
            w.Indent().Line($"return {ScriptMarshalling.Push(property.Type, read)} ? 1 : {ScriptNames.Raise};").Outdent();
        }

        foreach (ScriptFunctionModel function in model.Functions)
        {
            w.Line($"case {Atom(function.LuauName)}:");
            w.Indent();
            w.Line(
                $"return context.PushMethod(stack, {ScriptNames.ClassIds}.{ScriptNames.Identifier(model.LuauName)}, " +
                $"{ScriptNames.Utf8(function.LuauName)});");
            w.Outdent();
        }

        EmitDefault(w, model, "Read");
        w.Close();
        w.Close();
        w.Line();
    }

    // The value to write is the third argument of __newindex.
    private static void EmitWrite(SourceWriter w, ScriptContainerModel model, ScriptTypeModel self)
    {
        w.Line("/// <summary>Sets the member with this atom to the third value on the stack.</summary>");
        w.Open($"internal static int ScriptWrite({Signature}, {self.ClrType} self, int atom)");
        w.Open("switch (atom)");

        var readOnly = new List<string>();
        foreach (ScriptPropertyModel property in model.Properties)
        {
            if (property.Sets.Length > 0)
            {
                string door = property.Type.Kind switch
                {
                    ScriptValueKind.Text => "WriteText",
                    ScriptValueKind.Vector3 => "WriteVector",
                    ScriptValueKind.Boolean => "WriteBoolean",
                    ScriptValueKind.Number => "WriteNumber",
                    ScriptValueKind.Color3 => "WriteColor",
                    _ => "WriteFrame",
                };
                w.Line($"case {Atom(property.LuauName)}:");
                w.Indent().Line($"return context.{door}(stack, self, {ScriptNames.Core}.SettableMemberId.{property.Sets});").Outdent();
            }
            else if (property.SetBy.Length > 0)
            {
                EmitSetBy(w, property);
            }
            else if (property.CannotSet.Length > 0)
            {
                w.Line($"case {Atom(property.LuauName)}:");
                w.Indent().Line($"return stack.Fail({ScriptNames.Literal(property.CannotSet)});").Outdent();
            }
            else
            {
                readOnly.Add(property.LuauName);
            }
        }

        readOnly.AddRange(model.Functions.Select(function => function.LuauName));
        EmitCases(w, readOnly, $"return {ScriptNames.Member}.ReadOnly;");
        EmitDefault(w, model, "Write");
        w.Close();
        w.Close();
        w.Line();
    }

    // A write the object takes itself: its method gets the value, and what
    // it returns is why it refused it, which the script reads as its error.
    private static void EmitSetBy(SourceWriter w, ScriptPropertyModel property)
    {
        w.Open($"case {Atom(property.LuauName)}:");
        ScriptMarshalling.EmitReadAssigned(w, property.Type, property.LuauName);
        w.Line();
        w.Line(
            $"return self.{property.SetBy}({ScriptMarshalling.Assigned(property.Type)}) is {{ }} refusal " +
            "? stack.Fail(refusal) : 0;");
        w.Close();
    }

    // What __namecall comes to: the method is called with the object it was
    // called on, and its arguments start at the second value.
    private static void EmitInvoke(
        SourceWriter w, ScriptContainerModel model, ScriptTypeModel self, ScriptFunctionModel[] functions)
    {
        w.Line("/// <summary>Calls the method with this atom on the object. Its arguments start at the second value.</summary>");
        w.Open($"internal static int ScriptInvoke({Signature}, {self.ClrType} self, int atom)");
        w.Open("switch (atom)");

        foreach (ScriptFunctionModel function in functions)
        {
            w.Line($"case {Atom(function.LuauName)}:");
            w.Indent().Line($"return {ScriptFunctionEmitter.CallOnSelf(function)};").Outdent();
        }

        EmitCases(w, model.Properties.Select(property => property.LuauName).ToList(), $"return {ScriptNames.Member}.NotCallable;");
        EmitDefault(w, model, "Invoke");
        w.Close();
        w.Close();
        w.Line();
    }

    // One function for each method, made once. It finds the object in its
    // first argument, which is where a call with a dot leaves it out.
    private static void EmitMethods(SourceWriter w, ScriptTypeModel self, ScriptFunctionModel[] functions)
    {
        w.Line("/// <summary>The functions a script gets when it reads a method and does not call it.</summary>");
        w.Line($"internal static {ScriptNames.MethodTable} ScriptMethods({ScriptNames.Context} context) =>");
        w.Indent();
        w.Line($"new {ScriptNames.ApiTable}(context.Host)");
        w.Indent();
        foreach (ScriptFunctionModel function in functions)
        {
            w.Line(
                $".Function({ScriptNames.Utf8(function.LuauName)}, stack => {ScriptMarshalling.ReadSelf(self)} " +
                $"? {ScriptFunctionEmitter.CallOnSelf(function)} " +
                $": {ScriptNames.Arguments}.NoSelf(stack, {ScriptNames.Literal(function.LuauName)}))");
        }

        w.Line(".KeepAsMethods();");
        w.Outdent().Outdent();
        w.Line();
    }

    // The body of a property a script can set: its read comes from the door
    // its write goes through.
    private static void EmitDoorRead(SourceWriter w, ScriptPropertyModel property, ScriptTypeModel self)
    {
        string held = property.Type.Kind switch
        {
            ScriptValueKind.Text => "Text",
            ScriptValueKind.Vector3 => "Vector",
            ScriptValueKind.Boolean => "Boolean",
            ScriptValueKind.Number => "Number",
            ScriptValueKind.Color3 => "Color",
            _ => "Frame",
        };

        // The door holds a number as a double. The property may be a float.
        string cast = property.Type.Kind == ScriptValueKind.Number ? $"({property.Type.ClrType})" : "";
        w.Line($"{property.Access} static partial {property.Type.ClrType} {property.MemberName}({self.ClrType} {property.SelfName}) =>");
        w.Indent();
        w.Line(
            $"{cast}{ScriptNames.Core}.ScriptWrites.Read({property.SelfName}, " +
            $"{ScriptNames.Core}.SettableMemberId.{property.Sets}).{held};");
        w.Outdent();
        w.Line();
    }

    private static void EmitOverrides(SourceWriter w, ScriptContainerModel model)
    {
        w.Line("/// <inheritdoc/>");
        w.Line($"public override string ClassName => {ScriptNames.Literal(model.LuauName)};");
        w.Line();
        w.Line($"internal override int ReadMember({Signature}, int atom) => ScriptRead(context, stack, this, atom);");
        w.Line();
        w.Line($"internal override int WriteMember({Signature}, int atom) => ScriptWrite(context, stack, this, atom);");
        w.Line();
        w.Line($"internal override int InvokeMember({Signature}, int atom) => ScriptInvoke(context, stack, this, atom);");
        w.Line();
    }

    private static void EmitCases(SourceWriter w, List<string> names, string body)
    {
        if (names.Count == 0)
            return;

        foreach (string name in names)
            w.Line($"case {Atom(name)}:");

        w.Indent().Line(body).Outdent();
    }

    // A name the class does not declare is one of the class it extends, or
    // of none. `access` is Read, Write or Invoke.
    private static void EmitDefault(SourceWriter w, ScriptContainerModel model, string access)
    {
        string answer = model.Extends.Length > 0
            ? $"{ScriptNames.Bases}.{ScriptNames.BaseCall(access, model.FullTypeName)}(context, stack, self, atom)"
            : $"{ScriptNames.Member}.None";

        w.Line("default:");
        w.Indent().Line($"return {answer};").Outdent();
    }

    private static string Atom(string name) => $"{ScriptNames.Atoms}.{ScriptNames.Identifier(name)}";
}
