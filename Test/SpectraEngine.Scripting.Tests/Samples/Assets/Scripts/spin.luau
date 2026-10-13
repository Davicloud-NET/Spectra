--!strict
-- A part that turns, bobs for two seconds and then drifts back to its parent.
local RunService: RunService = game:GetService("RunService")

local part = script.Parent
assert(part, "this script sits under the node it turns")

print(`{part.Name} turns on a script`)

local turning: RBXScriptConnection = RunService.Heartbeat:Connect(function(step)
	part.CFrame = part.CFrame * CFrame.Angles(0, step, 0)
end)

local bobbing = RunService.Stepped:Connect(function(time, step)
	part.Position += Vector3.new(0, math.sin(time) * step, 0)
end)

task.delay(2, function()
	bobbing:Disconnect()
	print(turning.Connected, bobbing.Connected)
end)

-- The first and the last signal of a tick.
local ends: { RBXScriptSignal<number> } = { RunService.PreSimulation, RunService.PostSimulation }
for _, signal in ends do
	signal:Once(function(step)
		print("a tick is", step, "seconds")
	end)
end

local waited = RunService.Heartbeat:Wait()
part.LocalCFrame = part.LocalCFrame * CFrame.Angles(0, waited, 0)

while true do
	local passed = task.wait(0.5)
	part.LocalPosition = part.LocalPosition:Lerp(Vector3.zero, passed)
end
