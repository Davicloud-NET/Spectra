--!strict
-- One mistake a line. After each arrow is what the checker has to say of it.
local bulb = workspace:WaitForChild("Bulb") :: PointLight
local wall = workspace:WaitForChild("Wall") :: Part
local door = workspace:WaitForChild("Door") :: func_door
local counter = workspace:WaitForChild("Counter") :: math_counter

bulb.Color = Vector3.one --> 'Color3', but got 'Vector3'
bulb.Brightness = "bright" --> 'number', but got 'string'
bulb.Enabled = 1 --> 'boolean', but got 'number'
bulb.Range = bulb.Color --> 'number', but got 'Color3'
wall.CanCollide = "no" --> 'boolean', but got 'string'
wall.Visible = 0 --> 'boolean', but got 'number'

print(Color3.new("red")) --> 'number?', but got 'string'
print(Color3.fromRGB(255, "128", 0)) --> 'number?', but got 'string'
print(Color3.fromHex(255)) --> 'string', but got 'number'
print(Color3.new(1, 0, 0):Lerp(Vector3.zero, 0.5)) --> 'Color3', but got 'Vector3'
print(Color3.new(1, 0, 0) + Color3.new(0, 1, 0)) --> old: '+' not supported by types 'Color3' and 'Color3' --> new: Operator '+' could not be applied
Color3.new(1, 0, 0).R = 0.5 --> new: R of type 'Color3' is read-only

door:Open(1) --> expects 1 argument, but 2 are specified
door:Opne() --> Cannot call a value of type Instance
counter:SetValue("three") --> 'number', but got 'string'
counter:SetValue() --> expects 2 arguments, but only 1 is specified
door.OnOpen:Connect(function(value: number) end) --> '() -> ()', but got '(number) -> ()'
counter.OutValue:Connect(function(value: string) end) --> '(number) -> ()'
door.OnOpen = nil --> old: 'RBXScriptSignal<>', but got 'nil' --> new: OnOpen of type 'func_door' is read-only
door.OnOpen:Fire() --> Key 'Fire' not found
