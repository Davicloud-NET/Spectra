--!strict
-- Has movers/walk.luau move the player, in place of the engine's built-in mover.
-- A mover that fails is taken out, and the built-in one moves the player again.
local Players = game:GetService("Players")
local walk = require("./movers/walk")

local player = Players.LocalPlayer
local character = player and player.Character
if character then
	character:SetMover(walk)
	print("The player moves by a mover in Luau")
end
