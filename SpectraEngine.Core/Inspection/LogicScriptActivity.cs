namespace SpectraEngine.Core.Inspection;

/// <summary>What one <see cref="LogicScriptWire"/> has carried since a view began to watch.</summary>
/// <param name="Fired">How many times the input was sent, or the output fired for a listener.</param>
/// <param name="LastFiredTick">The tick it last did.</param>
public readonly record struct LogicScriptActivity(int Fired, long LastFiredTick);
