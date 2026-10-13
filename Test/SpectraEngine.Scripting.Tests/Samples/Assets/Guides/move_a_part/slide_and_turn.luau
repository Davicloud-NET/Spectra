-- Slides the part up and down, and turns it as it goes.
local RunService = game:GetService("RunService")
local part = script.Parent
local home = part.Position
local seconds = 0

RunService.Heartbeat:Connect(function(step)
	seconds += step
	local place = home + Vector3.new(0, math.sin(seconds), 0)
	part.CFrame = CFrame.new(place) * CFrame.Angles(0, seconds, 0)
end)
