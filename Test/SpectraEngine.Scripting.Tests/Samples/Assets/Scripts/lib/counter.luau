--!strict
-- A module: a counter that says when it has reached its goal.
local Counter = {}
Counter.__index = Counter

export type Counter = typeof(setmetatable({} :: { count: number, goal: number }, Counter))

function Counter.new(goal: number): Counter
	return setmetatable({ count = 0, goal = goal }, Counter)
end

function Counter.add(self: Counter): boolean
	self.count += 1
	return self.count >= self.goal
end

return Counter
