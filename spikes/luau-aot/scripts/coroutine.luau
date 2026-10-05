-- Yields twice: once from Luau, once through a host function.
local module = {}

function module.run(start: number): number
    local a = coroutine.yield(start + 1)
    local b = wait(0.5)
    return start + a + b
end

function module.ticker()
    while true do
        coroutine.yield()
    end
end

return module
