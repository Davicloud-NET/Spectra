-- Remembers the last checkpoint the player walked through. A player who
-- touches lava is put back there.
local CollectionService = game:GetService("CollectionService")

local saved

for _, checkpoint in CollectionService:GetTagged("Checkpoint") do
	checkpoint.Touched:Connect(function()
		saved = checkpoint.Position
		print("reached", checkpoint.Name)
	end)
end

for _, lava in CollectionService:GetTagged("Lava") do
	lava.Touched:Connect(function(character)
		if saved then
			character:Teleport(saved)
		else
			character:Respawn()
		end
	end)
end
