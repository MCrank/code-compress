-- WeaponTypes: Weapon type definitions and utility functions
-- Defines weapon categories, stats, and lookup functions

export type WeaponData = {
    id: string,
    name: string,
    category: string,
    baseDamage: number,
    attackSpeed: number,
    range: number,
}

export type WeaponModifier = {
    name: string,
    damageBonus: number,
    speedBonus: number,
}

local function createWeaponData(id: string, name: string, baseDamage: number): table
    return {
        id = id,
        name = name,
        category = "melee",
        baseDamage = baseDamage,
        attackSpeed = 1.0,
        range = 5,
    }
end

local function createModifier(name: string, damageBonus: number, speedBonus: number): table
    return {
        name = name,
        damageBonus = damageBonus,
        speedBonus = speedBonus,
    }
end

local function applyModifier(weapon: table, modifier: table): table
    return {
        id = weapon.id,
        name = weapon.name,
        category = weapon.category,
        baseDamage = weapon.baseDamage + modifier.damageBonus,
        attackSpeed = weapon.attackSpeed + modifier.speedBonus,
        range = weapon.range,
    }
end

function getWeaponCategory(weaponType: string): string
    if weaponType == "sword" or weaponType == "axe" then
        return "melee"
    elseif weaponType == "bow" or weaponType == "crossbow" then
        return "ranged"
    elseif weaponType == "staff" or weaponType == "wand" then
        return "magic"
    end
    return "unknown"
end

function getWeaponById(weapons: table, weaponId: string): table?
    for _, weapon in ipairs(weapons) do
        if weapon.id == weaponId then
            return weapon
        end
    end
    return nil
end

local WeaponTypes = {}
WeaponTypes.createWeaponData = createWeaponData
WeaponTypes.createModifier = createModifier
WeaponTypes.applyModifier = applyModifier
WeaponTypes.getWeaponCategory = getWeaponCategory
WeaponTypes.getWeaponById = getWeaponById

return WeaponTypes
