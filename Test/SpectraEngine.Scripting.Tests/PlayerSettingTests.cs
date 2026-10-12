using System.Globalization;
using System.Reflection;
using System.Text;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;
using static SpectraEngine.Scripting.Tests.PlayerScripts;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a script can set on the player and what it is refused, and that
/// Stop puts back all of it. The character and its tuning outlive a play
/// session, so a write that recorded nothing would stay for the next one.
/// </summary>
public sealed class PlayerSettingTests
{
    // What a Roblox script would try first.
    private static readonly string[] RobloxNames =
    [
        "Anchored", "AssemblyLinearVelocity", "DisplayName", "Health", "HipHeight", "JumpPower", "MaxHealth",
        "MaxSlopeAngle", "Parent", "RespawnLocation", "Team", "UserId",
    ];

    private static ScriptClassDescription Described(string name) =>
        LuauScriptApi.Description.FindClass(name).ShouldNotBeNull();

    [Fact]
    public void A_write_that_makes_no_sense_is_an_error_at_the_scripts_line_and_changes_nothing()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            print('before')
            character.WalkSpeed = 0 / 0
            print('not reached')
            """);

        level.Play().Tick();

        level.Errors.ShouldBe(["WalkSpeed cannot be NaN: it takes a number from 0 to 10000."]);
        level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error).Line.ShouldBe(4);
        level.Printed.ShouldBe(["before"]);
        Walker(level).Tuning.WalkSpeed.ShouldBe(new CharacterTuning().WalkSpeed);
    }

    [Theory]
    [InlineData("character.WalkSpeed = -1", "WalkSpeed cannot be -1: it takes a number from 0 to 10000.")]
    [InlineData("character.WalkSpeed = math.huge", "WalkSpeed cannot be Infinity: it takes a number from 0 to 10000.")]
    [InlineData("character.WalkSpeed = 'fast'", "Unable to assign property WalkSpeed. number expected, got string")]
    [InlineData("character.WalkSpeed = nil", "Unable to assign property WalkSpeed. number expected, got nil")]
    [InlineData("character.JumpHeight = 0 / 0", "JumpHeight cannot be NaN: it takes a number from 0 to 10000.")]
    [InlineData("character.JumpHeight = {}", "Unable to assign property JumpHeight. number expected, got table")]
    [InlineData(
        "character.Velocity = Vector3.new(0, 0 / 0, 0)",
        "Velocity cannot be (0, NaN, 0): it takes three numbers and no more than 10000 units a second.")]
    [InlineData(
        "character.Velocity = Vector3.new(20000, 0, 0)",
        "Velocity cannot be (20000, 0, 0): it takes three numbers and no more than 10000 units a second.")]
    [InlineData("character.Velocity = 5", "Unable to assign property Velocity. Vector3 expected, got number")]
    [InlineData(
        "character:Teleport(Vector3.new(0, math.huge, 0))",
        "The player cannot be put there: a position must be three numbers, with none of them infinite.")]
    [InlineData(
        "character:Teleport(CFrame.new(0 / 0, 0, 0))",
        "The player cannot be put there: a position must be three numbers, with none of them infinite.")]
    [InlineData("character:Teleport(workspace)", "invalid argument #1 to 'Teleport' (CFrame or Vector3 expected, got Instance)")]
    [InlineData("character:Teleport()", "invalid argument #1 to 'Teleport' (CFrame or Vector3 expected, got nil)")]
    [InlineData("character.Position = Vector3.zero", "Position cannot be set. Move the player with Teleport")]
    [InlineData("character.CFrame = CFrame.identity", "CFrame cannot be set. Move the player with Teleport")]
    [InlineData("character.IsGrounded = true", "Unable to assign property IsGrounded. Property is read only")]
    [InlineData("character.Health = 0", "Health is not a valid member of Character \"Character\"")]
    [InlineData("Players.LocalPlayer.Character = nil", "Unable to assign property Character. Property is read only")]
    [InlineData("Players.LocalPlayer.Name = 'Ann'", "Unable to assign property Name. Property is read only")]
    [InlineData("Players.LocalPlayer = nil", "Unable to assign property LocalPlayer. Property is read only")]
    public void What_a_script_may_not_do_to_the_player_is_refused_in_words_and_changes_nothing(
        string statement, string error)
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            local done, why = pcall(function() {{statement}} end)
            print(done, why)
            """);
        level.Play();
        CharacterSimulation walker = Walker(level);
        CharacterState before = walker.State;
        string tuning = LongLived(walker);

        // The entities alone, so nothing but the script has run.
        level.Entities.Tick(ScriptLevel.Dt);

        level.Errors.ShouldBeEmpty();
        level.Printed.Single().ShouldStartWith("false ");
        level.Printed.Single().ShouldEndWith(error);
        walker.State.ShouldBe(before);
        LongLived(walker).ShouldBe(tuning);
        walker.TryTakeTeleport(out _).ShouldBeFalse();
    }

    // Only an assignment is tried. A method that changes the player is not
    // seen here: the test below calls those.
    [Fact]
    public void A_script_can_set_on_the_players_objects_what_the_description_says_and_nothing_else()
    {
        string[] classes = ["Players", "Player", "Character"];
        var names = new SortedSet<string>(RobloxNames, StringComparer.Ordinal);
        foreach (string owner in classes)
            names.UnionWith(LuauScriptApi.Description.MemberNamesOf(owner));

        const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance;
        names.UnionWith(typeof(CharacterTuning).GetProperties(Members).Select(property => property.Name));
        names.UnionWith(typeof(CharacterState).GetFields(Members).Select(field => field.Name));
        names.UnionWith(typeof(CharacterSimulation).GetProperties(Members).Select(property => property.Name));
        names.ShouldContain("StepHeight", "the properties of CharacterTuning were not found");
        names.ShouldContain("GroundNormal", "the fields of CharacterState were not found");

        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            local names = { {{string.Join(", ", names.Select(name => $"\"{name}\""))}} }
            local values = { 1, true, "text", workspace, {}, print, Vector3.new(1, 2, 3), CFrame.new(1, 2, 3) }
            local function takes(target, name)
                local took = pcall(function() target[name] = nil end)
                for _, value in values do
                    if pcall(function() target[name] = value end) then took = true end
                end
                return took
            end
            for label, target in { Players = Players, Player = Players.LocalPlayer, Character = character } do
                for _, name in names do
                    if takes(target, name) then print(label .. "." .. name) end
                end
            end
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.Order(StringComparer.Ordinal).ShouldBe(
            classes
                .SelectMany(owner => Described(owner).Properties
                    .Where(property => property.CanSet)
                    .Select(property => $"{owner}.{property.Name}"))
                .Order(StringComparer.Ordinal),
            "what a script could set on the player's objects, against what the description says it can");
        Described("Character").Properties.Count(property => property.CanSet).ShouldBeGreaterThan(2);
    }

    [Fact]
    public void Everything_a_script_set_on_the_player_is_back_after_Stop_and_the_next_Play_starts_clean()
    {
        using var level = new ScriptLevel();
        CharacterSimulation walker = Walker(level);
        (string script, string[] says) = SetEverything();
        level.AddScript(level.Scene.Root, script);

        // An edit made outside play. No Stop may take it back.
        walker.Tuning.WalkSpeed = 6f;
        string authored = LongLived(walker);
        Dictionary<string, string> tuning = TuningOf(walker.Tuning);

        level.Play();
        CharacterState atStart = walker.State;
        Tick(level, Forward(0.3f), 30);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(says);
        ShouldHaveChangedTheTuning(walker.Tuning, tuning);
        walker.State.Position.ShouldNotBe(atStart.Position);

        level.Stop();

        LongLived(walker).ShouldBe(authored, "what outlives a play session, after the first Stop");

        // Another edit between two plays, of a member a script sets and of one it cannot.
        walker.Tuning.WalkSpeed = 7f;
        walker.Tuning.StepHeight = 0.6f;
        string edited = LongLived(walker);

        level.Play();

        // Nothing of the first play is left: not where the player was, not a teleport no view took.
        walker.State.ShouldBe(atStart);
        walker.Yaw.ShouldBe(walker.SpawnYaw);
        walker.TryTakeTeleport(out _).ShouldBeFalse();
        LongLived(walker).ShouldBe(edited);

        Tick(level, Forward(0.3f), 30);
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe([.. says, .. says]);
        LongLived(walker).ShouldNotBe(edited);

        level.Stop();

        LongLived(walker).ShouldBe(edited, "what outlives a play session, after the second Stop");
    }

    // A script that sets every property of the character the description
    // says a script can set, to another value than it has, then calls the
    // methods that move the player. With it, the lines it prints.
    private static (string Script, string[] Says) SetEverything()
    {
        var script = new StringBuilder(Character).AppendLine();
        var says = new List<string>();
        foreach (ScriptPropertyDescription property in Described("Character").Properties.Where(property => property.CanSet))
        {
            string value = property.Type switch
            {
                "number" => $"character.{property.Name} + 1.5",
                "Vector3" => "Vector3.new(1.5, 6, -2.5)",
                "boolean" => $"not character.{property.Name}",
                "CFrame" => "CFrame.new(3, 4, 5)",
                _ => throw new NotSupportedException(
                    $"The test has no value of the type {property.Type} for {property.Name}. Give it one, " +
                    "or a write of that member is never checked against Stop."),
            };

            script.AppendLine(CultureInfo.InvariantCulture, $"character.{property.Name} = {value}");
            script.AppendLine(CultureInfo.InvariantCulture, $"print('set {property.Name}')");
            says.Add($"set {property.Name}");
        }

        script.AppendLine("character:Respawn()").AppendLine("character:Teleport(CFrame.new(7, 3, -7) * CFrame.Angles(0, 2, 0))");
        return (script.ToString(), [.. says]);
    }

    // A settable property named as a member of the tuning is kept there.
    // One that reads as before is a write that went nowhere.
    private static void ShouldHaveChangedTheTuning(CharacterTuning tuning, Dictionary<string, string> before)
    {
        string[] kept =
        [
            .. Described("Character").Properties
                .Where(property => property.CanSet && before.ContainsKey(property.Name))
                .Select(property => property.Name),
        ];

        kept.ShouldNotBeEmpty();
        Dictionary<string, string> now = TuningOf(tuning);
        foreach (string name in kept)
            now[name].ShouldNotBe(before[name], name);
    }

    // Every member of the tuning by its name, as text that reads back the same.
    private static Dictionary<string, string> TuningOf(CharacterTuning tuning) =>
        typeof(CharacterTuning).GetProperties().ToDictionary(
            property => property.Name,
            property => Convert.ToString(property.GetValue(tuning), CultureInfo.InvariantCulture) ?? "");

    // Everything of the character that outlives a play session, as text:
    // every member of its tuning, its mover and where it falls out.
    private static string LongLived(CharacterSimulation walker)
    {
        var text = new StringBuilder();
        foreach ((string name, string value) in TuningOf(walker.Tuning).OrderBy(member => member.Key, StringComparer.Ordinal))
            text.Append(name).Append(" = ").Append(value).Append('\n');

        text.Append("Mover = ").Append(walker.Mover.GetType().Name).Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"FallOutHeight = {walker.FallOutHeight}\n");
        return text.ToString();
    }
}
