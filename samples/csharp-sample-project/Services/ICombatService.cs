using System.Threading.Tasks;
using GameProject.Models;

namespace GameProject.Services;

/// <summary>
/// Provides combat-related operations.
/// </summary>
public interface ICombatService
{
    /// <summary>
    /// Processes an attack between two players.
    /// </summary>
    Task<int> ProcessAttackAsync(Player attacker, Player defender);

    /// <summary>
    /// Calculates damage based on attacker stats.
    /// </summary>
    int CalculateDamage(Player attacker);

    Task HealAsync(Player target, int amount);
}
