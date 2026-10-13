--!strict
-- A door that opens on the third press of a button, with no wire between them.
local button = workspace:WaitForChild("Button") :: func_button
local door = workspace:WaitForChild("Door") :: func_door
local counter = workspace:WaitForChild("Counter") :: math_counter

local presses = 0
button.OnPressed:Connect(function()
	presses += 1
	counter:Add(1)
	if presses == 3 then
		door:Open()
	end
end)

counter.OutValue:Connect(function(count)
	print("the counter says", count + 0)
end)

door.OnFullyOpen:Once(function()
	print(door.Name, "is open at", door.Position)
	task.wait(2)
	door:Close()
end)

door.OnFullyClosed:Wait()
print("and closed again")
