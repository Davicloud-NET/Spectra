using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Text;

namespace SpectraEngine.Core.Entities;

// What ent_show prints for one entity. Every row starts with the entity's
// name, so a grep for the name and a word finds one kind of row.
internal sealed class EntityDescription
{
    private readonly IConsoleOutput _output;
    private readonly Scene.Scene _scene;
    private readonly EntityWorld? _world;

    private readonly List<WireRow> _wires = [];
    private readonly List<string> _listedOutputs = [];
    private readonly List<Entity> _resolved = [];
    private readonly List<KeyValuePair<string, string>> _state = [];

    // world is null when the level is not running.
    public EntityDescription(IConsoleOutput output, Scene.Scene scene, EntityWorld? world)
    {
        _output = output;
        _scene = scene;
        _world = world;
    }

    public void Write(in ListedEntity entity)
    {
        string name = EntityConsoleText.Name(entity.Name);
        EntitySchemaCatalog? schemas = _scene.EntitySchemas;
        EntitySchema? schema = null;
        schemas?.TryGetSchema(entity.ClassName, out schema);

        string className = entity.ClassName.Length == 0
            ? "(no class)"
            : entity.IsUnknownClass(schemas) ? $"{entity.ClassName} (class not in this build)" : entity.ClassName;
        _output.Print($"{name}: {className}, node {KeyvalueWire.Format(entity.NodeId)}");

        WriteKeyvalues(name, entity.Data, schema);

        if (entity.Live is { } live)
            WriteLiveState(name, live);
        else
            _output.Print($"{name} not running: no live state");

        WriteOutputs(name, entity, schema);
        WriteInputs(name, schema);
    }

    // What the class declares, in its order, with the default where the map
    // says nothing. Then what the map says that the class does not declare.
    private void WriteKeyvalues(string name, EntityData data, EntitySchema? schema)
    {
        if (schema is not null)
        {
            foreach (KeyvalueDescriptor declared in schema.Keyvalues)
            {
                _output.Print(data.TryGetValue(declared.Name, out string authored)
                    ? $"{name} keyvalue {declared.Name} = {Value(authored)}"
                    : $"{name} keyvalue {declared.Name} = {Value(declared.Default)} (default)");
            }
        }

        foreach (KeyValuePair<string, string> pair in data.Keyvalues)
        {
            if (schema is null)
                _output.Print($"{name} keyvalue {pair.Key} = {Value(pair.Value)}");
            else if (!Declares(schema, pair.Key))
                _output.Print($"{name} keyvalue {pair.Key} = {Value(pair.Value)} (not a {schema.ClassName} keyvalue)");
        }
    }

    private void WriteLiveState(string name, Entity live)
    {
        _state.Clear();
        live.DescribeState(new EntityStateWriter(_state));
        foreach (KeyValuePair<string, string> row in _state)
            _output.Print($"{name} state {row.Key} = {Value(row.Value)}");

        float next = live.NextThinkTime;
        _output.Print(float.IsFinite(next)
            ? $"{name} think: in {EntityConsoleText.Seconds(MathF.Max(next - live.World.Time, 0f))}s"
            : $"{name} think: none");
    }

    private void WriteOutputs(string name, in ListedEntity entity, EntitySchema? schema)
    {
        CollectWires(entity);
        _listedOutputs.Clear();

        if (schema is not null)
        {
            foreach (string declared in schema.Outputs)
            {
                _listedOutputs.Add(declared);
                if (!WriteWiresOf(name, declared, note: ""))
                    _output.Print($"{name} output {EntityConsoleText.Name(declared)}: not wired");
            }
        }

        // A wire on an output the class does not declare never fires. Only
        // said of a class that declares some, since a schema can be incomplete.
        for (int i = 0; i < _wires.Count; i++)
        {
            string output = _wires[i].Connection.Output;
            if (_listedOutputs.Contains(output))
                continue;

            _listedOutputs.Add(output);
            string note = schema is { Outputs.Count: > 0 }
                ? $", {schema.ClassName} has no output {EntityConsoleText.Name(output)}"
                : "";
            WriteWiresOf(name, output, note);
        }
    }

    private void WriteInputs(string name, EntitySchema? schema)
    {
        if (schema is null)
            _output.Print($"{name} inputs: not known");
        else if (schema.Inputs.Count == 0)
            _output.Print($"{name} inputs: none");
        else
            _output.Print($"{name} inputs: {string.Join(' ', schema.Inputs)}");
    }

    // The runtime copy while the level runs, since it counts the fires left.
    private void CollectWires(in ListedEntity entity)
    {
        _wires.Clear();

        if (entity.Live is not { } live)
        {
            foreach (EntityConnection wire in entity.Data.Connections)
                _wires.Add(new WireRow(wire, null));
            return;
        }

        foreach (EntityOutput output in live.Outputs)
        {
            for (int i = 0; i < output.WireCount; i++)
                _wires.Add(new WireRow(output.ConnectionAt(i), output.FiresLeftAt(i)));
        }
    }

    private bool WriteWiresOf(string name, string output, string note)
    {
        bool any = false;
        foreach (WireRow wire in _wires)
        {
            if (!string.Equals(wire.Connection.Output, output, StringComparison.Ordinal))
                continue;

            any = true;
            _output.Print(WireText(name, wire) + note);
        }

        return any;
    }

    private string WireText(string name, in WireRow row)
    {
        EntityConnection wire = row.Connection;
        var text = new StringBuilder(name).Append(" output ");
        EntityConsoleText.AppendName(text, wire.Output).Append(" -> ");
        EntityConsoleText.AppendName(text, wire.TargetName).Append('.');
        EntityConsoleText.AppendCall(text, wire.Input, wire.Parameter);

        if (wire.Delay > 0f)
            text.Append(" in=").Append(EntityConsoleText.Seconds(wire.Delay)).Append('s');

        string total = KeyvalueWire.Format(wire.TimesToFire);
        text.Append(" fires=");
        if (wire.FiresForever)
            text.Append("unlimited");
        else if (row.FiresLeft is { } left)
            text.Append(KeyvalueWire.Format(left)).Append(" of ").Append(total).Append(" left");
        else
            text.Append(total);

        return text.Append(", ").Append(Reach(wire.TargetName)).ToString();
    }

    // Who the wire would reach if it fired now.
    private string Reach(string target)
    {
        if (target == TargetNameIndex.SelfToken)
            return "this entity";
        if (TargetNamePattern.IsRuntimeToken(target))
            return "decided when it fires";

        int count = CountNamed(target);
        return count == 0
            ? EntityConsoleText.NothingAnswers(target)
            : EntityConsoleText.Reach(count, target);
    }

    private int CountNamed(string target)
    {
        if (_world?.Index is { } index)
        {
            _resolved.Clear();
            index.Resolve(target, null, null, null, _resolved);
            return _resolved.Count;
        }

        int count = 0;
        foreach (SceneNode node in _scene.Root.Traverse())
        {
            if (node.Entity is not null && TargetNamePattern.Matches(target, node.Name))
                count++;
        }

        return count;
    }

    private static bool Declares(EntitySchema schema, string key)
    {
        foreach (KeyvalueDescriptor declared in schema.Keyvalues)
        {
            if (string.Equals(declared.Name, key, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string Value(string text) => text.Length == 0 ? "\"\"" : text;

    // FiresLeft is null when the level is not running.
    private readonly record struct WireRow(EntityConnection Connection, int? FiresLeft);
}
