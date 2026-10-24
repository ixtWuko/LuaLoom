local M = {}

local function measure(value, depth, seen)
    if type(value) ~= "table" or seen[value] or depth > 4 then
        return 0, 0
    end

    seen[value] = true
    local entries = 0
    local bytes = 0

    for _, child in pairs(value) do
        entries = entries + 1
        bytes = bytes + 32
        local childEntries, childBytes = measure(child, depth + 1, seen)
        entries = entries + childEntries
        bytes = bytes + childBytes
    end

    return entries, bytes
end

function M.snapshot(top)
    if top == nil or top < 1 then
        top = 20
    end

    local seen = {}
    local rows = {}

    local function add(name, value, kind)
        if type(value) ~= "table" or seen[value] then
            return
        end

        local entries, bytes = measure(value, 0, seen)
        rows[#rows + 1] = {
            name = name,
            entries = entries,
            bytes = bytes,
            kind = kind,
            id = string.match(tostring(value), "0x(%x+)") or "-",
        }
    end

    for key, value in pairs(_G) do
        add(tostring(key), value, "GLOBAL")
    end

    for key, value in pairs(debug.getregistry()) do
        if type(key) == "number" then
            add("registry[" .. key .. "]", value, "REGISTRY")
        end
    end

    table.sort(rows, function(a, b) return a.bytes > b.bytes end)

    local out = {}
    out[#out + 1] = string.format("%-28s %8s %10s %10s %10s", "NAME", "ENTRIES", "BYTES", "TYPE", "ID")
    out[#out + 1] = string.rep("-", 72)

    for i = 1, math.min(#rows, top) do
        local row = rows[i]
        out[#out + 1] = string.format(
            "%-28s %8d %10d %10s %10s",
            row.name,
            row.entries,
            row.bytes,
            row.kind,
            row.id)
    end

    out[#out + 1] = string.format("total tables tracked: %d", #rows)
    return table.concat(out, "\n")
end

return M
