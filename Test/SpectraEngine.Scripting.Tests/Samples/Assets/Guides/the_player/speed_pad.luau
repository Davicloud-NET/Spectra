-- Makes the player faster while they stand on a part tagged SpeedPad.
-- A pad says how fast in its attribute Speed.
local CollectionService = game:GetService("CollectionService")
local Players = game:GetService("Players")

local character = Players.LocalPlayer.Character
local walk = character.WalkSpeed

for _, pad in CollectionService:GetTagged("SpeedPad") do
	pad.Touched:Connect(function()
		character.WalkSpeed = pad:GetAttribute("Speed") or walk * 2
	end)

	pad.TouchEnded:Connect(function()
		character.WalkSpeed = walk
	end)
end
