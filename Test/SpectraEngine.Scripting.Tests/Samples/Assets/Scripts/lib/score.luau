-- A module: the score of the level, for every script that asks for it.
local Score = {}

local points = 0
local listeners = {}

-- Adds to the score and tells everyone who asked to hear of it.
function Score.add(amount: number)
	points += amount
	for _, listener in listeners do
		listener(points)
	end
end

-- Calls a function with the new score every time it changes.
function Score.changed(listener: (number) -> ())
	table.insert(listeners, listener)
end

return Score
