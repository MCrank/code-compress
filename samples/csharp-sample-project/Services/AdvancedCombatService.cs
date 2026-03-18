using System;
using GameProject.Models;

namespace GameProject.Services;

/// <summary>
/// Enhanced combat service with critical hit mechanics.
/// Demonstrates: virtual/override, sealed method.
/// </summary>
public class AdvancedCombatService : CombatService
{
    private readonly double _criticalMultiplier;

    public AdvancedCombatService(Random random, double criticalMultiplier)
        : base(random)
    {
        _criticalMultiplier = criticalMultiplier;
    }

    /// <summary>
    /// Overrides base damage calculation with critical hit chance.
    /// </summary>
    public override int CalculateDamage(Player attacker)
    {
        var baseDamage = base.CalculateDamage(attacker);
        return (int)(baseDamage * _criticalMultiplier);
    }

    public bool IsCriticalHit(int roll) => roll >= 18;
}
