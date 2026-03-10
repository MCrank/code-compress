using System;
using System.Threading.Tasks;
using GameProject.Models;
using GameProject.Services;

namespace GameProject.Handlers;

/// <summary>
/// Handles game-level operations and coordinates services.
/// </summary>
public class GameHandler
{
    private readonly ICombatService _combatService;
    private readonly InventoryService _inventoryService;

    public GameHandler(ICombatService combatService, InventoryService inventoryService)
    {
        _combatService = combatService;
        _inventoryService = inventoryService;
    }

    public async Task<(bool Success, string Message)> HandleAttackAsync(Player attacker, Player defender)
    {
        var damage = await _combatService.ProcessAttackAsync(attacker, defender);
        return (true, $"Attack dealt {damage} damage");
    }

    public Player? FindPlayerByName(string name)
    {
        return null;
    }

    /// <summary>
    /// Gets the game status for a player.
    /// </summary>
    public string GetStatus(Player player) => player.IsAlive ? "Active" : "Defeated";
}
