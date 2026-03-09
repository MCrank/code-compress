-- AIService: Manages NPC artificial intelligence and behavior
-- Handles pathfinding, decision making, and state management

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local GameTypes = require(ReplicatedStorage.Shared.Types.GameTypes)
local Config = require(ReplicatedStorage.Shared.Constants.Config)

local AIService = {} :: AIService

function AIService:FindPath(start: Vector3, target: Vector3): table?
    local direction = target - start
    local distance = direction.Magnitude
    if distance < Config.MIN_PATH_DISTANCE then
        return nil
    end
    local waypoints = {}
    local steps = math.ceil(distance / Config.PATH_STEP_SIZE)
    for i = 1, steps do
        table.insert(waypoints, start + direction * (i / steps))
    end
    return waypoints
end

function AIService:MakeDecision(npcState: table): string
    local health = npcState.health or 100
    local nearbyEnemies = npcState.nearbyEnemies or 0
    if health < Config.FLEE_HEALTH_THRESHOLD then
        return "flee"
    end
    if nearbyEnemies > Config.MAX_ENGAGE_ENEMIES then
        return "retreat"
    end
    if nearbyEnemies > 0 then
        return "attack"
    end
    return "patrol"
end

function AIService:UpdateBehavior(npc: Model, deltaTime: number)
    local state = self:GetNPCState(npc)
    local decision = self:MakeDecision(state)
    if decision == "attack" then
        self:ExecuteAttack(npc, state)
    elseif decision == "flee" then
        self:ExecuteFlee(npc, state)
    elseif decision == "patrol" then
        self:ExecutePatrol(npc, deltaTime)
    end
end

function AIService:GetNPCState(npc: Model): table
    return {
        health = npc:GetAttribute("Health") or 100,
        position = npc.PrimaryPart and npc.PrimaryPart.Position or Vector3.zero,
        nearbyEnemies = self:CountNearbyEnemies(npc),
    }
end

function AIService:CountNearbyEnemies(npc: Model): number
    local count = 0
    local position = npc.PrimaryPart and npc.PrimaryPart.Position or Vector3.zero
    for _, enemy in pairs(workspace.NPCs:GetChildren()) do
        if enemy ~= npc then
            local dist = (enemy.PrimaryPart.Position - position).Magnitude
            if dist < Config.DETECTION_RANGE then
                count = count + 1
            end
        end
    end
    return count
end

function AIService:ExecuteAttack(npc: Model, state: table)
    npc:SetAttribute("State", "attacking")
end

function AIService:ExecuteFlee(npc: Model, state: table)
    npc:SetAttribute("State", "fleeing")
end

function AIService:ExecutePatrol(npc: Model, deltaTime: number)
    npc:SetAttribute("State", "patrolling")
end

local function calculateThreatLevel(npcState: table): number
    local healthFactor = 1 - (npcState.health / 100)
    local enemyFactor = npcState.nearbyEnemies * 0.2
    return math.clamp(healthFactor + enemyFactor, 0, 1)
end

local function selectBestTarget(npc: Model, enemies: table): Model?
    local bestTarget = nil
    local bestScore = math.huge
    for _, enemy in ipairs(enemies) do
        local dist = (enemy.PrimaryPart.Position - npc.PrimaryPart.Position).Magnitude
        if dist < bestScore then
            bestScore = dist
            bestTarget = enemy
        end
    end
    return bestTarget
end

return AIService
