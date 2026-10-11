using System.Reflection;
using System.Text.RegularExpressions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a script can set on a node is what the door lists, and nothing else.
/// A member the binding wrote some other way would have no first value in
/// the play journal, and Stop would leave the write in the level.
/// </summary>
public sealed class SettableListTests
{
    // What a Roblox script would try first.
    private static readonly string[] RobloxNames =
    [
        "Anchored", "Archivable", "Brightness", "CanCollide", "CanQuery", "CanTouch", "ClassName", "Color",
        "Enabled", "Locked", "Material", "Orientation", "Parent", "Range", "Rotation", "Shape", "Size",
        "Transparency",
    ];

    // The engine's own types a member of a node is read from.
    private static readonly Type[] NodeTypes =
        [typeof(SceneNode), typeof(Transform), typeof(Light), typeof(EntityData), typeof(Script)];

    // A name the binding compares a member's name with.
    private static readonly Regex NameLiteral = new(@"""(?<name>[A-Za-z_]\w*)""u8", RegexOptions.CultureInvariant);

    [Fact]
    public void A_script_can_set_what_the_door_lists_and_nothing_else()
    {
        using var level = new ScriptLevel();
        OneOfEverything.AddTo(level);
        string probe = Probe(Candidates());
        foreach (SceneNode node in level.Scene.Root.Traverse().ToArray())
            level.AddScript(node, probe, "Probe");

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        string[] set = [.. level.Printed.Distinct().Order(StringComparer.Ordinal)];
        set.ShouldBe(
            [.. ScriptWrites.Settable.Select(member => member.Name).Order(StringComparer.Ordinal)],
            "a script set a member that ScriptWrites does not list, or could not set one it lists. " +
            "The binding sets a node only through ScriptWrites.TryWrite, which records what Stop puts back");
    }

    [Fact]
    public void The_names_tried_include_every_member_the_binding_and_a_node_have()
    {
        SortedSet<string> names = Candidates();

        names.ShouldContain("ClassName", "the binding's sources were not found, or name no member the old way");
        names.ShouldContain("LocalRotation", "the properties of SceneNode were not found");
        names.ShouldContain("Intensity", "the properties of Light were not found");
    }

    // Every name a script might set: what the door lists, what the binding's
    // sources name, what a node and its payloads hold, and Roblox's own.
    private static SortedSet<string> Candidates()
    {
        var names = new SortedSet<string>(RobloxNames, StringComparer.Ordinal);
        names.UnionWith(ScriptWrites.Settable.Select(member => member.Name));

        foreach (Type type in NodeTypes)
        {
            const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance;
            names.UnionWith(type.GetProperties(Members).Select(property => property.Name));
            names.UnionWith(type.GetFields(Members).Select(field => field.Name));
        }

        string binding = Path.Combine(BindingSourceTests.RepositoryRoot(), BindingSourceTests.Binding);
        foreach (string file in BindingSourceTests.Sources(binding))
            names.UnionWith(NameLiteral.Matches(File.ReadAllText(file)).Select(match => match.Groups["name"].Value));

        return names;
    }

    // Tries every name with a value of every kind, on the script's own node
    // and the one above it, and prints each name that took one.
    private static string Probe(IEnumerable<string> names) => $$"""
        local names = { {{string.Join(", ", names.Select(name => $"\"{name}\""))}} }
        local values = { 1, "text", true, Vector3.new(1, 2, 3), CFrame.new(1, 2, 3), workspace, {}, print }
        for _, target in { script, script.Parent } do
            for _, name in names do
                local took = pcall(function() target[name] = nil end)
                for _, value in values do
                    if pcall(function() target[name] = value end) then took = true end
                end
                if took then print(name) end
            end
        end
        """;
}
