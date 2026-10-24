local M = {}
local now = lua_loom_clock or os.clock
local active = false
local stats = {}
local stack = {}

local function displayName(info)
    if info.name and info.name ~= "" then
        return info.name
    end

    return string.format("%s:%d", info.short_src or "?", info.linedefined or -1)
end

local function getEntry(info)
    local key = info.func
    if not key then
        return nil
    end

    if info.short_src and string.find(info.short_src, "lua_loom_profiler", 1, true) then
        return nil
    end

    local entry = stats[key]
    if not entry then
        entry = {
            name = displayName(info),
            source = info.short_src or "?",
            line = info.linedefined or -1,
            total = 0,
            count = 0,
        }
        stats[key] = entry
    end

    return entry
end

local function hook(event)
    if event == "call" then
        local info = debug.getinfo(2, "nSf")
        if not info then
            return
        end

        local entry = getEntry(info)
        if not entry then
            return
        end

        entry.count = entry.count + 1
        stack[#stack + 1] = { entry = entry, start = now() }
    elseif event == "return" or event == "tail call" then
        local top = table.remove(stack)
        if top then
            top.entry.total = top.entry.total + (now() - top.start)
        end
    end
end

function M.start()
    if active then
        error("profiling already active")
    end
    stats = {}
    stack = {}
    active = true
    debug.sethook(hook, "cr")
end

function M.stop(sort)
    if not active then
        error("profiling is not active")
    end
    debug.sethook()
    active = false

    local list = {}
    local total = 0
    for _, entry in pairs(stats) do
        list[#list + 1] = entry
        total = total + entry.total
    end

    if sort == "AVERAGE" then
        table.sort(list, function(a, b) return a.total / math.max(a.count, 1) > b.total / math.max(b.count, 1) end)
    elseif sort == "CALLED" then
        table.sort(list, function(a, b) return a.count > b.count end)
    else
        table.sort(list, function(a, b) return a.total > b.total end)
    end

    local out = {}
    out[#out + 1] = string.format("%-32s %6s %12s %12s %8s", "NAME", "CALLS", "TOTAL(s)", "AVERAGE(s)", "PERCENT")
    out[#out + 1] = string.rep("-", 76)
    for i = 1, math.min(#list, 50) do
        local entry = list[i]
        out[#out + 1] = string.format(
            "%-32s %6d %12.6f %12.6f %7.1f%%",
            entry.name,
            entry.count,
            entry.total,
            entry.total / math.max(entry.count, 1),
            entry.total / math.max(total, 1e-9) * 100)
    end

    if #list == 0 then
        out[#out + 1] = "(no samples collected)"
    end

    return table.concat(out, "\n")
end

return M
