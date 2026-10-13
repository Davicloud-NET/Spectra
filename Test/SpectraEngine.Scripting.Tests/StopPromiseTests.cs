using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Stop puts a level back as it was. A write to every member a script can
/// set on every kind of node, then random ones, and the level saves to the
/// same bytes after Stop as before Play. The seed is in every failure, and
/// the same seed makes the same writes.
/// </summary>
public sealed class StopPromiseTests
{
    private const int WritesPerPlay = 320;
    private const int WritesPerTick = 16;
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
        var random = new RandomWrites(seed);

        // Twice: the second play starts from what the first Stop left.
        for (int play = 1; play <= 2; play++)
        {
            string run = $"seed {seed}, play {play}";
            var coverage = new WriteCoverage(level.Scene);
            level.Play();
            var door = new ScriptWrites(level.Entities);

            // No tick has run, so nothing but these writes has changed the level.
            int taken = WriteToEveryMember(door, coverage, nodes, random);
            new LevelState(level.Scene).IsSavedAs(before).ShouldBeFalse($"{run}: the writes changed nothing a save holds");

            for (int i = 0; i < WritesPerPlay; i++)
            {
                int node = random.Pick(nodes.Length);
                RandomWrite write = random.Next(nodes[node]);
                MemberValue value = write.PutsBack ? atStart[node][(int)write.Member.Id] : write.Value;
                if (coverage.Write(door, nodes[node], write.Member, in value))
                    taken++;

                if (i % WritesPerTick == WritesPerTick - 1)
                {
                    OneOfEverything.FireTheWire(level);
                    level.Tick();
                }
            }

            ShouldBeAMix(taken, WritesPerPlay + (nodes.Length * ScriptWrites.Settable.Count), run);
            coverage.ShouldBeWhole(run);
            level.Errors.ShouldBeEmpty(run);

            level.Stop();

            new LevelState(level.Scene).ShouldBe(before, run);
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
        // through the binding: a script reaches itself, its parent and the root.
        var random = new RandomWrites(seed);
        SceneNode[] nodes = [.. level.Scene.Root.Traverse()];
        foreach (SceneNode node in nodes)
            level.AddScript(node, WritingScript.From(random, WritesPerScript, node), "Writer");

        var before = new LevelState(level.Scene);
        var coverage = new WriteCoverage(level.Scene);

        level.Play();
        for (int tick = 0; tick < WritingScript.TicksFor(WritesPerScript); tick++)
        {
            OneOfEverything.FireTheWire(level);
            level.Tick();
            coverage.Look();
        }

        level.Errors.ShouldBeEmpty(run);
        (int scripts, IReadOnlyDictionary<string, int> taken, int refused) = WritingScript.Tally(level.Printed);
        scripts.ShouldBe(nodes.Length, $"{run}: not every script reached its last line");
        foreach (SettableMember member in ScriptWrites.Settable)
            coverage.Took(member, taken.GetValueOrDefault(member.Name));

        ShouldBeAMix(taken.Values.Sum(), taken.Values.Sum() + refused, run);
        coverage.ShouldBeWhole(run);

        level.Stop();

        new LevelState(level.Scene).ShouldBe(before, run);
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
