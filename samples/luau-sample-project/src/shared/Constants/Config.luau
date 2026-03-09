-- Config: Game configuration constants
-- Central configuration for gameplay parameters

local function createDefaultConfig(): table
    return {
        MAX_PLAYERS = 50,
        MAX_INVENTORY_SIZE = 100,
        MIN_DAMAGE = 1,
        CRIT_MULTIPLIER = 2.0,
        RESPAWN_DELAY = 5,
        MIN_PATH_DISTANCE = 1,
        PATH_STEP_SIZE = 4,
        FLEE_HEALTH_THRESHOLD = 20,
        MAX_ENGAGE_ENEMIES = 3,
        DETECTION_RANGE = 50,
    }
end

local function validateConfig(config: table): boolean
    if not config then
        return false
    end
    if not config.MAX_PLAYERS or config.MAX_PLAYERS < 1 then
        return false
    end
    if not config.MAX_INVENTORY_SIZE or config.MAX_INVENTORY_SIZE < 1 then
        return false
    end
    return true
end

function mergeConfig(base: table, overrides: table): table
    local merged = {}
    for key, value in pairs(base) do
        merged[key] = value
    end
    for key, value in pairs(overrides) do
        merged[key] = value
    end
    return merged
end

local Config = createDefaultConfig()

return Config
