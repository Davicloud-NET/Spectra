using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Entities;
using static SpectraEngine.Scripting.Tests.PlayerScripts;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The site's page on making things while a level plays, held to the code:
/// a part and a light are made, coins are cloned and destroyed, and two
/// scripts share one module, each as the page says.
/// </summary>
public sealed class MakeThingsGuideTests
{
    private const string Folder = "Guides/make_things/";

    // The paths the page gives its files. A path that is asked for has to be real.
    private const string Module = "Scripts/lib/score.luau";
    private const string Spawner = "Scripts/coin_spawner.luau";
    private const string Board = "Scripts/score_board.luau";
    private const string WrongPath = ScriptSamples.MistakesFolder + "/score_board_path.luau";

    // Ten coins, one every two seconds.
    private const int TicksToLayTheRow = 10 * 120;
    private const int Patience = 900;

    private static readonly SitePage Page = SitePage.At("guides/make-things-while-the-level-plays.mdx");

    private static string Code(string path) => ScriptSamples.At(path).Code;

    [Fact]
    public void The_page_shows_its_samples_in_the_order_a_person_writes_them()
    {
        Page.Blocks("luau").ShouldBe(
        [
            Code(Folder + "new_part.luau"), Code(Folder + "coins.luau"), Code(Module), Code(Spawner), Code(Board),
            Code(WrongPath),
        ]);
    }

    [Fact]
    public void The_page_names_the_files_by_the_paths_its_scripts_ask_for()
    {
        Page.Text.ShouldContain($"`Assets/{Module}`");
        Code(Spawner).ShouldContain("require(\"./lib/score\")");
        Code(Board).ShouldContain("require(\"@assets/Scripts/lib/score\")");
    }

    [Fact]
    public void A_part_and_a_light_are_made_where_the_script_puts_them_and_gone_at_Stop()
    {
        using var level = new ScriptLevel();
        SceneNode marker = level.Part("Marker", new Vector3(5f, 1f, 5f));
        level.ScriptAt("Scripts/Marker.luau", Code(Folder + "new_part.luau"), under: marker);
        int built = level.Scene.NodeCount;

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        SceneNode crate = level.Scene.Root.Children.Single(node => node.Name == "Crate");
        crate.WorldPosition.ShouldBe(new Vector3(5f, 3f, 5f));
        SceneNode lamp = crate.Children.ShouldHaveSingleItem();
        lamp.Light.ShouldNotBeNull();
        lamp.WorldPosition.ShouldBe(new Vector3(5f, 4f, 5f));

        level.Stop();

        level.Scene.NodeCount.ShouldBe(built);
    }

