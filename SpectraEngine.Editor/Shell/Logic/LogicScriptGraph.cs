using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Shell.Logic;

// What a level's scripts add to a wiring graph while it is built: the ports
// they reach on entities, a card for each script that reaches one, and an
// edge for each link.
internal sealed class LogicScriptGraph
{
    private readonly LogicScriptLinks _links;
    private readonly IReadOnlyList<LogicEntityInfo> _entities;
    private readonly EntitySchemaCatalog? _catalog;
    private readonly Dictionary<Guid, int> _entityByNode = [];
    private LogicCard?[] _cardOfScript = [];

    public LogicScriptGraph(LogicGraphInfo info, EntitySchemaCatalog? catalog, LogicScriptWiring? running)
    {
        _links = new LogicScriptLinks(info, running);
        _entities = info.Entities;
        _catalog = catalog;
        HasMoreWires = running?.IsTruncated ?? false;

        for (int i = 0; i < _entities.Count; i++)
            _entityByNode.TryAdd(_entities[i].NodeId, i);

        _links.Links.RemoveAll(link => !CanDraw(link));
    }

    // How many scripts run when the level plays.
    public int ScriptCount => _links.Scripts.Count;

    // How many of them have a card.
    public int CardCount { get; private set; }

    // Whether the scripts made more wires than the engine listed.
    public bool HasMoreWires { get; }

    // The names of the scripts inside an entity, in scene order.
    public IReadOnlyList<string> Inside(Guid entity) => _links.Inside(entity);

    // What a script hears or sends counts as a wire of the entity: the port
    // gets a row for the edge to meet, and the card is on show. The lists
    // are the builder's, one for each entity.
    public void MarkPorts(List<string>?[] sent, List<string>?[] received)
    {
        foreach (LogicScriptLink link in _links.Links)
        {
            int entity = _entityByNode[link.Entity];
            if (link.IsSend)
                LogicGraphBuilder.Mark(ref received[entity], link.Port);
            else
                LogicGraphBuilder.Mark(ref sent[entity], link.Port);
        }
    }

    // A card for each script that hears or sends something, in scene order.
    public void AddCards(List<LogicCard> cards)
    {
        _cardOfScript = new LogicCard?[_links.Scripts.Count];
        var linked = new bool[_cardOfScript.Length];
        foreach (LogicScriptLink link in _links.Links)
            linked[link.Script] = true;

        for (int i = 0; i < linked.Length; i++)
        {
            if (!linked[i])
                continue;

            var card = new LogicCard(cards.Count, _links.Scripts[i]);
            _cardOfScript[i] = card;
            cards.Add(card);
            CardCount++;
        }
    }

    // cardOfEntity finds an entity's card by its place in the level's list.
    public void AddEdges(List<LogicEdge> edges, Func<int, LogicCard> cardOfEntity)
    {
        foreach (LogicScriptLink link in _links.Links)
        {
            if (_cardOfScript[link.Script] is not { } script)
                continue;

            LogicCard entity = cardOfEntity(_entityByNode[link.Entity]);
            edges.Add(new LogicEdge(
                link.IsSend ? script : entity,
                link.IsSend ? entity : script,
                link.Port,
                link.Wires,
                _links.LabelOf(link)));
        }
    }

    // The graph draws inputs and outputs of entities. A script also listens
    // to what is neither, such as a touch, and to nodes that are no entity.
    private bool CanDraw(LogicScriptLink link)
    {
        if (!_entityByNode.TryGetValue(link.Entity, out int entity))
            return false;

        EntitySchema? schema = null;
        if (_catalog?.TryGetSchema(_entities[entity].ClassName, out schema) != true || schema is null)
            return true;

        IReadOnlyList<string> declared = link.IsSend ? schema.InputNames : schema.OutputNames;
        return declared.Contains(link.Port);
    }
}
