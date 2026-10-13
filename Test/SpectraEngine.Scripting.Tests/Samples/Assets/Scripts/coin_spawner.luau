-- Lays a row of coins, one every two seconds. A coin the player walks into
-- is gone and counts a point.
local Score = require("./lib/score")

local template = workspace:WaitForChild("Coin")
local spawner = script.Parent

for count = 1, 10 do
	task.wait(2)

	local coin = template:Clone()
	coin.Name = "Coin" .. count
	coin.Position = spawner.Position + Vector3.new(count * 2, 1, 0)
	coin.Touched:Connect(function()
		Score.add(1)
		coin:Destroy()
	end)
	coin.Parent = workspace
end
