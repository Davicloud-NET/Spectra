-- Turns the part this script sits under, a little every tick.
local RunService = game:GetService("RunService")
local part = script.Parent

print(part.Name .. " turns on a script")

RunService.Heartbeat:Connect(function(step)
	part.CFrame = part.CFrame * CFrame.Angles(0, step, 0)
end)
