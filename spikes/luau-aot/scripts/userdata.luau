-- Holds host objects so the host can test who keeps them alive.
local module = {}
local held = nil

function module.hold(object)
    held = object
end

function module.use(amount: number): number
    held:add(amount)
    return held:get()
end

function module.drop()
    held = nil
end

-- Creates and drops many host objects.
function module.churn(create, count: number)
    for _ = 1, count do
        local object = create()
        object:add(1)
    end
end

return module
