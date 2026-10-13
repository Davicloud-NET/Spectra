using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// What scripts add to a wiring graph. While editing that is the names of the
// scripts inside each entity. While the level runs it is also what each
// script hears and sends: the engine lists one wire for each line of code,
// and the ones that agree in script, entity and port are one link here.
internal sealed class LogicScriptLinks
{
    private readonly Dictionary<Guid, List<string>> _inside = [];
    private readonly LogicScriptWiring? _running;

    public LogicScriptLinks(LogicGraphInfo info, LogicScriptWiring? running)
    {
        Scripts = info.Scripts;
        _running = running;

        var byNode = new Dictionary<Guid, int>(Scripts.Count);
        for (int i = 0; i < Scripts.Count; i++)
        {
            byNode.TryAdd(Scripts[i].NodeId, i);
            if (Scripts[i].EntityId == Guid.Empty)
                continue;

            if (!_inside.TryGetValue(Scripts[i].EntityId, out List<string>? names))
                _inside.Add(Scripts[i].EntityId, names = []);

            names.Add(Scripts[i].Name);
        }

        Join(byNode);
    }

    // The scripts that run when the level plays, in scene order.
    public IReadOnlyList<LogicScriptInfo> Scripts { get; }

    // In the order the engine lists their first wire.
    public List<LogicScriptLink> Links { get; } = [];

    // The names of the scripts inside an entity, in scene order.
    public IReadOnlyList<string> Inside(Guid entity) =>
        _inside.TryGetValue(entity, out List<string>? names) ? names : [];

    // What a link's edge says: the line of code that made it, or how many did.
    public LogicLabel LabelOf(LogicScriptLink link)
    {
        if (_running is null || link.Wires.Count == 0)
            return LogicLabel.None;

        if (link.Wires.Count > 1)
            return LogicLabel.Places(link.Wires.Count);

        LogicScriptWire wire = _running.Wires[link.Wires[0]];
        return LogicLabel.Place(wire.File, wire.Line);
    }

    // A wire of a script the level does not list has no card to end at.
    private void Join(Dictionary<Guid, int> byNode)
    {
        if (_running is null)
            return;

        var joined = new Dictionary<(int Script, Guid Entity, string Port, bool IsSend), LogicScriptLink>();
        for (int i = 0; i < _running.Wires.Count; i++)
        {
            LogicScriptWire wire = _running.Wires[i];
            if (!byNode.TryGetValue(wire.Script, out int script))
                continue;

            var key = (script, wire.Entity, wire.Port ?? "", wire.IsSend);
            if (!joined.TryGetValue(key, out LogicScriptLink? link))
            {
                link = new LogicScriptLink(script, wire.Entity, wire.Port ?? "", wire.IsSend);
                joined.Add(key, link);
                Links.Add(link);
            }

            link.Wires.Add(i);
        }
    }
}
