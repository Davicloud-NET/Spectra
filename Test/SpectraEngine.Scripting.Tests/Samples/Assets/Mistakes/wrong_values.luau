--!strict
-- One mistake a line. After each arrow is what the checker has to say of it.
local frame = CFrame.new(0, 2, 0)

print(CFrame.new("here")) --> 'Vector3', but got 'string'
print(CFrame.Angles(0, "half", 0)) --> 'number', but got 'string'
print(CFrame.Angles(0, 1)) --> expects 3 arguments, but only 2 are specified
print(CFrame.Angels(0, 0, 0)) --> Angels
print(frame + frame) --> old: 'Vector3', but got 'CFrame' --> new: Operator '+' could not be applied
print(frame:Lerp(Vector3.zero, 0.5)) --> 'CFrame', but got 'Vector3'
frame.Position = Vector3.zero --> new: Position of type 'CFrame' is read-only

print(Vector3.new("up")) --> 'number?', but got 'string'
print(Vector3.zero:Dot(5)) --> 'Vector3', but got 'number'
print(Vector3.one.Lenght) --> Lenght

task.wait("soon") --> 'number?', but got 'string'
task.delay("soon", print) --> 'number', but got 'string'
task.cancel(5) --> 'thread', but got 'number'

wait(1) --> Function 'wait' is deprecated, use 'task.wait' instead
spawn(function() end) --> Function 'spawn' is deprecated, use 'task.spawn' instead
delay(1, function() end) --> Function 'delay' is deprecated, use 'task.delay' instead
