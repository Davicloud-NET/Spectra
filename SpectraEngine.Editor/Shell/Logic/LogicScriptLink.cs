using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// One script hears one output of one entity, or sends it one input, from
// however many lines of code.
internal sealed class LogicScriptLink(int script, Guid entity, string port, bool isSend)
{
    // The script's place among the level's scripts.
    public int Script { get; } = script;

    public Guid Entity { get; } = entity;

    public string Port { get; } = port;

    public bool IsSend { get; } = isSend;

    // The engine's wires it joins, by their place in the engine's list.
    public List<int> Wires { get; } = [];
}
