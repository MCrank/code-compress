-- CombatService: Handles combat mechanics for the game
-- Manages attacks, damage calculation, and status effects

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local GameTypes = require(ReplicatedStorage.Shared.Types.GameTypes)
local WeaponTypes = require(ReplicatedStorage.Shared.Types.WeaponTypes)
local Config = require(ReplicatedStorage.Shared.Constants.Config)

local CombatService = {} :: CombatService

function CombatService:ProcessAttack(attacker: Player, target: Player, weaponId: string): boolean
    local weapon = WeaponTypes.getWeaponById(weaponId)
    if not weapon then
        return false
    end
    local damage = self:CalculateDamage(weapon.baseDamage, weapon.multiplier)
    self:ApplyDamage(target, damage)
    return true
end

function CombatService:CalculateDamage(baseDamage: number, multiplier: number): number
    local finalDamage = baseDamage * multiplier
    if finalDamage < Config.MIN_DAMAGE then
        return Config.MIN_DAMAGE
    end
    return finalDamage
end

function CombatService:ApplyDamage(target: Player, damage: number)
    local health = target:GetAttribute("Health") or 100
    local newHealth = math.max(0, health - damage)
    target:SetAttribute("Health", newHealth)
    if newHealth <= 0 then
        self:HandleDefeat(target)
    end
end

function CombatService:HandleDefeat(player: Player)
    player:SetAttribute("IsDefeated", true)
    player:SetAttribute("RespawnTime", os.clock() + Config.RESPAWN_DELAY)
end

function CombatService:ApplyEffect(target: Player, effect: string, duration: number)
    local effects = target:GetAttribute("ActiveEffects") or {}
    effects[effect] = os.clock() + duration
    target:SetAttribute("ActiveEffects", effects)
end

function CombatService:GetCombatStats(player: Player): table
    return {
        health = player:GetAttribute("Health") or 100,
        armor = player:GetAttribute("Armor") or 0,
        isDefeated = player:GetAttribute("IsDefeated") or false,
    }
end

local function validateTarget(target: Player): boolean
    if not target then
        return false
    end
    if target:GetAttribute("IsDefeated") then
        return false
    end
    return true
end

local function calculateCriticalHit(baseDamage: number, critChance: number): number
    if math.random() < critChance then
        return baseDamage * Config.CRIT_MULTIPLIER
    end
    return baseDamage
end

return CombatService
