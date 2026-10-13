namespace Spectra.Kitchen.Tests;

// A script that asks about everything a script can ask about today and prints
// each answer on a line of its own, with the name of the node it sits under in
// front. A level puts it under many nodes. What it writes is refused or made,
// and it prints which.
internal static class AskingScript
{
    public const string Path = "Scripts/ask.luau";

    // It waits for the next tick after every third line: all scripts of a
    // level may print a hundred lines a tick between them.
    public const string Text = """
        local RunService = game:GetService("RunService")

        local who = script.Parent.Name
        local lines = 0

        local function say(what, answer)
        	print(who .. " | " .. what .. " | " .. tostring(answer))
        	lines += 1
        	if lines % 3 == 0 then
        		task.wait()
        	end
        end

        local function attempt(what, action)
        	local ok, why = pcall(action)
        	say(what, if ok then "done" else why)
        end

        -- The script and every node above it.
        local node = script
        local up = 0
        while node do
        	say("up " .. up, node.Name .. " is a " .. node.ClassName)
        	say("up " .. up .. " position", tostring(node.Position) .. " local " .. tostring(node.LocalPosition))
        	say("up " .. up .. " frame", tostring(node.CFrame) .. " local " .. tostring(node.LocalCFrame))
        	node = node.Parent
        	up += 1
        end

        say("workspace", workspace.Name .. " is a " .. workspace.ClassName)
        say("one workspace", game.Workspace == workspace and workspace == game:GetService("Workspace"))
        say("above the workspace", workspace.Parent)
        say("as text", tostring(script.Parent) .. " and " .. tostring(script))

        local target = script.Parent

        -- What the node is, and what is around it: by name, by class and by
        -- what a node is. Asked before any script has renamed anything.
        local classes = {
        	"Instance", "BasePart", "Part", "MeshPart", "Light", "PointLight", "SpotLight", "DirectionalLight",
        	"SurfaceLight", "DiscLight", "Folder", "Script", "ModuleScript", "func_door", "logic_relay",
        	"info_player_start", "trigger_multiple",
        }

        local function names(nodes)
        	local list = {}
        	for index, node in nodes do
        		list[index] = node.Name .. " " .. node.ClassName
        	end
        	return #list .. ": " .. table.concat(list, ", ")
        end

        local function path(node)
        	return if node then node:GetFullName() else "nothing"
        end

        local isA = {}
        for _, class in classes do
        	if target:IsA(class) then
        		table.insert(isA, class)
        	end
        end

        say("is a", table.concat(isA, " "))
        say("children", names(target:GetChildren()))
        say("descendants", names(target:GetDescendants()))
        say("full name", script:GetFullName())
        say("child by name", target.Ask == script and target:FindFirstChild("Ask") == script)
        say("first of its name", path(workspace:FindFirstChild(who, true)))
        say("first part below", path(target:FindFirstChildWhichIsA("BasePart", true)))
        say("first light below", path(target:FindFirstChildWhichIsA("Light", true)))
        say("script child", path(target:FindFirstChildOfClass("Script")))
        say("part above", path(script:FindFirstAncestorWhichIsA("BasePart")))
        say("folder above", path(script:FindFirstAncestorOfClass("Folder")))
        say("workspace above", script:FindFirstAncestor(workspace.Name) == workspace)
        say("in the tree", script:IsDescendantOf(workspace) and workspace:IsAncestorOf(script) and not script:IsAncestorOf(target))
        say("a child that is there", target:WaitForChild("Ask") == script)
        say("a child that never comes", target:WaitForChild("Nobody", 0.05))

        attempt("a member no node has", function() return target.Anchored end)
        attempt("a class name written", function() target.ClassName = "Part" end)

        attempt("move up", function() target.Position += Vector3.new(0, 1, 0) end)
        say("after the move", target.Position)
        attempt("turn", function() target.CFrame = target.CFrame * CFrame.Angles(0, math.pi / 2, 0) end)
        say("after the turn", target.CFrame)
        attempt("move under the parent", function() target.LocalPosition = Vector3.new(1, 2, 3) end)
        say("after the local move", target.LocalPosition)
        attempt("frame under the parent", function() target.LocalCFrame = CFrame.new(3, 2, 1) end)
        say("after the local frame", target.LocalCFrame)

        attempt("rename", function() target.Name = target.Name .. " renamed" end)
        say("after the rename", target.Name)
        attempt("a name no node may have", function() target.Name = "" end)

        say("waited", task.wait(0.05))

        local beats = 0
        local connection
        connection = RunService.Heartbeat:Connect(function(step)
        	beats += 1
        	if beats == 3 then
        		connection:Disconnect()
        		print(who .. " | heartbeat | " .. beats .. " beats of " .. step)
        	end
        end)

        -- What the node's entity class gives it: an input is a method and an
        -- output is a signal. A node with no entity has neither.
        local function member(name)
        	local ok, found = pcall(function() return target[name] end)
        	return if not ok then found elseif type(found) == "function" then "a method" else tostring(found)
        end

        for _, name in { "Open", "Trigger", "OnFullyOpen", "OnTrigger" } do
        	say("member " .. name, member(name))
        end
        attempt("an input no class has", function() target:Explode() end)

        if target:IsA("logic_relay") then
        	local heard = 0
        	target.OnTrigger:Connect(function() heard += 1 end)
        	target:Trigger()
        	local atOnce = heard
        	task.wait()
        	say("heard its own output", atOnce .. " at once, " .. heard .. " a tick later")
        	attempt("a value for an input that takes none", function() target:Trigger(1) end)
        end

        if target:IsA("func_door") then
        	target.OnOpen:Once(function() print(who .. " | heard | OnOpen") end)
        	target.OnFullyOpen:Once(function() print(who .. " | heard | OnFullyOpen") end)
        	target:Open()
        	say("sent Open", target.OnOpen == target.OnOpen)
        end
        """;
}
