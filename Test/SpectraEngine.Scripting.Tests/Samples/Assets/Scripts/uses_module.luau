--!strict
-- Asking for a module from beside this file, and from the content root.
local Near = require("./lib/counter")
local FromRoot = require("@assets/Scripts/lib/counter")

local presses = Near.new(3)
local other = FromRoot.new(1)

if presses:add() or other:add() then
	print("reached", presses.count + other.count)
end
