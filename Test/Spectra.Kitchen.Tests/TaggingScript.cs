namespace Spectra.Kitchen.Tests;

// A script that tags every node of the level, lists each tag, and sets and
// reads attributes on an entity and on a world brush. Every line begins with
// the same word, so a test finds them among the rest. It names no attribute
// the asking script reads, and is done before any script renames a node.
internal static class TaggingScript
{
    public const string Path = "Scripts/tags.luau";

    // The node the script is, under the root and after every other node.
    public const string Node = "Tagger";

    // What every line of the script begins with.
    public const string Mark = "Tags | ";

    public const string Text = """
        local CollectionService = game:GetService("CollectionService")

        local function say(what, answer)
        	print("Tags | " .. what .. " | " .. tostring(answer))
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
        	return table.concat(list, ", ")
        end

        local heard = {}
        CollectionService:GetInstanceAddedSignal("Solid"):Connect(function(node)
        	table.insert(heard, node.Name)
        end)

        -- From the last node up, so the order of the tree is not the order of tagging.
        local all = workspace:GetDescendants()
        for index = #all, 1, -1 do
        	local node = all[index]
        	if not node:IsA("Script") and not node:IsA("ModuleScript") then
        		node:AddTag("Level")
        		if node:IsA("BasePart") then
        			node:AddTag("Solid")
        		end
        		if node:IsA("Light") then
        			node:AddTag("Bright")
        		end
        	end
        end
        workspace:AddTag("Level")

        say("every node in the order of the tree", names(CollectionService:GetTagged("Level")))
        say("the parts", names(CollectionService:GetTagged("Solid")))
        say("the lights", names(CollectionService:GetTagged("Bright")))
        say("a tag no node has", names(CollectionService:GetTagged("Nobody")))

        -- An entity: a keyvalue reads until a script sets its name.
        local door = workspace.Door
        say("the door's tags", table.concat(door:GetTags(), " ") .. ", Solid " .. tostring(door:HasTag("Solid")))
        say("a keyvalue before a set", typeof(door:GetAttribute("lip")) .. " " .. tostring(door:GetAttribute("lip")))
        door:SetAttribute("lip", 2)
        door:SetAttribute("distance", nil)
        door:SetAttribute("Score", 7)
        say("the door's attributes", attributes(door))

        -- A world brush: baked, its node has no brush.
        local floor = workspace.Floor
        floor:SetAttribute("Glow", Color3.fromRGB(255, 128, 0))
        floor:SetAttribute("Rest", CFrame.new(1, 2, 3))
        floor:SetAttribute("Flow", Vector3.new(0, 0, 1))
        floor:SetAttribute("Label", "hot")
        floor:SetAttribute("Deadly", true)
        say("a world brush's attributes", attributes(floor))

        local changed = {}
        floor.AttributeChanged:Connect(function(name) table.insert(changed, name) end)
        floor:GetAttributeChangedSignal("Heat"):Connect(function() table.insert(changed, "the heat") end)
        floor:SetAttribute("Heat", 900)
        floor:SetAttribute("Heat", 900)
        floor:SetAttribute("Label", nil)
        door:RemoveTag("Solid")

        task.wait()
        say("heard of the parts", #heard .. ": " .. table.concat(heard, " "))
        say("heard of the changes", table.concat(changed, " "))
        say("the parts without the door", names(CollectionService:GetTagged("Solid")))
        """;

    // What a level with the script prints, each line with the file and line it came from.
    public static readonly string[] Lines =
    [
        Said("every node in the order of the tree | 22: Root Floor Wall BackWall Doorway Crate Door DoorTrigger " +
            $"Relay Start Sun Lamp Spot Panel Disc Bulb Shelf Plank Props Prop {AskedLevel.DoorInGerman} Leaf"),
        Said("the parts | 11: Floor Wall BackWall Doorway Crate Door DoorTrigger Bulb Plank Prop Leaf"),
        Said("the lights | 5: Sun Lamp Spot Panel Disc"),
        Said("a tag no node has | 0: "),
        Said("the door's tags | Level Solid, Solid true"),
        Said("a keyvalue before a set | number 0.05"),
        Said("the door's attributes | Score=number 7, lip=number 2, movedir=Vector3 0, 1, 0, speed=number 100, " +
            "startopen=boolean false, wait=number 4"),
        Said("a world brush's attributes | Deadly=boolean true, Flow=Vector3 0, 0, 1, Glow=Color3 1, 0.501961, 0, " +
            "Label=string hot, Rest=CFrame 1, 2, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1"),
        Said("heard of the parts | 11: Leaf Prop Plank Bulb DoorTrigger Door Crate Doorway BackWall Wall Floor"),
        Said("heard of the changes | Heat the heat Label"),
        Said("the parts without the door | 10: Floor Wall BackWall Doorway Crate DoorTrigger Bulb Plank Prop Leaf"),
    ];

    private static string Said(string line) => $"Print {Path}:4: {Mark}{line}";
}
