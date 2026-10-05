-- A 200-line script shaped like ordinary gameplay code. Used to time the
-- compiler and the loader. It runs, but nothing checks what it computes.
local Inventory = {}
Inventory.__index = Inventory

type Item = { id: string, weight: number, count: number, tags: { string } }
type Slot = { item: Item?, locked: boolean }

local MAX_SLOTS = 24
local MAX_WEIGHT = 120

local function clamp(value: number, low: number, high: number): number
    if value < low then
        return low
    elseif value > high then
        return high
    end
    return value
end

local function hasTag(item: Item, tag: string): boolean
    for _, candidate in item.tags do
        if candidate == tag then
            return true
        end
    end
    return false
end

function Inventory.new(owner: string)
    local self = setmetatable({}, Inventory)
    self.owner = owner
    self.slots = table.create(MAX_SLOTS)
    self.weight = 0
    self.listeners = {}
    for index = 1, MAX_SLOTS do
        self.slots[index] = { item = nil, locked = false }
    end
    return self
end

function Inventory:onChanged(listener: (string, Item?) -> ())
    table.insert(self.listeners, listener)
    return function()
        local index = table.find(self.listeners, listener)
        if index then
            table.remove(self.listeners, index)
        end
    end
end

function Inventory:notify(event: string, item: Item?)
    for _, listener in self.listeners do
        local ok, message = pcall(listener, event, item)
        if not ok then
            warn(`listener failed for {event}: {message}`)
        end
    end
end

function Inventory:findSlot(id: string): number?
    for index, slot in self.slots do
        if slot.item and slot.item.id == id then
            return index
        end
    end
    return nil
end

function Inventory:firstFree(): number?
    for index, slot in self.slots do
        if not slot.item and not slot.locked then
            return index
        end
    end
    return nil
end

function Inventory:canCarry(item: Item, count: number): boolean
    return self.weight + item.weight * count <= MAX_WEIGHT
end

function Inventory:add(item: Item, count: number?): boolean
    local amount = clamp(count or 1, 1, 99)
    if not self:canCarry(item, amount) then
        self:notify("too_heavy", item)
        return false
    end

    local index = self:findSlot(item.id)
    if index then
        local existing = self.slots[index].item
        existing.count += amount
    else
        index = self:firstFree()
        if not index then
            self:notify("full", item)
            return false
        end
        self.slots[index].item = {
            id = item.id,
            weight = item.weight,
            count = amount,
            tags = table.clone(item.tags),
        }
    end

    self.weight += item.weight * amount
    self:notify("added", self.slots[index].item)
    return true
end

function Inventory:remove(id: string, count: number?): number
    local index = self:findSlot(id)
    if not index then
        return 0
    end

    local slot = self.slots[index]
    local item = slot.item
    local amount = math.min(count or item.count, item.count)
    item.count -= amount
    self.weight -= item.weight * amount
    if item.count == 0 then
        slot.item = nil
    end

    self:notify("removed", item)
    return amount
end

function Inventory:count(id: string): number
    local index = self:findSlot(id)
    return if index then self.slots[index].item.count else 0
end

function Inventory:withTag(tag: string): { Item }
    local result = {}
    for _, slot in self.slots do
        if slot.item and hasTag(slot.item, tag) then
            table.insert(result, slot.item)
        end
    end
    table.sort(result, function(a, b)
        return a.id < b.id
    end)
    return result
end

function Inventory:summary(): string
    local parts = {}
    for _, slot in self.slots do
        if slot.item then
            table.insert(parts, string.format("%s x%d", slot.item.id, slot.item.count))
        end
    end
    return `{self.owner}: {table.concat(parts, ", ")} ({self.weight}/{MAX_WEIGHT})`
end

local Recipes = {
    torch = { wood = 1, cloth = 1 },
    bandage = { cloth = 2 },
    spear = { wood = 2, flint = 1 },
    rope = { cloth = 3 },
}

local function craft(inventory, recipeName: string): boolean
    local recipe = Recipes[recipeName]
    if not recipe then
        return false
    end
    for id, needed in recipe do
        if inventory:count(id) < needed then
            return false
        end
    end
    for id, needed in recipe do
        inventory:remove(id, needed)
    end
    return inventory:add({ id = recipeName, weight = 2, count = 1, tags = { "crafted" } })
end

local function demo(): string
    local bag = Inventory.new("player")
    local events = 0
    local disconnect = bag:onChanged(function()
        events += 1
    end)
    bag:add({ id = "wood", weight = 3, count = 1, tags = { "material" } }, 4)
    bag:add({ id = "cloth", weight = 1, count = 1, tags = { "material", "soft" } }, 3)
    bag:add({ id = "flint", weight = 2, count = 1, tags = { "material" } }, 1)
    bag:add({ id = "anvil", weight = 200, count = 1, tags = {} })
    craft(bag, "torch")
    craft(bag, "spear")
    craft(bag, "bandage")
    disconnect()
    return `{bag:summary()} after {events} events, {#bag:withTag("crafted")} crafted`
end

return { Inventory = Inventory, craft = craft, demo = demo }
