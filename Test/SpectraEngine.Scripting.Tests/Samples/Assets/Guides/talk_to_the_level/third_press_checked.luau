--!strict
-- Opens the door on the third press of the button.
local door = script.Parent :: func_door
local button = workspace:WaitForChild("Button") :: func_button

local presses = 0
button.OnPressed:Connect(function()
	presses += 1
	print("press", presses)
	if presses == 3 then
		door:Open()
	end
end)
