namespace Spectra.Kitchen.Tests;

// A script that makes, clones, moves and destroys nodes, and asks a world
// brush for each of those. Every line begins with the same word, so a test
// finds them among the rest. It waits before it starts, so the other scripts
// list the level as it was built, and it destroys every node it made.
internal static class MakingScript
{
    public const string Path = "Scripts/makes.luau";

    // The node the script is, under the root.
    public const string Node = "Maker";

    // What every line of the script begins with.
    public const string Mark = "Makes | ";

    // It waits for the next tick after every few lines, as the asking script
    // does: all scripts of a level share a hundred lines a tick.
    public const string Text = """
        local function say(what, answer)
        	print("Makes | " .. what .. " | " .. tostring(answer))
        end

        -- A copy of this script, which a clone carries into the level, says so and is done.
        if script:GetAttribute("Copy") then
        	say("the script of a clone", script.Parent.Name .. " under " .. script.Parent.Parent.Name)
        	return
        end

        -- The asking scripts rename what they sit under.
        local function named(parent, name)
        	return parent:FindFirstChild(name) or parent:FindFirstChild(name .. " renamed")
        end

        -- The reason with no file and line in front, and with the node called It.
        local function attempt(what, node, action)
        	local ok, why = pcall(action)
        	say(what, if ok then "done" else (why:gsub("^.-:%d+: ", ""):gsub(node.Name, "It", 1)))
        end

        -- A clone with no script below it, so no copied script asks again.
        local function bare(copy)
        	for _, below in copy:GetDescendants() do
        		if below:IsA("Script") then
        			below:Destroy()
        		end
        	end
        	return copy
        end

        local function near(a, b)
        	return (a - b).Magnitude < 1e-3
        end

        task.wait(0.25)
        local floor, shelf, crate = named(workspace, "Floor"), named(workspace, "Shelf"), named(workspace, "Crate")
        local door, lamp = named(workspace, "Door"), named(workspace, "Lamp")
        local prop = named(named(workspace, "Props"), "Prop")
        local built = #workspace:GetChildren()

        local kit = Instance.new("Folder", workspace)
        kit.Name = "Kit"
        local brick = Instance.new("Part")
        brick.Name = "Brick"
        brick.Position = Vector3.new(30, 2, 30)
        brick.Parent = kit
        local glow = Instance.new("PointLight", brick)
        say("made", `{kit.Parent == workspace} {brick.ClassName} {brick.Position} {glow.ClassName} {glow.Brightness} {glow.Range} {glow.Position}`)
        say("in the level", `{brick:IsDescendantOf(game)} {kit:FindFirstChild("Brick") == brick} {#workspace:GetChildren() - built}`)
        task.wait()

        -- A world brush: baked, its node has no brush.
        attempt("clone a world brush", floor, function() return floor:Clone() end)
        attempt("destroy a world brush", floor, function() floor:Destroy() end)
        attempt("move a world brush", floor, function() floor.Parent = kit end)
        task.wait()
        attempt("clone what holds a world brush", shelf, function() return shelf:Clone() end)
        attempt("destroy what holds a world brush", shelf, function() shelf:Destroy() end)
        say("the world brush stays", `{floor.Parent == workspace} {floor:IsDescendantOf(game)} {#kit:GetChildren()}`)
        task.wait()

        local part = bare(crate:Clone())
        say("a cloned part", `{part.ClassName} {part.Parent} {near(part.Position, crate.Position)} {part.CanCollide == crate.CanCollide} {#part:GetChildren()}`)
        local mesh = bare(prop:Clone())
        say("a cloned mesh", `{mesh.ClassName} {mesh:IsA("MeshPart")} {mesh.CFrame:FuzzyEq(prop.CFrame, 1e-3)}`)
        local light = bare(lamp:Clone())
        say("a cloned light", `{light.ClassName} {light.Brightness == lamp.Brightness} {light.Color == lamp.Color} {light.Range == lamp.Range} {light:FindFirstChildOfClass("SpotLight") ~= nil}`)
        task.wait()

        -- A brush entity: its copy runs once it is in the level.
        local gate = bare(door:Clone())
        gate.Name = "Gate of the kit"
        gate.Position += Vector3.new(0, 0, 40)
        local heard = {}
        gate.OnOpen:Connect(function() table.insert(heard, "OnOpen") end)
        for _, node in { part, mesh, light, gate } do
        	node.Parent = kit
        end
        say("a cloned door", `{gate.ClassName} {gate:IsA("Part")} {gate:GetAttribute("speed")} {gate:IsDescendantOf(game)} {#kit:GetChildren()}`)
        attempt("a door that is not running yet", gate, function() gate:Open() end)
        task.wait()
        attempt("a door that runs", gate, function() gate:Open() end)
        task.wait(0.1)
        say("the cloned door said", table.concat(heard, " "))

        -- A script: its copy starts at the end of the tick its node comes into the level.
        local twin = script:Clone()
        twin:SetAttribute("Copy", true)
        say("a cloned script", `{twin.ClassName} {twin:GetAttribute("Copy")} {script:GetAttribute("Copy")}`)
        twin.Parent = brick
        task.wait()

        -- Another parent, and none. The brick stays where it is in the world.
        local at = brick.Position
        brick.Parent = gate
        say("under another parent", `{brick.Parent == gate} {near(brick.Position, at)} {near(brick.LocalPosition, at)}`)
        brick.Parent = nil
        say("under no parent", `{brick.Parent} {brick:IsDescendantOf(game)} {near(brick.Position, at)} {glow.Parent == brick}`)
        brick.Parent = kit
        task.wait()

        kit:Destroy()
        say("destroyed", `{kit.Parent} {brick.Parent} {gate:IsDescendantOf(game)} {#kit:GetChildren()} {brick.Name}`)
        attempt("a parent for what was destroyed", brick, function() brick.Parent = workspace end)
        say("the level as it was built", #workspace:GetChildren() == built)
        """;

