using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Shell.Logic;

// Turns a wiring snapshot into a LogicGraph.
internal sealed class LogicGraphBuilder
{
    private readonly LogicGraphInfo _info;
    private readonly EntitySchemaCatalog? _catalog;
    private readonly IReadOnlyList<LogicEntityInfo> _entities;
    private readonly Dictionary<string, List<int>> _byName = new(StringComparer.Ordinal);

    // The port names wires use, per entity. Null until a wire touches the entity.
    private readonly List<string>?[] _sent;
    private readonly List<string>?[] _received;

    private readonly List<Hop> _hops = [];
    private readonly List<Stub> _stubs = [];
    private readonly Dictionary<(LogicStubKind Kind, string Name), int> _stubIndex = [];

    private readonly List<LogicCard> _cards = [];
    private readonly LogicCard?[] _cardOfEntity;
    private LogicCard[] _cardOfStub = [];
    private int _unwired;

    // One wire reaching one receiver. Receiver is an entity index, or -1 when
    // Stub names the stub instead.
    private readonly record struct Hop(int Sender, int WireIndex, int Receiver, int Stub);

    private sealed record Stub(LogicStubKind Kind, string Name, List<string> Inputs);

    public LogicGraphBuilder(LogicGraphInfo info, EntitySchemaCatalog? catalog)
    {
        _info = info;
        _catalog = catalog;
        _entities = info.Entities;
        _sent = new List<string>?[_entities.Count];
        _received = new List<string>?[_entities.Count];
        _cardOfEntity = new LogicCard?[_entities.Count];
    }

    public LogicGraph Build()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            if (!_byName.TryGetValue(_entities[i].Name, out List<int>? named))
                _byName.Add(_entities[i].Name, named = []);

