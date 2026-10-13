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