    // What a level with the script prints, each line with the file and line it came from.
    public static readonly string[] Lines =
    [
        Said("made | true Part 30, 2, 30 PointLight 1 8 0, 0, 0"),
        Said("in the level | true true 1"),
        Said("clone a world brush | It cannot be cloned: " + OnlyParts("be made")),
        Said("destroy a world brush | It cannot be destroyed: " + OnlyParts("be destroyed")),
        Said("move a world brush | It cannot get another parent: " + OnlyParts("move")),
        Said("clone what holds a world brush | It cannot be cloned: " + OnlyParts("be made")),
        Said("destroy what holds a world brush | It cannot be destroyed: " + OnlyParts("be destroyed")),
        Said("the world brush stays | true true 1"),
        Said("a cloned part | Part nil true true 0"),
        Said("a cloned mesh | MeshPart true true"),
        Said("a cloned light | PointLight true true true true"),
        Said("a cloned door | func_door true 100 true 5"),
        Said("a door that is not running yet | Open was not sent: the entity of \"It\" is not running"),
        Said("a door that runs | done"),
        Said("the cloned door said | OnOpen"),
        Said("a cloned script | Script true nil"),
        Said("the script of a clone | Brick under Kit"),
        Said("under another parent | true true false"),
        Said("under no parent | nil false true true"),

        // Back in the level, the brick's script starts afresh.
        Said("the script of a clone | Brick under Kit"),
        Said("destroyed | nil nil false 0 Brick"),
        Said("a parent for what was destroyed | The Parent property of It is locked: Brick was destroyed"),
        Said("the level as it was built | true"),
    ];

    private static string OnlyParts(string what) =>
        $"a world brush sits at or below it, and only parts can {what} while a level plays. Convert the brush to a part.";

    private static string Said(string line) => $"Print {Path}:2: {Mark}{line}";
}
