using System.Globalization;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The site's page for a Roblox developer, held to the code: what it says is
/// there is there, what it says is missing fails by name, and its numbers
/// are the engine's.
/// </summary>
public sealed class ComingFromRobloxPageTests
{
    private static readonly string Page = SitePage.At("start/coming-from-roblox.mdx").Text;

    private static ScriptApiDescription Api => LuauScriptApi.Description;

    [Fact]
    public void The_services_the_page_names_are_the_ones_GetService_takes()
    {
        string[] services =
        [
            .. Api.Classes.Single(described => described.Name == "DataModel")
                .Methods.Single(method => method.Name == "GetService")
                .Signatures.Select(signature => signature.Parameters.Single().Type.Trim('"')),
        ];

        services.ShouldBe(["RunService", "Workspace", "Players", "CollectionService"]);
        Page.ShouldContain(
            "`game:GetService` knows four names: `RunService`, `Workspace`, `Players` and `CollectionService`.");
    }

    [Fact]
    public void The_numbers_on_the_page_are_the_engines()
    {
        var tuning = new CharacterTuning();

        Page.ShouldContain(Invariant($"| `WalkSpeed` | 16 | {tuning.WalkSpeed} |"));
        Page.ShouldContain(Invariant($"| `JumpHeight` | 7.2 | {tuning.JumpHeight} |"));
        Page.ShouldContain(Invariant($"the player is {tuning.StandHeight} units tall"));
        Page.ShouldContain($"print {PrintCap.MaxPrintsPerTick} lines a tick");
        LuauHost.DefaultCallLimit.ShouldBe(TimeSpan.FromSeconds(3));
        Page.ShouldContain("stopped after three seconds");
    }

    [Fact]
    public void What_the_page_says_a_script_keeps_from_Roblox_is_in_the_script_api()
    {
        ScriptClassDescription node = Api.Classes.Single(described => described.Name == "Instance");
        string[] members = [.. node.Properties.Select(one => one.Name), .. node.Methods.Select(one => one.Name)];

        foreach (string name in new[]
        {
            "FindFirstChild", "WaitForChild", "GetChildren", "GetDescendants", "IsA", "ClassName", "Name", "Parent",
            "GetFullName", "Clone", "Destroy", "Position", "CFrame", "LocalPosition", "LocalCFrame", "SetAttribute",
            "GetAttribute", "AddTag", "HasTag",
        })
        {
            members.ShouldContain(name);
            Page.ShouldContain($"`{name}`");
        }

        Api.Libraries.Single(library => library.Name == "task").Functions.Select(function => function.Name)
            .ShouldBe(["wait", "defer", "delay", "spawn", "cancel"], ignoreOrder: true);
    }

    // Each line is what a Roblox script would write, and the words the level
    // answers with. The page lists every one as not there.
    [Theory]
    [InlineData("game:GetService('TweenService')", "'TweenService' is not a valid Service name", "`TweenService`")]
    [InlineData("game:GetService('UserInputService')", "'UserInputService' is not a valid Service name", "`UserInputService`")]
    [InlineData("game:GetService('DataStoreService')", "'DataStoreService' is not a valid Service name", "`DataStoreService`")]
    [InlineData("return game.ReplicatedStorage", "ReplicatedStorage is not a valid member of DataModel \"game\"", "`ReplicatedStorage`")]
    [InlineData("return workspace.CurrentCamera", "CurrentCamera is not a valid member of Folder \"Root\"", "`workspace.CurrentCamera`")]
    [InlineData("workspace:Raycast(Vector3.zero, Vector3.yAxis)", "Raycast is not a valid member of Folder \"Root\"", "`workspace:Raycast`")]
    [InlineData("return workspace.ChildAdded", "ChildAdded is not a valid member of Folder \"Root\"", "`ChildAdded`")]
    [InlineData("return part.Changed", "Changed is not a valid member of Part \"North\"", "`Changed`")]
    [InlineData("part:GetPropertyChangedSignal('Name')", "GetPropertyChangedSignal is not a valid member of Part \"North\"", "`GetPropertyChangedSignal`")]
    [InlineData("return part.Anchored", "Anchored is not a valid member of Part \"North\"", "`Anchored`")]
    [InlineData("return part.Size", "Size is not a valid member of Part \"North\"", "`Size`")]
    [InlineData("part.Color = Color3.new(1, 0, 0)", "Color is not a valid member of Part \"North\"", "`Color`")]
    [InlineData("part.Transparency = 1", "Transparency is not a valid member of Part \"North\"", "`Transparency`")]
    [InlineData("return character.Humanoid", "Humanoid is not a valid member of Character \"Character\"", "`Humanoid`")]
    [InlineData("return RunService.RenderStepped", "RenderStepped is not a valid member of RunService \"RunService\"", "`RenderStepped`")]
    [InlineData("Instance.new('BindableEvent')", "Unable to create an Instance of type \"BindableEvent\"", "`BindableEvent`")]
    [InlineData("Instance.new('ScreenGui')", "Unable to create an Instance of type \"ScreenGui\"", "`ScreenGui`")]
    [InlineData("Instance.new('RemoteEvent')", "Unable to create an Instance of type \"RemoteEvent\"", "`RemoteEvent`")]
    [InlineData("_G.score = 1", "attempt to modify a readonly table", "`_G` is read only")]
    public void What_the_page_lists_as_missing_fails_with_an_error_that_names_it(string line, string error, string onThePage)
    {
        string[] printed = House.Ask($$"""
            local RunService = game:GetService("RunService")
            local character = game:GetService("Players").LocalPlayer.Character
            local part = workspace.House.Walls.North
            local ok, why = pcall(function() {{line}} end)
            print(ok, why)
            """);

        printed.ShouldHaveSingleItem().ShouldStartWith("false ");
        printed[0].ShouldContain(error);
        Page.ShouldContain(onThePage);
    }

