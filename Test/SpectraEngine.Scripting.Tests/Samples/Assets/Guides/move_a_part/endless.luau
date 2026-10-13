-- A loop with no wait in it.
local part = script.Parent

while true do
	part.Position += Vector3.new(0, 0.001, 0)
end
