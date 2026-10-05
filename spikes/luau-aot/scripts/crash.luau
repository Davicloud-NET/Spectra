-- For the crash cases. Each one runs in its own process.
local module = {}

-- Catches an error raised by a host function, then calls back into the host.
function module.raiseAndContinue(raise, churn)
    local ok, message = pcall(raise)
    churn()
    return ok, message
end

function module.callProtected(f)
    return pcall(f)
end

module.trap = setmetatable({}, {
    __index = function(_, key)
        error("no field " .. key)
    end,
})

return module
