--!strict
-- A kill brick, a checkpoint and a coin: what the player touches.
local Players = game:GetService("Players")

local lava = workspace:WaitForChild("Lava") :: Part
local checkpoint = workspace:WaitForChild("Checkpoint") :: Part
local coin = workspace:WaitForChild("Coin") :: MeshPart

local saved: Vector3? = nil

checkpoint.CanCollide = false
checkpoint.Touched:Connect(function(character: Character)
	saved = checkpoint.Position
	print("checkpoint at", saved, "walking at", character.WalkSpeed)
end)

lava.Touched:Connect(function(character)
	if saved then
		character:Teleport(saved)
	else
		character:Respawn()
	end
end)

local standing: RBXScriptConnection = lava.TouchEnded:Connect(function(character)
	print("off the lava", character.IsGrounded)
end)
print(standing.Connected, lava.Touched == lava.Touched)

coin.Touched:Once(function(character)
	local player = Players.LocalPlayer
	if player and player.Character == character then
		coin.Visible = false
		coin.CanTouch = false
	end
end)

local gold = Color3.fromRGB(255, 200, 40)
local hue, saturation, value = Color3.toHSV(gold)
print(hue + saturation + value)

local first: Character = coin.TouchEnded:Wait()
print(first.Position)
