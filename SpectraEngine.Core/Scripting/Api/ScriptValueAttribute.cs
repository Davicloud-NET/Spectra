using System;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Declares a type a script holds by value, such as <c>CFrame</c>. It goes
/// on a static class whose members each take the value as their first
/// parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ScriptValueAttribute : Attribute
{
    /// <summary>Declares the value type under the name a script knows it by.</summary>
    public ScriptValueAttribute(string name) => Name = name;

    /// <summary>What a script calls the type.</summary>
    public string Name { get; }
}
