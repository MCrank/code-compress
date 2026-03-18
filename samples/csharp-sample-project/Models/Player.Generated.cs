using System;

namespace GameProject.Models;

/// <summary>
/// Partial extension of Player record. Demonstrates: partial types.
/// </summary>
public partial record Player
{
    /// <summary>
    /// Gets a formatted status string for the player.
    /// </summary>
    public string StatusSummary =>
        $"{DisplayName} - HP: {Health} - Score: {Score} - {(IsAlive ? "Alive" : "Dead")}";

    public bool CanBattle(Player other) =>
        IsAlive && other.IsAlive && Level >= other.Level - 5;
}
