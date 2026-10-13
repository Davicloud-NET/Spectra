--!strict
-- One mistake a line. After each arrow is what the checker has to say of it.
local lava = workspace:WaitForChild("Lava") :: Part
local coin = workspace:WaitForChild("Coin") :: MeshPart
local lamp = workspace:WaitForChild("Lamp") :: PointLight

lava.Touched:Connect(function(hit: Part) end) --> '(Character) -> ()'
lava.Touched:Connect(function(character) character:Destroy() end) --> Destroy
lava.Touched:Connect(function(character) print(character.Parent) end) --> Parent
coin.TouchEnded:Connect(5) --> but got 'number'
lava.Touched:Conect(print) --> Conect
lava.Touched = nil --> old: 'RBXScriptSignal<Character>', but got 'nil' --> new: Touched of type 'Part' is read-only
lava.Touched:Fire() --> Key 'Fire' not found
lamp.Touched:Connect(print) --> Cannot call a value of type Instance

local hit: Part = coin.Touched:Wait() --> 'Part', but got 'Character'
print(hit)

print(Color3.toHSV(Vector3.one)) --> 'Color3', but got 'Vector3'
print(Color3.toHSV()) --> expects 1 argument, but none are specified
