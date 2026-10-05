namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>One input or output of a card.</summary>
/// <param name="Name">The name a wire spells it by.</param>
/// <param name="IsOutput">Whether it sends. False for an input.</param>
/// <param name="IsDeclared">Whether the card's class declares it. False when only a wire names it.</param>
/// <param name="IsWired">Whether a wire leaves it or arrives at it.</param>
public readonly record struct LogicPort(string Name, bool IsOutput, bool IsDeclared, bool IsWired);
