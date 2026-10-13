using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Stop puts a level back as it was. A write to every member a script can
/// set on every kind of node, then random ones, with nodes made, cloned,
/// moved and destroyed between them, and the level saves to the same bytes
/// after Stop as before Play. The seed is in every failure, and the same
/// seed makes the same run.
/// </summary>
public sealed class StopPromiseTests
{
    private const int WritesPerPlay = 320;
    private const int WritesPerTick = 16;
    private const int WritesPerTreeStep = 3;
    private const int WritesPerScript = 40;

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(2026)]
    [InlineData(31337)]
    public void Random_writes_through_the_door_are_all_put_back_by_Stop(int seed)
    {
        using var level = new ScriptLevel();
        OneOfEverything.AddTo(level);
        SceneNode[] nodes = [.. level.Scene.Root.Traverse()];
        MemberValue[][] atStart = [.. nodes.Select(MembersOf)];
        var before = new LevelState(level.Scene);
        var random = new RandomWrites(seed) { Nodes = nodes, Homes = HomesOf(level), TakesNodesOut = true };

        // Twice: the second play starts from what the first Stop left.
        for (int play = 1; play <= 2; play++)
        {
            string run = $"seed {seed}, play {play}";
            var coverage = new WriteCoverage(level.Scene);
            level.Play();
            var door = new ScriptWrites(level.Entities);
            var tree = new RandomTree(random, door.Tree, nodes);

            // One tick first: a script of the level reads the node above it
            // when it starts, and the writes give every node another parent.
            // The relay is in the level here, so its wire fires in every run.
            OneOfEverything.FireTheWire(level).ShouldBeTrue($"{run}: the relay was not there to fire its wire");
            level.Tick();
            var started = new LevelState(level.Scene);
            int taken = WriteToEveryMember(door, coverage, nodes, random);
            new LevelState(level.Scene).IsSavedAs(started).ShouldBeFalse($"{run}: the writes changed nothing a save holds");

            // The writes go to what the level was built with and to what was
            // made since, in the level or out of it, destroyed or not.
            for (int i = 0; i < WritesPerPlay; i++)
            {
                int node = random.Pick(nodes.Length);
                RandomWrite write = random.Next(nodes[node]);
                MemberValue value = write.PutsBack ? atStart[node][(int)write.Member.Id] : write.Value;
                if (coverage.Write(door, nodes[node], write.Member, in value))
                    taken++;

                MemberValue again = write.Value;
                door.TryWrite(tree.Any(), write.Member.Id, in again, out _);
                if (i % WritesPerTreeStep == WritesPerTreeStep - 1)
                    tree.Step();

                if (i % WritesPerTick == WritesPerTick - 1)
                {
                    OneOfEverything.FireTheWire(level);
                    level.Tick();
                }
            }

            ShouldBeAMix(taken, WritesPerPlay + (nodes.Length * ScriptWrites.Settable.Count), run);
            coverage.ShouldBeWhole(run);
            ShouldHaveChangedTheTree(tree, door, run);
            ShouldHaveOneWorldBrush(level, random.Homes[1], run);
            level.Errors.ShouldBeEmpty(run);

            level.Stop();

            new LevelState(level.Scene).ShouldBe(before, run);
            level.Scene.Root.Traverse().ShouldBe(nodes, run);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(2026)]
    [InlineData(31337)]
    public void Random_writes_from_scripts_are_all_put_back_by_Stop(int seed)
    {
        using var level = new ScriptLevel();
        OneOfEverything.AddTo(level);
        string run = $"seed {seed}";

        // A script on every node, so every kind of node is written to
        // through the binding: a script reaches itself, its parent and the
        // root. A script that took a node out of the level would end every
        // script below it, so here a new parent is always a node.
        SceneNode[] nodes = [.. level.Scene.Root.Traverse()];
        var random = new RandomWrites(seed) { Nodes = nodes, Homes = HomesOf(level) };
        foreach (SceneNode node in nodes)
            level.AddScript(node, WritingScript.From(random, WritesPerScript, node), WritingScript.WriterName);

        SceneNode[] built = [.. level.Scene.Root.Traverse()];
        var before = new LevelState(level.Scene);
        var coverage = new WriteCoverage(level.Scene);

        level.Play();
        bool hasFired = false;
        for (int tick = 0; tick < WritingScript.TicksFor(WritesPerScript); tick++)
        {
            hasFired |= OneOfEverything.FireTheWire(level);
            level.Tick();
            coverage.Look();
        }

        hasFired.ShouldBeTrue($"{run}: the relay was never there to fire its wire");
        level.Errors.ShouldBeEmpty(run);
        (int scripts, IReadOnlyDictionary<string, int> taken, int refused) = WritingScript.Tally(level.Printed);
        scripts.ShouldBe(nodes.Length, $"{run}: not every script reached its last line");
        foreach (SettableMember member in ScriptWrites.Settable)
            coverage.Took(member, taken.GetValueOrDefault(member.Name));

        ShouldBeAMix(taken.Values.Sum(), taken.Values.Sum() + refused, run);
        coverage.ShouldBeWhole(run);

        // Then each script makes, clones, moves and destroys, and the
        // scripts that came in with a clone start.
        level.Tick(WritingScript.TreeTicks);

        level.Errors.ShouldBeEmpty(run);
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBeGreaterThan(0, $"{run}: no script made a node");
        level.Scene.Root.Traverse().Count().ShouldNotBe(built.Length, $"{run}: the scripts left the tree as it was");
        ShouldHaveOneWorldBrush(level, random.Homes[1], run);

        level.Stop();

        new LevelState(level.Scene).ShouldBe(before, run);
        level.Scene.Root.Traverse().ShouldBe(built, run);
    }

    // Whatever was written, made, cloned and moved, the level's ground is
    // still its one world brush, where it was: no write made another, took
    // it away or turned a part into one.
    private static void ShouldHaveOneWorldBrush(ScriptLevel level, SceneNode ground, string run)
    {
        SceneNode[] world = [.. level.Scene.Root.Traverse().Where(node => node.IsStaticWorldBrush)];
        world.ShouldBe([ground], run);
        ground.Parent.ShouldBeSameAs(level.Scene.Root, run);
        level.Scene.StaticWorldDirty.ShouldBeFalse(run);
    }

    // The root and the level's world brush, which no script can move.
    private static SceneNode[] HomesOf(ScriptLevel level) => [level.Scene.Root, level.Baseplate];

    // A run in which nothing was made, cloned, moved or destroyed shows
    // nothing of the tree.
    private static void ShouldHaveChangedTheTree(RandomTree tree, ScriptWrites door, string run)
    {
        tree.Made.ShouldBeGreaterThan(0, $"{run}: nothing was made");
        tree.Cloned.ShouldBeGreaterThan(0, $"{run}: nothing was cloned");
        tree.Destroyed.ShouldBeGreaterThan(0, $"{run}: nothing was destroyed");
        tree.Moved.ShouldBeGreaterThan(0, $"{run}: nothing that was made got another parent");
        door.Tree.MadeCount.ShouldBeGreaterThan(0, $"{run}: nothing that was made is still there");
    }

    // One write of a value it takes to every member of every node, so no
    // seed leaves a member or a kind of node out. Hands back how many took.
    private static int WriteToEveryMember(
        ScriptWrites door, WriteCoverage coverage, SceneNode[] nodes, RandomWrites random)
    {
        int taken = 0;
        foreach (SceneNode node in nodes)
        {
            foreach (SettableMember member in ScriptWrites.Settable)
            {
                // Not the value the member reads already, which changes nothing.
                MemberValue value = random.Taken(member).Value;
                while (value == ScriptWrites.Read(node, member.Id))
                    value = random.Taken(member).Value;

                if (coverage.Write(door, node, member, in value))
                    taken++;
            }
        }

        return taken;
    }

    // What each member a script can set reads on a node, in the list's order.
    private static MemberValue[] MembersOf(SceneNode node) =>
        [.. ScriptWrites.Settable.Select(member => ScriptWrites.Read(node, member.Id))];

    // A run where every write is taken, or every one refused, shows little.
    private static void ShouldBeAMix(int taken, int writes, string run) =>
        taken.ShouldBeInRange(writes / 5, writes * 4 / 5, $"{run}: {taken} of {writes} writes were taken");
}
