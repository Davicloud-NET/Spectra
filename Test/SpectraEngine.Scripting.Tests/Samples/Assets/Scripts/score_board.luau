-- Says the score when it changes, and opens the exit at five points.
local Score = require("@assets/Scripts/lib/score")

local exit = workspace:WaitForChild("Exit")

Score.changed(function(points)
	print("score:", points)
	if points == 5 then
		exit:Open()
	end
end)