    [Fact]
    public void The_globals_the_page_says_are_missing_are_nil_and_the_ones_it_names_instead_are_there()
    {
        string[] printed = House.Ask("""
            print(type(shared), type(tick), type(time), type(elapsedTime), type(Random))
            print(type(Enum), type(Vector2), type(UDim2), type(TweenInfo))
            print(type(os.clock), type(os.time), type(math.random))
            """);

        printed.ShouldBe(["nil nil nil nil nil", "nil nil nil nil", "function function function"]);
        Page.ShouldContain("| `tick`, `time`, `elapsedTime`, `Random` | There. | Not there. `os.clock`, `os.time` and `math.random` are. |");
        Page.ShouldContain("| `Enum`, `Vector2`, `UDim2`, `TweenInfo` | There. | Not there. |");
        Page.ShouldContain("there is no `shared`");
    }

    // Studio takes each of these. The page says they are errors here.
    [Theory]
    [InlineData("lamp.Enabled = 1", "boolean expected, got number")]
    [InlineData("lamp.Range = 0", "Range cannot be 0")]
    [InlineData("part:SetAttribute('Owner', workspace)", "Instance is not a supported attribute type")]
    public void What_the_page_says_is_an_error_here_is_one(string line, string error)
    {
        string[] printed = House.Ask($$"""
            local lamp = workspace.House.Lamp
            local part = workspace.House.Walls.North
            print(pcall(function() {{line}} end))
            """);

        printed.ShouldHaveSingleItem().ShouldStartWith("false ");
        printed[0].ShouldContain(error);
    }

    [Fact]
    public void A_light_keeps_a_range_past_what_Roblox_caps_it_at_and_the_root_is_a_folder()
    {
        string[] printed = House.Ask("""
            local lamp = workspace.House.Lamp
            lamp.Range = 1000
            print(lamp.Range, workspace.ClassName, game.Workspace == workspace)
            """);

        printed.ShouldBe(["1000 Folder true"]);
        Page.ShouldContain("`workspace` is of class `Folder`");
    }

    // The page's way to aim a light: a frame looks along its -Z and a light shines along its +Z.
    [Fact]
    public void The_turn_the_page_gives_points_a_frames_positive_Z_at_the_target()
    {
        string[] printed = House.Ask("""
            local at, target = Vector3.new(0, 4, 0), Vector3.new(10, 4, 0)
            local aimed = CFrame.lookAt(at, target) * CFrame.Angles(0, math.pi, 0)
            local toTarget = (target - at).Unit
            print(aimed.ZVector:Dot(toTarget) > 0.999, CFrame.lookAt(at, target).LookVector:Dot(toTarget) > 0.999)
            """);

        printed.ShouldBe(["true true"]);
        Page.ShouldContain("`CFrame.lookAt(light.Position, target) * CFrame.Angles(0, math.pi, 0)`");
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
