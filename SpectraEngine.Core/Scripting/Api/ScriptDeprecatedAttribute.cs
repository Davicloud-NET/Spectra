using System;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Marks an old spelling that still works, such as <c>wait</c>, and says
/// what to write in its place.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
public sealed class ScriptDeprecatedAttribute : Attribute
{
    /// <summary>Marks the member as deprecated.</summary>
    /// <param name="use">What to write in its place: <c>task.wait</c>.</param>
    public ScriptDeprecatedAttribute(string use) => Use = use;

    /// <summary>What to write in its place.</summary>
    public string Use { get; }
}
