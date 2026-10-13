-- Turns the part this script sits under.
local RunService = game:GetService("RunService")
local part = script.Parent

RunService.Heartbeat:Connect(function(step)
	part.CFrame = part.CFrame * CFrame.Angles(0, step, 0)
end)
