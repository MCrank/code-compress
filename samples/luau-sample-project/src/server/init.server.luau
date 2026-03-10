-- Server entry point: Initializes all server-side services
-- Wires up dependencies and starts the game loop

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local CombatService = require(script.Services.CombatService)
local AIService = require(script.Services.AIService)
local InventoryService = require(script.Services.InventoryService)

local function initialize()
    print("Initializing server services...")
    CombatService:GetCombatStats(nil)
    AIService:MakeDecision({})
    InventoryService:GetInventory(nil)
    print("Server services initialized")
end

local function startGameLoop(deltaTime: number)
    for _, npc in pairs(workspace.NPCs:GetChildren()) do
        AIService:UpdateBehavior(npc, deltaTime)
    end
end

local function shutdown()
    print("Shutting down server...")
end

initialize()
