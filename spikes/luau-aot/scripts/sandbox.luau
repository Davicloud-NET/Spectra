-- Run once per script environment. Reports what it could see and change.
local seenBefore = shared_counter
shared_counter = (shared_counter or 0) + 1

local changedLibrary = pcall(function()
    string.upper = function()
        return "hacked"
    end
end)

local changedGlobals = pcall(function()
    _G.host_add = nil
end)

local shadowedPrint = pcall(function()
    print = function() end
end)

return seenBefore, shared_counter, changedLibrary, changedGlobals, shadowedPrint, string.upper("ok")
