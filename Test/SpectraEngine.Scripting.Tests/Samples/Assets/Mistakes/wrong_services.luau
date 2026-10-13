--!strict
-- One mistake a line. After each arrow is what the checker has to say of it.
local RunService = game:GetService("RunService")
local Players = game:GetService("Players")

game:GetService("Lighting") --> overloads

RunService.Heartbeat:Connect(function(step: string) end) --> '(number) -> ()'
RunService.Heartbeat:Conect(print) --> Conect
RunService.Stepped:Connect(5) --> but got 'number'
RunService.Heartbeat = nil --> old: 'RBXScriptSignal<number>', but got 'nil' --> new: Heartbeat of type 'RunService' is read-only

local connection = RunService.PostSimulation:Connect(print)
connection:Disconect() --> Disconect
connection.Connected = false --> new: Connected of type 'RBXScriptConnection' is read-only

local player = Players.LocalPlayer
print(player.Name) --> Value of type 'Player?' could be nil

local character = player and player.Character
if character then
	character.WalkSpeed = "fast" --> 'number', but got 'string'
	character.IsGrounded = true --> new: IsGrounded of type 'Character' is read-only
	character:Teleport(5) --> but got 'number'
	character:Respwan() --> Respwan
end
