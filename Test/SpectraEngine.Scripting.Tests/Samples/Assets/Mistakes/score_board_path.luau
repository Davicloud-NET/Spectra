-- Says the score when it changes.
local Score = require("Scripts/lib/score") --> old: Unknown require

Score.changed(function(points)
	print("score:", points)
end)
