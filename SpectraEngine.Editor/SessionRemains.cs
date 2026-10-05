using SpectraEngine.Core.Maps;

namespace SpectraEngine.Editor;

/// <summary>What is taken from an engine session that died.</summary>
/// <param name="Level">The scene as it stood, with anything a run had moved put back.</param>
/// <param name="UndoDepth">How many edits its history could undo.</param>
/// <param name="RedoDepth">How many edits its history could redo.</param>
// The depths say whether edits landed after the last snapshot a shell saw.
public sealed record SessionRemains(MapDocument Level, int UndoDepth, int RedoDepth);
