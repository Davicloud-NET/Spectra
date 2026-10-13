--!strict
-- Making, cloning, moving and destroying nodes while the level plays.
local crate = Instance.new("Part")
crate.Name = "Crate"
crate.Position = Vector3.new(0, 2, 0)
crate.CanCollide = false
crate.Parent = workspace

local lamp = Instance.new("PointLight", crate)
lamp.Brightness = 2
lamp.Range = 6

local shelf = Instance.new("Folder")
shelf.Parent = workspace
crate.Parent = shelf

-- A coin is cloned from one in the level, and gone when the player walks into it.
local template = workspace:WaitForChild("Coin") :: Part
local coin = template:Clone() :: Part
coin.Position += Vector3.new(0, 1, 0)
coin.Touched:Connect(function(character)
	print(character.Position)
	coin:Destroy()
end)
coin.Parent = workspace

-- Out of the level for a while, and back.
shelf.Parent = nil
task.wait(1)
shelf.Parent = workspace

if coin.Parent == nil then
	print("the coin is gone")
end

local spot = Instance.new("SpotLight")
local panel = Instance.new("SurfaceLight")
local sun = Instance.new("DirectionalLight")
local dish = Instance.new("DiscLight")
spot.Range = panel.Range
sun.Enabled = dish.Enabled

local lights: { Light } = { spot, panel, sun, dish }
for _, light in lights do
	light.Parent = crate
end
