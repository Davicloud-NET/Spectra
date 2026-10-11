using System;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Declares globals. Each function and property of the class that carries a
/// script attribute is one, such as <c>wait</c> or <c>game</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ScriptGlobalsAttribute : Attribute
{
}
