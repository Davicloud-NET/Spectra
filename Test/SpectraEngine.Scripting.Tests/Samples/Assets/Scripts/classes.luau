--!strict
-- Every class of node, as a script names one that it knows is there.
local crate = workspace:WaitForChild("Crate") :: Part
local statue = workspace:WaitForChild("Statue") :: MeshPart
local shapes: { BasePart } = { crate, statue }

local props = workspace:WaitForChild("Props") :: Folder
local main = props:WaitForChild("Main") :: Script
local shared = props:WaitForChild("Shared") :: ModuleScript

local sun = workspace:WaitForChild("Sun") :: DirectionalLight
local bulb = workspace:WaitForChild("Bulb") :: PointLight
local torch = workspace:WaitForChild("Torch") :: SpotLight
local panel = workspace:WaitForChild("Panel") :: SurfaceLight
local porthole = workspace:WaitForChild("Porthole") :: DiscLight
local lights: { Light } = { sun, bulb, torch, panel, porthole }

for _, shape in shapes do
	print(shape.Name, shape:IsA("BasePart"), shape.Position)
end

for _, light in lights do
	light.CFrame = CFrame.lookAt(light.Position, crate.Position)
end

print(main.Parent == props, shared.ClassName)
