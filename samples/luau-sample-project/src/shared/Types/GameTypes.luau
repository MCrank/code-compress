-- GameTypes: Shared type definitions and factory functions for the game
-- Provides data creation and validation utilities

export type PlayerData = {
    name: string,
    level: number,
    health: number,
    maxHealth: number,
    experience: number,
}

export type EnemyData = {
    type: string,
    health: number,
    damage: number,
    speed: number,
}

export type ItemData = {
    id: string,
    name: string,
    category: string,
    stackable: boolean,
    maxStack: number,
}

local function createPlayerData(name: string, level: number): table
    return {
        name = name,
        level = level,
        health = 100,
        maxHealth = 100,
        experience = 0,
    }
end

local function createEnemyData(enemyType: string, health: number, damage: number): table
    return {
        type = enemyType,
        health = health,
        damage = damage,
        speed = 16,
    }
end

local function createItemData(id: string, name: string, category: string): table
    return {
        id = id,
        name = name,
        category = category,
        stackable = true,
        maxStack = 99,
    }
end

function validatePlayerData(data: table): boolean
    if not data then
        return false
    end
    if not data.name or #data.name == 0 then
        return false
    end
    if not data.level or data.level < 1 then
        return false
    end
    return true
end

function validateEnemyData(data: table): boolean
    if not data then
        return false
    end
    if not data.type or #data.type == 0 then
        return false
    end
    if not data.health or data.health <= 0 then
        return false
    end
    return true
end

local GameTypes = {}
GameTypes.createPlayerData = createPlayerData
GameTypes.createEnemyData = createEnemyData
GameTypes.createItemData = createItemData
GameTypes.validatePlayerData = validatePlayerData
GameTypes.validateEnemyData = validateEnemyData

return GameTypes
