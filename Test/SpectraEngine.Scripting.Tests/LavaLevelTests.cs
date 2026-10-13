using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Three scripts that know each other only by a tag and an attribute, played
/// through a play session with no window. One tags every part under a folder
/// as Lava, one counts the lava, and one keeps a score that the first hears of.
/// </summary>
public sealed class LavaLevelTests
{
    private const string Tagger = """
        for _, child in workspace.Pit:GetChildren() do
            if child:IsA("BasePart") then
                child:AddTag("Lava")
            end
        end

        local board = workspace.Board
        board:GetAttributeChangedSignal("Score"):Connect(function()
            print("tagger hears the score is", board:GetAttribute("Score"))
        end)
        """;

    // The way a Roblox script takes in a tag: what has it now, and what gets it later.
    private const string Counter = """
        local CollectionService = game:GetService("CollectionService")
        local listed, heard = 0, 0
        CollectionService:GetInstanceAddedSignal("Lava"):Connect(function(part)
            heard += 1
            part:SetAttribute("Heat", 900)
        end)
        for _, part in CollectionService:GetTagged("Lava") do
            listed += 1
            part:SetAttribute("Heat", 900)
        end

        task.wait()
        local hot = {}
        for _, part in CollectionService:GetTagged("Lava") do
            table.insert(hot, part.Name .. "=" .. part:GetAttribute("Heat"))
        end
        print("counter listed", listed, "and heard of", heard, ":", table.concat(hot, " "))
        workspace.Board:SetAttribute("Lava", listed + heard)
        """;

    private const string Scorer = """
        local board = workspace.Board
        for score = 1, 3 do
            board:SetAttribute("Score", score)
            task.wait()
        end
        print("scorer sees", board:GetAttribute("Lava"), "lava parts")
        """;

    // Scripts start in the order of the tree. Whichever of the two comes
    // first, the counter ends with every lava part: from the list or from
    // the signal.
    [Theory]
    [InlineData(true, "counter listed 3 and heard of 0 : Flow=900 Pool=900 Spout=900")]
    [InlineData(false, "counter listed 0 and heard of 3 : Flow=900 Pool=900 Spout=900")]
    public void Scripts_that_share_a_tag_and_an_attribute_find_each_other(bool taggerStartsFirst, string counted)
    {
        using var level = new ScriptLevel();
        SceneNode pit = level.Scene.Root.CreateChild("Pit");
        level.Part("Flow", new Vector3(3f, 0.5f, 3f), pit);
        level.Part("Pool", new Vector3(4f, 0.5f, 3f), pit);
        pit.CreateChild("Rim");
        level.Part("Spout", new Vector3(5f, 0.5f, 3f), pit);
        level.Part("Board", new Vector3(8f, 2f, 3f));

        if (taggerStartsFirst)
            level.AddScript(level.Scene.Root, Tagger, "Tagger");
        level.AddScript(level.Scene.Root, Counter, "Counter");
        if (!taggerStartsFirst)
            level.AddScript(level.Scene.Root, Tagger, "Tagger");
        level.AddScript(level.Scene.Root, Scorer, "Scorer");
        var before = new LevelState(level.Scene);

        level.Play().Tick(4);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "tagger hears the score is 1",
            counted,
            "tagger hears the score is 2",
            "tagger hears the score is 3",
            "scorer sees 3 lava parts",
        ]);

        // Nothing of it is on a node, so Stop leaves the level as it was built.
        level.Runtime.ShouldNotBeNull().TaggedNodeCount.ShouldBe(3);
        level.Stop();
        level.Runtime.ShouldNotBeNull().TaggedNodeCount.ShouldBe(0);
        new LevelState(level.Scene).ShouldBe(before, "after Stop");
    }
}