    // The page says a new node is at the origin until it is put somewhere,
    // whatever parent it was made with.
    [Fact]
    public void A_light_made_with_a_parent_stands_at_the_origin_of_the_world()
    {
        using var level = new ScriptLevel();
        level.Part("Crate", new Vector3(5f, 3f, 5f));
        level.AddScript(level.Scene.Root, """
            local lamp = Instance.new("PointLight", workspace.Crate)
            print(lamp.Position, lamp.LocalPosition)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["0, 0, 0 -5, -3, -5"]);
    }

    [Fact]
    public void A_row_of_coins_is_cloned_and_each_is_destroyed_when_the_player_walks_into_it()
    {
        using var level = new ScriptLevel();
        Coin(level);
        SceneNode spawner = level.Scene.Root.CreateChild("Spawner");
        level.ScriptAt("Scripts/Spawner.luau", Code(Folder + "coins.luau"), under: spawner);
        int built = level.Scene.NodeCount;

        level.Play().Tick(TicksToLayTheRow + 1);
        level.Scene.NodeCount.ShouldBe(built + 10);

        for (int tick = 0; tick < Patience && level.Printed.Length < 10; tick++)
            Tick(level, Forward());

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe([.. Enumerable.Range(1, 10).Select(count => $"took Coin{count}")]);
        level.Scene.NodeCount.ShouldBe(built);
        level.Scene.Root.Children.ShouldContain(node => node.Name == "Coin", "the template stays");
    }

    // The page's way to keep a template out of sight.
    [Fact]
    public void A_template_taken_out_of_the_level_is_not_drawn_and_can_still_be_cloned()
    {
        using var level = new ScriptLevel();
        Coin(level);
        level.AddScript(level.Scene.Root, """
            local template = workspace:WaitForChild("Coin")
            template.Parent = nil
            local coin = template:Clone()
            coin.Parent = workspace
            print(workspace:FindFirstChild("Coin") == coin, template.Parent)
            """);
        int built = level.Scene.NodeCount;

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true nil"]);

        level.Stop();

        level.Scene.NodeCount.ShouldBe(built);
        level.Scene.Root.Children.Count(node => node.Name == "Coin").ShouldBe(1);
    }

    [Fact]
    public void Instance_new_refuses_a_class_it_does_not_make_in_the_words_the_page_quotes()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, "Instance.new('Model')");

        level.Play().Tick();

        string refusal = level.Errors.ShouldHaveSingleItem();
        refusal.ShouldStartWith("Unable to create an Instance of type \"Model\". Instance.new makes ");
        Page.Text.ShouldContain($"`{refusal}`");
    }

    [Fact]
    public void Two_scripts_share_the_score_of_one_module_and_the_exit_opens_at_five()
    {
        using var level = new ScriptLevel();
        Coin(level);
        SceneNode exit = level.Part("Exit", new Vector3(30f, 1f, 30f));
        exit.Entity = new EntityData("func_door");
        level.FileAt(Module, Code(Module));
        level.ScriptAt(Spawner, Code(Spawner), under: level.Scene.Root.CreateChild("Spawner"));
        level.ScriptAt(Board, Code(Board));

        level.Play().Tick(TicksToLayTheRow + 1);
        for (int tick = 0; tick < Patience && level.Printed.Length < 5; tick++)
            Tick(level, Forward());

        level.Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.Take(5).ShouldBe(["score: 1", "score: 2", "score: 3", "score: 4", "score: 5"]);
        level.Entities.Entities.OfType<FuncDoor>().Single().TicksTravelled.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_path_with_no_start_is_refused_with_the_sentence_the_page_quotes()
    {
        using var level = new ScriptLevel();
        level.FileAt(Module, Code(Module));
        level.ScriptAt(Board, Code(WrongPath));

        level.Play().Tick();

        ScriptMessage failure = level.Sink.Messages.ShouldHaveSingleItem();
        failure.Severity.ShouldBe(ScriptSeverity.Error);
        failure.Text.ShouldBe(
            "'Scripts/lib/score' is no path to a module. A path starts with ./ or ../, from the file that asks, " +
            "or with @self or @assets");
        Page.Text.ShouldContain($"`{failure.Text}`");
        ScriptSamples.At(WrongPath).MarkedLines.ShouldBe([failure.Line], "the checker and the level name the same line");
    }

    // The sample marks its path for the old solver alone. While that holds,
    // a reader who uses the new one sees no mark and has to be told.
    [Fact]
    public void The_page_names_the_solver_that_passes_its_wrong_path()
    {
        ScriptSample wrong = ScriptSamples.At(WrongPath);

        wrong.Expected(isNewSolver: false).ShouldNotBeEmpty();
        wrong.Expected(isNewSolver: true).ShouldBeEmpty(
            "the new solver reports the page's path now, so the page's sentence on it is out of date");
        Page.Text.ShouldContain("new type solver");
        Page.Text.ShouldContain("`--!strict`");
    }

    // The coin the level was built with: a part the player walks through,
    // out of the player's way.
    private static void Coin(ScriptLevel level) =>
        TouchParts.Box(level, "Coin", new Vector3(0f, 1f, 40f), new Vector3(0.25f, 1f, 2f), isSolid: false);
}
