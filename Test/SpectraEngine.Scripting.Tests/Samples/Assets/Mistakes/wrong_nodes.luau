--!strict
-- One mistake a line. After each arrow is what the checker has to say of it.
local door = workspace.Door

door.Position = 5 --> 'Vector3', but got 'number'
door.CFrame = door.Position --> 'CFrame', but got 'Vector3'
door.Name = 7 --> 'string', but got 'number'
door.ClassName = "Wall" --> new: ClassName of type 'Instance' is read-only
door.Parent = game --> 'Instance?', but got 'DataModel'
door:FindFirstChild(7) --> 'string', but got 'number'
door:WaitForChild("Hinge", "soon") --> 'number', but got 'string'
door:Explode() --> Cannot call a value of type Instance

local lamp = workspace:FindFirstChild("Lamp")
print(lamp.Name) --> Value of type 'Instance?' could be nil

local above = script.Parent
print(above.Name) --> Value of type 'Instance?' could be nil

local level: DataModel = workspace --> 'DataModel', but got 'Instance'
print(level)
