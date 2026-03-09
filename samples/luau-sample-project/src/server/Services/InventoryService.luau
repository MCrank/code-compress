-- InventoryService: Manages player inventories
-- Provides CRUD operations for items and stack management

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local GameTypes = require(ReplicatedStorage.Shared.Types.GameTypes)
local Config = require(ReplicatedStorage.Shared.Constants.Config)

local InventoryService = {} :: InventoryService

function InventoryService:AddItem(player: Player, itemId: string, quantity: number): boolean
    local inventory = self:GetInventory(player)
    if #inventory >= Config.MAX_INVENTORY_SIZE then
        return false
    end
    local existing = self:FindItem(inventory, itemId)
    if existing then
        existing.quantity = existing.quantity + quantity
    else
        table.insert(inventory, { id = itemId, quantity = quantity })
    end
    self:SaveInventory(player, inventory)
    return true
end

function InventoryService:RemoveItem(player: Player, itemId: string, quantity: number): boolean
    local inventory = self:GetInventory(player)
    local item = self:FindItem(inventory, itemId)
    if not item or item.quantity < quantity then
        return false
    end
    item.quantity = item.quantity - quantity
    if item.quantity <= 0 then
        self:RemoveFromList(inventory, itemId)
    end
    self:SaveInventory(player, inventory)
    return true
end

function InventoryService:GetInventory(player: Player): table
    local data = player:GetAttribute("Inventory")
    if not data then
        return {}
    end
    return data
end

function InventoryService:HasItem(player: Player, itemId: string): boolean
    local inventory = self:GetInventory(player)
    return self:FindItem(inventory, itemId) ~= nil
end

function InventoryService:GetItemCount(player: Player, itemId: string): number
    local inventory = self:GetInventory(player)
    local item = self:FindItem(inventory, itemId)
    if item then
        return item.quantity
    end
    return 0
end

function InventoryService:ClearInventory(player: Player)
    player:SetAttribute("Inventory", {})
end

function InventoryService:TransferItem(from: Player, to: Player, itemId: string, quantity: number): boolean
    if not self:HasItem(from, itemId) then
        return false
    end
    local success = self:RemoveItem(from, itemId, quantity)
    if success then
        self:AddItem(to, itemId, quantity)
    end
    return success
end

local function validateItemId(itemId: string): boolean
    if not itemId or #itemId == 0 then
        return false
    end
    return true
end

function InventoryService:FindItem(inventory: table, itemId: string): table?
    for _, item in ipairs(inventory) do
        if item.id == itemId then
            return item
        end
    end
    return nil
end

function InventoryService:RemoveFromList(inventory: table, itemId: string)
    for i, item in ipairs(inventory) do
        if item.id == itemId then
            table.remove(inventory, i)
            return
        end
    end
end

function InventoryService:SaveInventory(player: Player, inventory: table)
    player:SetAttribute("Inventory", inventory)
end

return InventoryService
