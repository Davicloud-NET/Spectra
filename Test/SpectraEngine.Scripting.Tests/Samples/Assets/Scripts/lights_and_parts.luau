--!strict
-- A lamp that flickers, a wall that lets go, and what a builder typed into a door.
local bulb = workspace:WaitForChild("Bulb") :: PointLight
local sun = workspace:WaitForChild("Sun") :: DirectionalLight
local wall = workspace:WaitForChild("Wall") :: Part
local door = workspace:WaitForChild("Door")

local warm: Color3 = Color3.fromRGB(255, 180, 90)
local cold = Color3.fromHSV(0.6, 0.4, 1)
local between = warm:Lerp(cold, 0.5)
local hue, saturation, value = between:ToHSV()
print(between.R + between.G + between.B, hue, saturation, value, Color3.fromHex("#ffb45a"):ToHex())

bulb.Color = warm
bulb.Brightness *= 2
bulb.Range = 6
sun.Color = Color3.new(1, 0.95, 0.9)

task.spawn(function()
	while true do
		bulb.Enabled = not bulb.Enabled
		task.wait(0.1)
	end
end)

wall.CanCollide = false
wall.CanQuery = wall.CanCollide
wall.CanTouch = false
wall.Visible = not wall.Visible

local speed = door:GetAttribute("speed")
if typeof(speed) == "number" then
	print("the door moves at", speed)
end
