--!strict
-- Vectors and frames, made and taken apart.
local level: DataModel = game
local root: Instance = level:GetService("Workspace")

local up = Vector3.new(0, 1, 0)
local corner = Vector3.one * 2 + Vector3.xAxis - Vector3.zAxis / 2
local between: Vector3 = up:Lerp(corner, 0.5)
print(up:Dot(corner), up:Cross(corner).Unit, between.Magnitude, -between, corner // 1, corner.X + corner.y)

local home = CFrame.new(0, 2, 0)
local facing = CFrame.lookAt(home.Position, Vector3.zero)
local turned = facing * CFrame.Angles(0, math.pi / 2, 0)
local there: Vector3 = turned * Vector3.yAxis
local moved: CFrame = turned + up - corner
local rx, ry, rz = moved:ToOrientation()
print(turned:Inverse():ToObjectSpace(home).LookVector, there, rx, ry, rz)
print(CFrame.identity:Lerp(CFrame.fromAxisAngle(up, 1), 0.5):FuzzyEq(CFrame.identity))
print(CFrame.new(up, corner).Rotation.UpVector, moved:PointToWorldSpace(up), moved.X)

if root.Position ~= Vector3.zero then
	warn("the level's root is off the origin", root:GetFullName())
end

local waiting = task.spawn(function()
	task.wait(10)
end)
task.defer(function()
	task.cancel(waiting)
end)
