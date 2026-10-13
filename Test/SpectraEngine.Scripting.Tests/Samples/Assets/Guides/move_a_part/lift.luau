-- Rides the part up three units and down again, with a second of rest at each end.
local part = script.Parent
local low = part.Position
local high = low + Vector3.new(0, 3, 0)

local function ride(from, to)
	for count = 1, 120 do
		part.Position = from:Lerp(to, count / 120)
		task.wait()
	end
end

while true do
	ride(low, high)
	task.wait(1)
	ride(high, low)
	task.wait(1)
end
