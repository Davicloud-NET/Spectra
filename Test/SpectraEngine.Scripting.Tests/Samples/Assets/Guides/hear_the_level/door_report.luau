-- Says what the door does, and how fast it was built to do it.
local door = script.Parent

print("the door moves", door:GetAttribute("speed"), "units a second")

door.OnOpen:Connect(function()
	print("opening")
end)

door.OnFullyOpen:Once(function()
	print("open for the first time")
end)

while true do
	door.OnFullyClosed:Wait()
	print("closed again")
end
