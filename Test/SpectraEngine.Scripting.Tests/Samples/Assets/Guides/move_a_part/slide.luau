-- Slides the part up and down, one unit each way.
local RunService = game:GetService("RunService")
local part = script.Parent
local home = part.Position
local seconds = 0

RunService.Heartbeat:Connect(function(step)
	seconds += step
	part.Position = home + Vector3.new(0, math.sin(seconds), 0)
end)
