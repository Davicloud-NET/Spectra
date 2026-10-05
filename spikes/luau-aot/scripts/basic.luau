-- Functions the host calls by name.
local module = {}

function module.add(a: number, b: number): number
    return a + b
end

function module.describe(name: string, count: number): (string, number)
    return `{name} x{count}`, count * 2
end

function module.callHost(a: number, b: number): number
    return host_add(a, b)
end

function module.greet(name: string)
    return host_greet(name, 21)
end

function module.fail()
    error("boom from Luau")
end

function module.tryHostFailure()
    return pcall(host_throw)
end

function module.hostFailure()
    host_throw()
end

function module.badArgument()
    return host_add("not a number", 1)
end

function module.badTypedArgument()
    return host_add_typed({}, 1)
end

-- A table whose fields cannot be read without running script code.
module.trap = setmetatable({}, {
    __index = function(_, key)
        error("no field " .. key)
    end,
})

return module
