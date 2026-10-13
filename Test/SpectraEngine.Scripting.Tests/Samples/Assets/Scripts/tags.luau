--!strict
-- Tags and attributes: how one script leaves word for another.
local CollectionService: CollectionService = game:GetService("CollectionService")

local lava = workspace:WaitForChild("Lava") :: Folder
for _, child in lava:GetChildren() do
	if child:IsA("BasePart") then
		child:AddTag("Lava")
	end
end

local function burns(part: Instance)
	part:SetAttribute("Heat", 900)
	part:SetAttribute("Glow", Color3.fromRGB(255, 80, 0))
	part:SetAttribute("Flow", Vector3.new(0, 0, 1))
	part:SetAttribute("Rest", part.CFrame)
	part:SetAttribute("Label", "hot")
	part:SetAttribute("Deadly", true)
end

CollectionService:GetInstanceAddedSignal("Lava"):Connect(burns)
CollectionService:GetInstanceRemovedSignal("Lava"):Connect(function(part: Instance)
	part:SetAttribute("Heat", nil)
	print(part.Name, "cooled down")
end)

for _, part in CollectionService:GetTagged("Lava") do
	burns(part)
end

local board = workspace:WaitForChild("Board")
board.AttributeChanged:Connect(function(attribute: string)
	print(attribute, "is now", board:GetAttribute(attribute))
end)
board:GetAttributeChangedSignal("Score"):Connect(function()
	local score = board:GetAttribute("Score")
	if typeof(score) == "number" and score >= 10 then
		board:RemoveTag("Open")
	end
end)

board:SetAttribute("Score", 0)
board:AddTag("Open")
print(board:HasTag("Open"), #board:GetTags(), CollectionService:HasTag(board, "Open"))

for name, value in board:GetAttributes() do
	print(name, value)
end

CollectionService:AddTag(board, "Scored")
CollectionService:RemoveTag(board, "Scored")
print(table.concat(CollectionService:GetTags(board), ", "))

-- A name may have a period, a hyphen and a slash, as in Roblox.
board:SetAttribute("ui/best.run-1", 12)
board:GetAttributeChangedSignal("ui/best.run-1"):Connect(function()
	print(board:GetAttribute("ui/best.run-1"))
end)

-- GetTags is sorted, whatever order the tags were put on in: Late, then Open.
board:AddTag("Late")
print(table.concat(board:GetTags(), " "))

-- A destroyed node keeps its tags and attributes for a script that holds it.
local ember = Instance.new("Part", lava)
ember:AddTag("Lava")
ember:SetAttribute("Heat", 900)
ember:Destroy()
print(ember.Parent, ember:HasTag("Lava"), ember:GetAttribute("Heat"), #CollectionService:GetTagged("Lava"))
ember:SetAttribute("Heat", 0)
ember:AddTag("Cold")
