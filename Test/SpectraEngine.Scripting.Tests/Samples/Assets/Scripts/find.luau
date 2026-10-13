--!strict
-- Finding nodes the ways a Roblox script does.
local door = workspace.Door
local hinge = workspace.Door.Hinge
local lamp = workspace:FindFirstChild("Lamp")
local key = workspace:WaitForChild("Key")
local maybeKey = workspace:WaitForChild("Key", 2)
local above = script.Parent

print(door.Name, hinge.ClassName, key:GetFullName())

if lamp and lamp:IsA("PointLight") then
	print(lamp.Position.Magnitude)
end

if maybeKey then
	print(maybeKey.CFrame.LookVector)
end

for index, child in workspace:GetChildren() do
	print(index, child.Name, child:IsDescendantOf(game), workspace:IsAncestorOf(child))
end

for _, below in door:GetDescendants() do
	if below:FindFirstAncestorWhichIsA("Part") == door then
		below.Name = "Under" .. door.Name
	end
end

if above then
	local first = above:FindFirstChildOfClass("Script")
	local other = above:FindFirstChildWhichIsA("BasePart", true)
	print(first and first.Parent == above, other and other:FindFirstAncestor("Level"))
	print(above:FindFirstAncestorOfClass("Folder"))
end

-- A node typed as its class still has children by name.
local function handleOf(part: Part): Instance
	return part.Handle
end

print(handleOf(door :: Part).Name)
