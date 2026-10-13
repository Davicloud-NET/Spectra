-- Hears the counter count, and opens the door a second after it is full.
local counter = workspace:WaitForChild("Presses")
local door = workspace:WaitForChild("Door")

counter.OutValue:Connect(function(count)
	print("pressed", count, "of 3 times,", 3 - count, "to go")
end)

counter.OnHitMax:Connect(function()
	task.wait(1)
	door:Open()
end)
