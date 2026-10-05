-- Loops for the cost measurements.
local module = {}

function module.add(a: number, b: number): number
    return a + b
end

function module.empty(n: number)
    for _ = 1, n do
    end
end

function module.call(f, n: number)
    for _ = 1, n do
        f(1, 2)
    end
end

function module.raise(n: number)
    for _ = 1, n do
        pcall(error, "x")
    end
end

function module.raiseHost(f, n: number)
    for _ = 1, n do
        pcall(f)
    end
end

function module.protect(n: number)
    local add = module.add
    for _ = 1, n do
        pcall(add, 1, 2)
    end
end

return module
