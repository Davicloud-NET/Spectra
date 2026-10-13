-- Sends the player back to the start when they touch this part.
local lava = script.Parent

lava.Touched:Connect(function(character)
	character:Respawn()
end)
