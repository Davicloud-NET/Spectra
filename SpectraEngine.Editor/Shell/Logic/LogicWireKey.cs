using System;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>Names one wire: the entity that sends it and its place in that entity's wire list.</summary>
public readonly record struct LogicWireKey(Guid NodeId, int WireIndex);
