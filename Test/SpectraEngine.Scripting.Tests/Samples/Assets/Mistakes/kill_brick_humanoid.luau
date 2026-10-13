--!strict
-- A kill brick the way a Roblox script writes one.
local lava = script.Parent :: Part

lava.Touched:Connect(function(hit)
	local humanoid = hit.Parent:FindFirstChild("Humanoid") --> Parent
	if humanoid then
		humanoid.Health = 0
	end
end)
