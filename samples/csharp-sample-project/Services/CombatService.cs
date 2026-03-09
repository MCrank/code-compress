using System;
using System.Threading.Tasks;
using GameProject.Models;

namespace GameProject.Services;

/// <summary>
/// Implementation of combat operations.
/// </summary>
public class CombatService : ICombatService
{
    private readonly Random _random;

    public CombatService(Random random)
    {
        _random = random;
    }

    /// <summary>
    /// Processes an attack between two players.
    /// </summary>
    public async Task<int> ProcessAttackAsync(Player attacker, Player defender)
    {
        var damage = CalculateDamage(attacker);
        await Task.Delay(10);
        return damage;
    }

    public int CalculateDamage(Player attacker)
    {
        return attacker.Level * _random.Next(1, 10);
    }

    public async Task HealAsync(Player target, int amount)
    {
        await Task.Delay(10);
    }

    private int CalculateCriticalHit(int baseDamage)
    {
        return baseDamage * 2;
    }
}
