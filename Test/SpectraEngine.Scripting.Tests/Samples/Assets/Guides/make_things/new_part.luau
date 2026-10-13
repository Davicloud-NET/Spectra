-- Makes a part two units above the node this script sits under, and a light over it.
local here = script.Parent

local crate = Instance.new("Part")
crate.Name = "Crate"
crate.Position = here.Position + Vector3.new(0, 2, 0)
crate.Parent = workspace

local lamp = Instance.new("PointLight")
lamp.Color = Color3.fromRGB(255, 180, 90)
lamp.Position = crate.Position + Vector3.new(0, 1, 0)
lamp.Parent = crate
