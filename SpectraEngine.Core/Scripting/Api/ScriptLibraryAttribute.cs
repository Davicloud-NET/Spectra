using System;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Declares a global table of functions and constants, such as <c>task</c>,
/// or the <c>CFrame</c> a script writes <c>CFrame.new</c> on.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ScriptLibraryAttribute : Attribute
{
    /// <summary>Declares the library under the name of its global.</summary>
    public ScriptLibraryAttribute(string name) => Name = name;

    /// <summary>The name of the global.</summary>
    public string Name { get; }
}
