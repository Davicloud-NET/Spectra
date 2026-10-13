-- Says how high the part is, once a second.
local part = script.Parent

while true do
	print(string.format("in the world %.2f, under its parent %.2f", part.Position.Y, part.LocalPosition.Y))
	task.wait(1)
end
