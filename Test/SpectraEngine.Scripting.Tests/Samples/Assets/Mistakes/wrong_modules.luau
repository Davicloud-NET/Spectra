--!strict
-- One mistake a line. After each arrow is what the checker has to say of it.
-- A module carries its types whichever way it is asked for.
local Near = require("./../Scripts/lib/counter")
local FromRoot = require("@assets/Scripts/lib/counter")

print(Near.new("three")) --> 'number', but got 'string'
print(FromRoot.new("three")) --> 'number', but got 'string'
print(FromRoot.new(3).cuont) --> cuont

print(require("./nothing_here")) --> nothing_here
print(require("@assets/Scripts/lib/none")) --> none
