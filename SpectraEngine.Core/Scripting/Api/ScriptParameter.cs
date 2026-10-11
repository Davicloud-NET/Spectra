namespace SpectraEngine.Core.Scripting.Api;

/// <summary>One parameter of a function, or one value a signal hands a listener.</summary>
/// <param name="Name">Its name. <c>...</c> stands for any number of further values.</param>
/// <param name="Type">Its type, as Luau writes it. One that may be left out ends in <c>?</c>.</param>
public sealed record ScriptParameter(string Name, string Type);
