using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

// A catalogue per test: EntityCatalog.Shared freezes on its first read.
internal static class EntityRuntime
{
    public static EntityCatalog Catalog(List<string> log)
    {
        var catalog = new EntityCatalog();
        catalog.Add(FuncButton.SpectraSchema, static () => new FuncButton());
        catalog.Add(FuncDoor.SpectraSchema, static () => new FuncDoor());
        catalog.Add(FuncMoveLinear.SpectraSchema, static () => new FuncMoveLinear());
        catalog.Add(InfoPlayerStart.SpectraSchema, static () => new InfoPlayerStart());
        catalog.Add(LogicAuto.SpectraSchema, static () => new LogicAuto());
        catalog.Add(LogicBranch.SpectraSchema, static () => new LogicBranch());
        catalog.Add(LogicCase.SpectraSchema, static () => new LogicCase());
        catalog.Add(LogicCompare.SpectraSchema, static () => new LogicCompare());
        catalog.Add(LogicRelay.SpectraSchema, static () => new LogicRelay());
        catalog.Add(LogicTimer.SpectraSchema, static () => new LogicTimer());
        catalog.Add(MathCounter.SpectraSchema, static () => new MathCounter());
        catalog.Add(InfoTeleportDestination.SpectraSchema, static () => new InfoTeleportDestination());
        catalog.Add(TriggerMultiple.SpectraSchema, static () => new TriggerMultiple());
        catalog.Add(TriggerOnce.SpectraSchema, static () => new TriggerOnce());
        catalog.Add(TriggerTeleport.SpectraSchema, static () => new TriggerTeleport());
        catalog.Add(new EntitySchema("test_recorder"), () => new RecordingEntity(log));
        return catalog;
    }

    public static SceneNode Place(SceneNode parent, string name, string className)
    {
        SceneNode node = parent.CreateChild(name);
        node.Entity = new EntityData(className);
        return node;
    }

    public static void Wire(
        SceneNode node,
        string output,
        string targetName,
        string input,
        string parameter = "",
        float delay = 0f) =>
        node.Entity!.Connections.Add(
            new EntityConnection(output, targetName, input, parameter, delay, EntityConnection.Infinite));

    public static T Live<T>(EntityWorld world, SceneNode node)
        where T : Entity
    {
        world.Index.ShouldNotBeNull();
        world.Index!.TryGetByNodeId(node.Id, out Entity? entity).ShouldBeTrue();
        return entity.ShouldBeOfType<T>();
    }

    // Same call EntityWorld makes to deliver an input.
    public static bool Send(Entity entity, string input, string parameter = "", Entity? activator = null)
    {
        var context = new EntityInputContext(activator, null, parameter);
        return entity.AcceptInput(input, ref context);
    }
}

// Logs one line per input received. Hand-written, so a failure cannot be
// the generator's.
internal sealed class RecordingEntity : Entity
{
    private readonly List<string> _log;

    public RecordingEntity(List<string> log) => _log = log;

    public override bool AcceptInput(string input, ref EntityInputContext context)
    {
        _log.Add($"{TargetName}:{input}:{context.Parameter}");
        return true;
    }
}

internal sealed class CapturingLogger : ILogger
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<string> MessagesAt(LogLevel level) =>
        _entries.Where(entry => entry.Level == level).Select(entry => entry.Message).ToArray();

    public string Describe() => _entries.Count == 0
        ? "(no log entries)"
        : string.Join(Environment.NewLine, _entries.Select(entry => $"[{entry.Level}] {entry.Message}"));

    public bool IsEnabled(LogLevel logLevel) => true;

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _entries.Add((logLevel, formatter(state, exception)));
}
