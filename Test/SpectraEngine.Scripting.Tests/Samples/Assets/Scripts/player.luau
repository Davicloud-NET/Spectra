--!strict
-- The player, as a checkpoint script reaches it.
local Players: Players = game:GetService("Players")

local player: Player? = Players.LocalPlayer
local character: Character? = player and player.Character
if not player or not character then
	return
end

print(player.Name, "walks at", character.WalkSpeed)

character.WalkSpeed = 6
character.JumpHeight *= 2
character.Velocity = Vector3.new(0, 8, 0)

local start = workspace:FindFirstChild("Start")
if start then
	character:Teleport(start.CFrame * CFrame.Angles(0, math.pi, 0))
	character:Teleport(start.Position + Vector3.yAxis)
end

if character.IsGrounded and character.Position.Y < -50 then
	character:Respawn()
end

print(game.Players == Players, game.Workspace == workspace, character.CFrame.LookVector)
