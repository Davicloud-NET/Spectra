using System.Linq;
using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace Spectra.Kitchen.Tests;

// Tags and attributes the level is built with, on a part, a world brush, a
// light, a folder, an entity and a script, and the script that reads them
// back. It asks in its first tick, before any script tags or renames a node,
// and prints later. No other script of the level asks after its names.
internal static class BuiltTagsScript
{
    public const string Path = "Scripts/built.luau";

    // The node the script is. It has a tag too.
    public const string Node = "Built";

    // What every line that is the same on any machine begins with.
    public const string Mark = "Built | ";

    // A turned frame prints its rotation as nine long numbers. Its line is
    // only held to be the same from loose files and from the pack.
    public const string TurnedMark = "Built turned | ";

    // A letter outside ASCII, by its code point.
    private static readonly string DoorInGerman = AskedLevel.DoorInGerman;

    public const string Text = """
        local CollectionService = game:GetService("CollectionService")

        local lines = {}
        local function say(what, answer)
        	table.insert(lines, "Built | " .. what .. " | " .. tostring(answer))
        end

        local function names(nodes)
        	local list = {}
        	for index, node in nodes do
        		list[index] = node.Name
        	end
        	return #list .. ": " .. table.concat(list, " ")
        end

        local function attributes(node)
        	local list = {}
        	for name, value in node:GetAttributes() do
        		table.insert(list, name .. "=" .. typeof(value) .. " " .. tostring(value))
        	end
        	table.sort(list)
        	return table.concat(list, "; ")
        end

        local crate = workspace.Crate
        local wall = workspace.Wall

        say("the lava in the order of the tree", names(CollectionService:GetTagged("Lava")))
        say("a tag with a space", names(CollectionService:GetTagged("Hot stuff")))
        say("the crate's tags", table.concat(crate:GetTags(), ", "))
        say("the crate's attributes", attributes(crate))
        say("a world brush's tags", table.concat(wall:GetTags(), ", "))
        say("a world brush's attributes", attributes(wall))
        say("a light and a folder", tostring(workspace.Sun:HasTag("Sky")) .. " " .. table.concat(workspace.Shelf:GetTags(), ", "))
        say("an entity's attribute beside its keyvalue",
        	tostring(workspace.Relay:GetAttribute("Owner")) .. " " .. typeof(workspace.Relay:GetAttribute("startdisabled")))
        say("a tag no node was built with", names(CollectionService:GetTagged("Ice")))
        local lean = workspace.Shelf:GetAttribute("Lean")
        local turned = "Built turned | " .. typeof(lean) .. " " .. tostring(lean)

        -- The play session's copy: the level's files keep what they have.
        crate:RemoveTag("Lava")
        crate:AddTag("Cooled")
        crate:SetAttribute("Damage", 99)
        crate:SetAttribute("Label", nil)
        wall:SetAttribute("Heat", nil)
        say("after a script changed its copy", names(CollectionService:GetTagged("Lava")) .. ", "
        	.. table.concat(crate:GetTags(), ", ") .. ", " .. tostring(crate:GetAttribute("Damage")) .. " "
        	.. tostring(crate:GetAttribute("Label")) .. " " .. tostring(wall:GetAttribute("Heat")))

        task.wait(0.7)
        for _, line in lines do
        	print(line)
        end
        print(turned)
        """;

    // The line of the script that prints what it gathered.
    private static readonly int PrintLine =
        1 + Text.Split('\n').TakeWhile(line => !line.Contains("print(line)")).Count();

    // What a level with the script prints, each line with the file and line it came from.
    // GetTags is sorted, whatever order a node was built with.
    public static readonly string[] Lines =
    [
        Said("the lava in the order of the tree | 4: Wall Crate Shelf Built"),
        Said("a tag with a space | 1: Crate"),
        Said("the crate's tags | Hot stuff, Lava"),
        Said("the crate's attributes | Damage=number 25; Deadly=boolean true; Glow=Color3 1, 0.5, 0; " +
            "Label=string hot; Push=Vector3 0, 12.5, -1; Rest=CFrame 1, 2, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1; " +
            "fx/glow.on-hit=boolean true"),
        Said("a world brush's tags | Lava"),
        Said($"a world brush's attributes | Heat=number 900.5; Open=boolean false; Sign=string {DoorInGerman}"),
        Said("a light and a folder | true Empty, Lava"),
        Said("an entity's attribute beside its keyvalue | nobody boolean"),
        Said("a tag no node was built with | 0: "),
        Said("after a script changed its copy | 3: Wall Shelf Built, Cooled, Hot stuff, 99 nil nil"),
    ];

    // Marks the level's nodes and adds the script under the root.
    public static void AddTo(Scene scene)
    {
        SceneNode shelf = Named(scene, "Shelf");
        shelf.Tags = TagSet.Of("Lava", "Empty");
        shelf.Attributes = AttributeSet.Empty.With("Lean", MemberValue.Of(new CFrame(
            new Vector3(0.1f, 4f, -2.5f), Quaternion.CreateFromYawPitchRoll(0.7f, -0.3f, 1.9f))));

        SceneNode crate = Named(scene, "Crate");
        crate.Tags = TagSet.Of("Lava", "Hot stuff");
        crate.Attributes = AttributeSet.Empty
            .With("Damage", MemberValue.Of(25d))
            .With("Label", MemberValue.Of("hot"))
            .With("Deadly", MemberValue.Of(true))
            .With("Push", MemberValue.Of(new Vector3(0f, 12.5f, -1f)))
            .With("Glow", MemberValue.Of(new Color3(1f, 0.5f, 0f)))
            .With("Rest", MemberValue.Of(new CFrame(1f, 2f, 3f)))
            .With("fx/glow.on-hit", MemberValue.Of(true));

        SceneNode wall = Named(scene, "Wall");
        wall.Tags = TagSet.Of("Lava");
        wall.Attributes = AttributeSet.Empty
            .With("Heat", MemberValue.Of(900.5d))
            .With("Sign", MemberValue.Of(DoorInGerman))
            .With("Open", MemberValue.Of(false));

        Named(scene, "Sun").Tags = TagSet.Of("Sky");

        SceneNode relay = Named(scene, "Relay");
        relay.Tags = TagSet.Of("Logic");
        relay.Attributes = AttributeSet.Empty.With("Owner", MemberValue.Of("nobody"));

        SceneNode built = scene.Root.CreateChild(Node);
        built.Script = new Script(Path);
        built.Tags = TagSet.Of("Lava");
    }

    private static SceneNode Named(Scene scene, string name) =>
        scene.Root.Traverse().Single(node => node.Name == name);

    private static string Said(string line) => $"Print {Path}:{PrintLine}: {Mark}{line}";
}
