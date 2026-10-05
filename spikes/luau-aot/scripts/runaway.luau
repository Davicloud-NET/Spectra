-- Scripts that never return on their own.
local module = {}

function module.spin()
    while true do
    end
end

-- Catches the stop error and keeps going. The host has to stop it again.
function module.stubborn()
    while true do
        pcall(function()
            while true do
            end
        end)
    end
end

-- Allocates until something stops it.
function module.hoard()
    local kept = {}
    while true do
        kept[#kept + 1] = table.create(1024, 0)
    end
end

function module.count(n: number): number
    local total = 0
    for i = 1, n do
        total += i
    end
    return total
end

return module
