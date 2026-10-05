-- Numeric work for comparing the interpreter with native code.
local module = {}

function module.mandelbrot(size: number, limit: number): number
    local inside = 0
    for py = 0, size - 1 do
        local y0 = py / size * 2 - 1
        for px = 0, size - 1 do
            local x0 = px / size * 3.5 - 2.5
            local x, y, i = 0, 0, 0
            while x * x + y * y <= 4 and i < limit do
                x, y = x * x - y * y + x0, 2 * x * y + y0
                i += 1
            end
            if i == limit then
                inside += 1
            end
        end
    end
    return inside
end

function module.sieve(n: number): number
    local composite = table.create(n, false)
    local count = 0
    for i = 2, n do
        if not composite[i] then
            count += 1
            for j = i * i, n, i do
                composite[j] = true
            end
        end
    end
    return count
end

function module.isNative(): boolean
    return host_in_native()
end

-- Raises through a stack of native frames, once from Luau and once from the host.
function module.raiseThroughNative(depth: number)
    local function dive(n: number, fail): number
        if n == 0 then
            fail("deep")
        end
        return dive(n - 1, fail) + 1
    end

    local luauOk, luauMessage = pcall(dive, depth, error)
    local hostOk, hostMessage = pcall(dive, depth, host_reject)
    return luauOk, luauMessage, hostOk, hostMessage
end

return module
