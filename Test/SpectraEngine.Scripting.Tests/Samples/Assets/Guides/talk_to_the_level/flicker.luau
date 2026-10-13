-- Makes the light this script sits under flicker until the door opens.
local lamp = script.Parent
local door = workspace:WaitForChild("Door")

local flickers = true
door.OnOpen:Once(function()
	flickers = false
	lamp.Enabled = true
end)

while flickers do
	lamp.Enabled = not lamp.Enabled
	task.wait(0.1)
end
