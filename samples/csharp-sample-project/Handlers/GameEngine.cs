using System;
using System.Threading.Tasks;
using GameProject.Models;
using GameProject.Services;

namespace GameProject.Handlers;

/// <summary>
/// Main game engine using primary constructor for dependency injection.
/// Demonstrates: class primary constructor (C# 12+), event usage.
/// </summary>
public class GameEngine(ICombatService combat, InventoryService inventory)
{
    /// <summary>
    /// Fired when a round completes.
    /// </summary>
    public event GameEventHandler? OnRoundComplete;

    public async Task<string> RunRoundAsync(Player attacker, Player defender)
    {
        var damage = await combat.ProcessAttackAsync(attacker, defender);
        OnRoundComplete?.Invoke("RoundComplete", damage);
        return $"Round complete: {damage} damage dealt";
    }

    public int GetInventoryCount(Inventory inv) => inventory.GetItemCount(inv);
}
