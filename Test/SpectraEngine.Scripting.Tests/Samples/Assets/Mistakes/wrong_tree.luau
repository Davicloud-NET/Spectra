--!strict
-- One mistake a line. After each arrow is what the checker has to say of it.
local door = workspace.Door

door.Parent = 5 --> 'Instance?', but got 'number'
door.Parent = "workspace" --> 'Instance?', but got 'string'
local model = Instance.new("Model") --> old: but got '"Model"' --> new: None of the overloads for function that accept 1 arguments are compatible
local part = Instance.new("Part", 5) --> old: 'Instance?', but got 'number' --> new: None of the overloads for function that accept 2 arguments are compatible
local copy: Part = door:Clone() --> 'Part', but got 'Instance'
door:Destroy(true) --> expects 1 argument, but 2 are specified
door:Clone(door) --> expects 1 argument, but 2 are specified
Instance.new() --> No overload for function accepts 0 arguments
print(model, part, copy)