            named.Add(i);
        }

        int wireCount = 0;
        for (int sender = 0; sender < _entities.Count; sender++)
        {
            wireCount += _entities[sender].Wires.Count;
            for (int wire = 0; wire < _entities[sender].Wires.Count; wire++)
                Resolve(sender, wire);
        }

        CreateCards();
        List<LogicEdge> edges = CreateEdges();
        HashSet<LogicWireKey> goingNowhere = GoingNowhere(edges, out LogicEdge? first);

        return new LogicGraph(_cards, edges, goingNowhere)
        {
            WireCount = wireCount,
            UnwiredCount = _unwired,
            FirstGoingNowhere = first,
            IsTruncated = _info.IsTruncated,
        };
    }

    private void Resolve(int sender, int wireIndex)
    {
        EntityConnection wire = _entities[sender].Wires[wireIndex];
        string target = wire.TargetName ?? "";
        string input = wire.Input ?? "";
        Mark(ref _sent[sender], wire.Output ?? "");

        List<int> receivers = Receivers(sender, target);
        foreach (int receiver in receivers)
        {
            Mark(ref _received[receiver], input);
            _hops.Add(new Hop(sender, wireIndex, receiver, -1));
        }

        if (receivers.Count > 0)
            return;

        int stub = StubFor(target);
        if (!_stubs[stub].Inputs.Contains(input))
            _stubs[stub].Inputs.Add(input);

        _hops.Add(new Hop(sender, wireIndex, -1, stub));
    }

    private List<int> Receivers(int sender, string target)
    {
        if (target is TargetNameIndex.SelfToken or TargetNameIndex.CallerToken)
            return [sender];

        if (TargetNamePattern.IsPrefix(target))
        {
            var matched = new List<int>();
            for (int i = 0; i < _entities.Count; i++)
            {
                if (TargetNamePattern.Matches(target, _entities[i].Name))
                    matched.Add(i);
            }

            return matched;
        }

        // A name that starts with ! matches nothing, even when an entity has it.
        if (target.Length > 0 && target[0] != '!' && _byName.TryGetValue(target, out List<int>? named))
            return named;

        return [];
    }

    private int StubFor(string target)
    {
        (LogicStubKind Kind, string Name) key = target switch
        {
            TargetNameIndex.ActivatorToken => (LogicStubKind.Activator, LogicText.ActivatorName),
            "" => (LogicStubKind.NoTarget, ""),
            _ when TargetNamePattern.IsPrefix(target) => (LogicStubKind.MissingPrefix, target),
            _ => (LogicStubKind.MissingName, target),
        };

        if (_stubIndex.TryGetValue(key, out int index))
            return index;

        _stubIndex.Add(key, _stubs.Count);
        _stubs.Add(new Stub(key.Kind, key.Name, []));
        return _stubs.Count - 1;
    }

    private static void Mark(ref List<string>? names, string name)
    {
        names ??= [];
        if (!names.Contains(name))
            names.Add(name);
    }

    private void CreateCards()
    {
        CreateEntityCards(isWired: true);

        // By kind and name, so the cards do not depend on which wire met a stub first.
        IEnumerable<int> ordered = Enumerable.Range(0, _stubs.Count)
            .OrderBy(i => _stubs[i].Kind)
            .ThenBy(i => _stubs[i].Name, StringComparer.Ordinal);

        _cardOfStub = new LogicCard[_stubs.Count];
        foreach (int i in ordered)
        {
            Stub stub = _stubs[i];
            _cardOfStub[i] = new LogicCard(
                _cards.Count, stub.Kind, stub.Name, Ports(null, stub.Inputs, isOutput: false));
            _cards.Add(_cardOfStub[i]);
        }

        int wired = _cards.Count;
        CreateEntityCards(isWired: false);
        _unwired = _cards.Count - wired;
    }

    // The entities a wire touches, or the ones none does, in scene order.
    private void CreateEntityCards(bool isWired)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            if ((_sent[i] is null && _received[i] is null) == isWired)
                continue;

            EntitySchema? schema = null;
            _catalog?.TryGetSchema(_entities[i].ClassName, out schema);

            var card = new LogicCard(
                _cards.Count,
                _entities[i],
                schema,
                Ports(schema?.Inputs, _received[i], isOutput: false),
                Ports(schema?.Outputs, _sent[i], isOutput: true));

            _cardOfEntity[i] = card;
            _cards.Add(card);
        }
    }

    private static LogicPort[] Ports(IReadOnlyList<string>? declared, List<string>? wired, bool isOutput)
    {
        var ports = new List<LogicPort>();
        foreach (string name in declared ?? [])
            ports.Add(new LogicPort(name, isOutput, true, wired is not null && wired.Contains(name)));

        if (wired is null)
            return [.. ports];

        // By name, since the order wires arrive in depends on the scene's order.
        IEnumerable<string> undeclared = wired
            .Where(name => declared is null || !declared.Contains(name))
            .Order(StringComparer.Ordinal);

        foreach (string name in undeclared)
            ports.Add(new LogicPort(name, isOutput, false, true));

        return [.. ports];
    }

    private List<LogicEdge> CreateEdges()
    {
        var shared = new Dictionary<(int From, int To, string Output, string Target, string Input), int>();
        var drafts = new List<(Hop First, LogicCard To, List<int> Wires)>();

        foreach (Hop hop in _hops)
        {
            EntityConnection wire = _entities[hop.Sender].Wires[hop.WireIndex];
            LogicCard to = hop.Stub >= 0 ? _cardOfStub[hop.Stub] : CardOf(hop.Receiver);
            var key = (hop.Sender, to.Index, wire.Output ?? "", wire.TargetName ?? "", wire.Input ?? "");

            if (shared.TryGetValue(key, out int draft))
            {
                drafts[draft].Wires.Add(hop.WireIndex);
                continue;
            }

            shared.Add(key, drafts.Count);
            drafts.Add((hop, to, [hop.WireIndex]));
        }

        var edges = new List<LogicEdge>(drafts.Count);
        foreach ((Hop first, LogicCard to, List<int> wires) in drafts)
        {
            LogicCard from = CardOf(first.Sender);
            EntityConnection wire = _entities[first.Sender].Wires[first.WireIndex];
            edges.Add(new LogicEdge(from, to, wire, wires, Judge(from, to, wire)));
        }

        return edges;
    }

    private LogicCard CardOf(int entity) =>
        _cardOfEntity[entity] ?? throw new InvalidOperationException("An entity has no card.");

    private static LogicVerdict Judge(LogicCard from, LogicCard to, EntityConnection wire)
    {
        if (to.Stub is LogicStubKind.MissingName or LogicStubKind.MissingPrefix or LogicStubKind.NoTarget)
            return LogicVerdict.TargetMissing;

        if (to.IsKnownClass && !to.Declares(wire.Input ?? "", isOutput: false))
            return LogicVerdict.NoSuchInput;

        if (from.IsKnownClass && !from.Declares(wire.Output ?? "", isOutput: true))
            return LogicVerdict.NoSuchOutput;

        return LogicVerdict.Fine;
    }

    // A wire goes nowhere when none of the entities it reaches can take it.
    private HashSet<LogicWireKey> GoingNowhere(List<LogicEdge> edges, out LogicEdge? first)
    {
        var delivered = new HashSet<LogicWireKey>();
        foreach (LogicEdge edge in edges)
        {
            if (edge.GoesNowhere)
                continue;

            foreach (int wire in edge.WireIndices)
                delivered.Add(new LogicWireKey(edge.From.NodeId, wire));
        }

        var goingNowhere = new HashSet<LogicWireKey>();
        LogicWireKey? firstKey = null;

        foreach (LogicEntityInfo entity in _entities)
        {
            for (int wire = 0; wire < entity.Wires.Count; wire++)
            {
                var key = new LogicWireKey(entity.NodeId, wire);
                if (!delivered.Contains(key) && goingNowhere.Add(key))
                    firstKey ??= key;
            }
        }

        first = firstKey is LogicWireKey found
            ? edges.FirstOrDefault(edge =>
                edge.From.NodeId == found.NodeId && edge.WireIndices.Contains(found.WireIndex))
            : null;

        return goingNowhere;
    }
}
