using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// No built-in entity writes a node's transform, flags, brush or kind itself.
/// </summary>
// Such a write is not journaled, so stopping the level leaves it in the map.
// A node is moved through Entity.MoveNode or EntityWorld.SetLocalTransform.
// Reads the sources as text.
public sealed class EntitySourceRuleTests
{
    private static readonly string[] NodeState =
    [
        "LocalTransform", "LocalPosition", "LocalRotation", "LocalScale",
        "PhysicsFlags", "CanCollide", "CanQuery", "CanTouch", "Anchored", "CollisionGroup",
        "IsRendered", "Brush", "BrushKind",
    ];

    // One of the names, then an assignment of any kind, ++ or --. Not == or =>.
    private static readonly Regex DirectWrite = new(
        $@"\b(?:{string.Join('|', NodeState)})\b\s*(?:(?:\?\?|<<|>>|[-+*/%&|^])?=(?![=>])|\+\+|--)",
        RegexOptions.CultureInvariant);

    private static readonly Regex LineComment = new(@"//[^\r\n]*", RegexOptions.CultureInvariant);

    [Fact]
    public void No_entity_source_writes_node_state_directly()
    {
        string folder = Path.Combine(SourceRoot(), "SpectraEngine.Entities");
        string[] files = [.. Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Where(IsSource)];
        files.ShouldNotBeEmpty();

        var offenders = new List<string>();
        foreach (string file in files)
        {
            foreach (string write in Writes(File.ReadAllText(file)))
                offenders.Add($"{Path.GetFileName(file)}, {write}");
        }

        offenders.ShouldBeEmpty(
            "an entity moves its node through Entity.MoveNode or EntityWorld.SetLocalTransform, " +
            "which stopping the level undoes. A direct write to a node stays in the saved map");
    }

    [Theory]
    [InlineData("Node.LocalPosition = target;")]
    [InlineData("node.LocalTransform=pose;")]
    [InlineData("Node.LocalRotation = turn;")]
    [InlineData("Node.LocalScale *= 2f;")]
    [InlineData("node.PhysicsFlags |= PhysicsFlags.CanTouch;")]
    [InlineData("node.CanCollide = false;")]
    [InlineData("node.CanQuery = false;")]
    [InlineData("node.CanTouch = true;")]
    [InlineData("node.Anchored = false;")]
    [InlineData("node.CollisionGroup++;")]
    [InlineData("node.IsRendered = false;")]
    [InlineData("Node.Brush = brush.WithScaledExtents(size);")]
    [InlineData("node.Brush ??= fallback;")]
    [InlineData("Node.BrushKind = BrushKind.Part;")]
    [InlineData("var copy = new SceneNode { LocalRotation = turn };")]
    [InlineData("Node.LocalPosition\r\n    = target;")]
    [InlineData("Node.LocalPosition\n    = target;")]
    public void A_write_to_node_state_is_caught(string source)
    {
        Writes(source).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("Transform closed = _owner.Node.LocalTransform;")]
    [InlineData("return _owner.World.SetLocalTransform(_owner.Node, in pose);")]
    [InlineData("pose.Position = Vector3.Lerp(a, b, t);")]
    [InlineData("if (node.LocalScale == Vector3.One)")]
    [InlineData("if (node.BrushKind != BrushKind.Part)")]
    [InlineData("if (brushes[i].Brush is not { } brush)")]
    [InlineData("Brush? brush = node.Brush;")]
    [InlineData("bool solid = node.CanCollide;")]
    [InlineData("return node.CollisionGroup >= 0;")]
    [InlineData("public bool IsRendered => Node.IsRendered;")]
    [InlineData("[SpectraEntity(\"func_door\", Placement = EntityPlacement.Brush)]")]
    [InlineData("// Node.LocalPosition = target;")]
    public void A_read_or_a_comment_is_let_through(string source)
    {
        Writes(source).ShouldBeEmpty();
    }

    // Each write as "line N: text". Counts line feeds, so CRLF and LF sources
    // give the same numbers.
    private static List<string> Writes(string source)
    {
        // Comments go first. They keep their line breaks, so the numbers hold.
        string code = LineComment.Replace(source, "");

        var writes = new List<string>();
        foreach (Match match in DirectWrite.Matches(code))
        {
            int line = 1 + code.AsSpan(0, match.Index).Count('\n');
            writes.Add($"line {line}: {match.Value.ReplaceLineEndings(" ")}");
        }

        return writes;
    }

    private static bool IsSource(string path)
    {
        char separator = Path.DirectorySeparatorChar;
        return !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not find the solution root above the test binary");
    }
}
